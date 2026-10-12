// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceStore;

/// <summary>
/// Partial: "misses on an absent entry and rejects a malformed key hash" (a malformed key throws <c>INVALID_ARGUMENT</c>
/// where upstream answers <c>invalid</c> or writes nothing), "fails closed on unreadable and oversized entries" (the
/// size cap is fixed at 1 MiB, not an option), and "evicts an entry on delete, tolerating absence" (the store has no
/// read-only mode). Not ported: "refuses to write an entry over its byte cap" and "never writes when not writable" (the
/// port's store has no byte cap and no <c>writable</c> flag; read-only is a session mode).
/// </summary>
public sealed class FileCacheStoreTests : IDisposable
{
    private static readonly string Key = new('a', 64);

    private readonly string _directory = CoreTests.TempCache();
    private readonly FileStepCache _store;

    public FileCacheStoreTests()
    {
        _store = new FileStepCache(_directory);
    }

    private static CacheEntry Payload() => new()
    {
        Instruction = "upgrade the plan",
        Route = "/settings",
        Actions = [new RecordedAction { Kind = "tap", Role = "button", Name = "Upgrade" }],
    };

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Round_trips_one_entry_under_the_key_digest()
    {
        _store.Write(Key, Payload());
        Assert.Equal([Key + ".json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
        var read = _store.Read(Key).Entry;
        Assert.NotNull(read);
        Assert.Equal("upgrade the plan", read.Instruction);
        Assert.Equal("/settings", read.Route);
        var action = Assert.Single(read.Actions);
        Assert.Equal(("tap", "button", "Upgrade"), (action.Kind, action.Role, action.Name));
    }

    [Fact]
    public void Misses_on_an_absent_entry_and_throws_on_a_malformed_key_hash()
    {
        var miss = _store.Read(Key);
        Assert.Null(miss.Entry);
        Assert.Equal("no-entry", miss.Reason);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => _store.Read("../escape")).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => _store.Write("../escape", Payload())).Code);
    }

    [Fact]
    public void Fails_closed_on_unreadable_entries_and_entries_over_one_MiB()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, Key + ".json");
        File.WriteAllText(path, "{ torn");
        Assert.Equal("invalid-entry", _store.Read(Key).Reason);
        File.WriteAllText(path, "{\"pad\":\"" + new string('x', 1024 * 1024 + 1) + "\"}");
        var oversized = _store.Read(Key);
        Assert.Null(oversized.Entry);
        Assert.Equal("invalid-entry", oversized.Reason);
    }

    [Fact]
    public void Evicts_an_entry_on_delete_tolerating_absence()
    {
        _store.Write(Key, Payload());
        _store.Delete(Key);
        Assert.Empty(Directory.GetFiles(_directory));
        _store.Delete(Key);
        Assert.Empty(Directory.GetFiles(_directory));
    }
}
