// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace E2E.Tests.TraceCacheConfig;

/// <summary><c>flushStagedTraces</c>: how an attempt claims and settles its cache keys.</summary>
public sealed class FlushStagedTracesTests
{
    private static readonly string KeyA = new('a', 64);
    private static readonly string KeyB = new('b', 64);

    [Fact]
    public void Writes_only_what_a_later_verification_step_confirmed_even_when_the_attempt_passed()
    {
        var store = new MemoryCache();
        store.Write(KeyB, Trace("stale trailing flow"));

        // A trailing act nothing asserted on: the attempt passing is not a check.
        E2ESession.Flush(store, [Recorded(KeyA, "one", verified: true), Recorded(KeyB, "two", verified: false)], preserve: false);

        Assert.Equal([KeyA], store.Entries.Keys);
    }

    [Fact]
    public void Writes_nothing_when_no_verification_step_ever_passed()
    {
        var store = new MemoryCache();

        E2ESession.Flush(store, [Recorded(KeyA, "unchecked", verified: false)], preserve: false);

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Confirms_traces_followed_by_a_later_passed_verification_and_evicts_the_implicated_one()
    {
        var store = new MemoryCache();
        store.Write(KeyB, Trace("stale good flow"));

        E2ESession.Flush(store, [Recorded(KeyA, "confirmed", verified: true), Recorded(KeyB, "implicated", verified: false)], preserve: false);

        Assert.True(store.Entries.ContainsKey(KeyA));
        Assert.False(store.Entries.ContainsKey(KeyB));
    }

    [Fact]
    public void Leaves_a_confirmed_kept_entry_exactly_as_stored_and_evicts_an_unconfirmed_one()
    {
        var store = new MemoryCache();
        store.Write(KeyA, Trace("replayed flow"));
        store.Write(KeyB, Trace("replayed flow"));
        var stored = store.Entries[KeyA];

        E2ESession.Flush(store, [Kept(KeyA, store, verified: true), Kept(KeyB, store, verified: false)], preserve: false);

        // The same bytes: a replay is not a rewrite.
        Assert.Same(stored, store.Entries[KeyA]);
        Assert.False(store.Entries.ContainsKey(KeyB));
    }

    [Fact]
    public void Completes_the_provenance_of_a_confirmed_kept_entry_recorded_before_the_occurrence_fields_and_nothing_else()
    {
        var store = new MemoryCache();
        var recorded = Trace("open billing");
        recorded.Engine = null;
        recorded.ParamsDigest = null;
        recorded.CallIndex = null;
        recorded.Agent = null;
        store.Write(KeyA, recorded);

        E2ESession.Flush(store, [Kept(KeyA, store, verified: true)], preserve: false);

        var expected = Trace("open billing");
        Assert.Equal(JsonSerializer.Serialize(expected), store.Entries[KeyA]);
    }

    [Fact]
    public void Writes_what_was_confirmed_and_leaves_every_unconfirmed_entry_as_stored_when_the_failure_implicates_nothing()
    {
        var keyC = new string('c', 64);
        var store = new MemoryCache();
        store.Write(KeyB, Trace("recorded flow"));
        store.Write(keyC, Trace("recorded flow"));
        var stored = store.Entries[KeyB];

        E2ESession.Flush(
            store,
            [Recorded(KeyA, "confirmed", verified: true), Recorded(KeyB, "re-recorded, unconfirmed", verified: false), Kept(keyC, store, verified: false)],
            preserve: true);

        Assert.True(store.Entries.ContainsKey(KeyA));
        Assert.Equal(stored, store.Entries[KeyB]);
        Assert.Equal(stored, store.Entries[keyC]);
    }

    [Fact]
    public void Claims_a_key_per_agent_and_per_agent_context_so_one_agent_never_replays_anothers_recording()
    {
        var buyer = Claim(Fresh(), "buyer", null);
        var admin = Claim(Fresh(), "admin", null);
        var monthly = Claim(Fresh(), "buyer", "Monthly");

        Assert.Equal(3, new HashSet<string> { buyer, admin, monthly }.Count);
        Assert.Equal(buyer, Claim(Fresh(), "buyer", null));

        // Another agent calling the same instruction first does not renumber this agent's occurrence.
        var shared = Fresh();
        Claim(shared, "admin", null);
        Assert.Equal(buyer, Claim(shared, "buyer", null));
    }

    [Fact]
    public void Claims_distinct_key_hashes_per_occurrence_of_the_same_signature()
    {
        var scope = Fresh();
        var agent = new ResolvedAgent { Name = "default" };

        var first = scope.ClaimKey("open billing", null, agent, out _);
        var repeat = scope.ClaimKey("open billing", null, agent, out _);
        var other = scope.ClaimKey("open billing", new Dictionary<string, object?> { ["fast"] = true }, agent, out _);

        Assert.Matches("^[a-f0-9]{64}$", first);
        Assert.NotEqual(first, repeat);
        Assert.NotEqual(first, other);
    }

    private static CacheEntry Trace(string summary)
    {
        var entry = Step();
        entry.Instruction = summary;
        entry.Route = "/";
        entry.EndRoute = "/customers";
        entry.Actions = [new RecordedAction { Kind = "navigate", Url = "/customers" }];
        return entry;
    }

    // The step every staged entry here was recorded for, whole.
    private static CacheEntry Step()
    {
        return new CacheEntry
        {
            Test = "tests/a.e2e.ts::a",
            Instruction = "open billing",
            Engine = "web",
            ParamsDigest = new string('e', 64),
            CallIndex = 0,
            Agent = "default",
        };
    }

    // An act that ran live and passed, with its recording.
    private static PendingAct Recorded(string key, string summary, bool verified)
    {
        return new PendingAct { Key = key, Completed = true, Verified = verified, Entry = Trace(summary) };
    }

    // An act a replay finished whole: the stored entry is already its flow.
    private static PendingAct Kept(string key, MemoryCache store, bool verified)
    {
        return new PendingAct
        {
            Key = key,
            Completed = true,
            Verified = verified,
            ReadEntry = true,
            ConsumedReplay = true,
            ReplayedWhole = true,
            Entry = store.Read(key).Entry,
            Step = Step(),
        };
    }

    private static AttemptScope Fresh()
    {
        return new AttemptScope
        {
            Session = null!,
            Agents = new Dictionary<string, ResolvedAgent>(),
            Cache = null,
            CacheEnabled = true,
            TestTitle = "billing > case",
            EnginePlatform = "document",
        };
    }

    private static string Claim(AttemptScope scope, string agent, string? context)
    {
        return scope.ClaimKey("approve the order", null, new ResolvedAgent { Name = agent, Context = context }, out _);
    }

    /// <summary>A store that keeps each entry as the JSON text it was written as.</summary>
    private sealed class MemoryCache : IStepCache
    {
        public Dictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);

        public CacheLookup Read(string key)
        {
            return Entries.TryGetValue(key, out var text)
                ? new CacheLookup { Entry = JsonSerializer.Deserialize<CacheEntry>(text) }
                : new CacheLookup { Reason = "no-entry" };
        }

        public void Write(string key, CacheEntry entry)
        {
            Entries[key] = JsonSerializer.Serialize(entry);
        }

        public void Delete(string key)
        {
            Entries.Remove(key);
        }
    }
}
