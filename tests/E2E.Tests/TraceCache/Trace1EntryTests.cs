// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using static E2E.Tests.StepCache.StepTraceSessionTests;

namespace E2E.Tests.TraceCache;

/// <summary>
/// The stored entry, <c>FileStepCache</c> and its schema 1. The port has no <c>createdAt</c>, summaries, executor, action cap,
/// input cap, anchor cap, anchor state check, path cap, or <c>typeSecret</c> action, so "missing createdAt", "unparseable createdAt",
/// "too many actions", "unknown action name", "tap without a target", "type without a value", "select with an empty value",
/// "oversized input value", "empty summary", "oversized summary", "empty descriptor", "missing executor name",
/// "empty anchor descriptor", "too many anchors", "too many gone anchors", "unknown anchor state", "unsorted anchor states",
/// "repeated anchor state", "empty anchor states", and "oversized start path" are not ported.
/// </summary>
public sealed class Trace1EntryTests
{
    private const string Key = "abc123";

    private static readonly JsonSerializerOptions EntryJson = new(JsonSerializerDefaults.Web);

    private static CacheEntry Entry(params RecordedAction[] actions) => new() { Route = "/storage", EndRoute = "/storage", Actions = [.. actions] };

    private static CacheEntry RoundTrip(CacheEntry entry)
    {
        var store = new FileStepCache(CoreTests.TempCache());
        store.Write(Key, entry);
        return store.Read(Key).Entry!;
    }

    private static CacheLookup ReadJson(string json)
    {
        var directory = CoreTests.TempCache();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, Key + ".json"), json);
        return new FileStepCache(directory).Read(Key);
    }

    /// <summary>
    /// Partial port of "round-trips every action variant through JSON": the port's actions are <c>fill</c>, <c>check</c>,
    /// <c>uncheck</c>, <c>clear</c>, <c>scrollTo</c>, and <c>scrollUntil</c> beside the shared ones, with no <c>typeSecret</c>
    /// and no summary, and the entry carries no executor.
    /// </summary>
    [Fact]
    public void Round_trips_every_action_kind_the_port_records_through_the_file_store()
    {
        var target = (Role: "textbox", Name: "Email");
        RecordedAction Action(string kind) => new() { Kind = kind, Role = target.Role, Name = target.Name, TestId = "email" };
        var actions = new[]
        {
            new RecordedAction { Kind = "navigate", Url = "/settings" },
            Action("tap"),
            Action("doubleTap"),
            new RecordedAction { Kind = "fill", Role = target.Role, Name = target.Name, Value = "x" },
            new RecordedAction { Kind = "press", Role = target.Role, Name = target.Name, Key = "Enter" },
            new RecordedAction { Kind = "select", Role = "combobox", Name = "Plan", Value = "Pro" },
            Action("check"),
            Action("uncheck"),
            Action("clear"),
            Action("scrollTo"),
            new RecordedAction { Kind = "scroll", Direction = "down", Times = 3 },
            new RecordedAction { Kind = "scroll", Direction = "up", Role = "group", Name = "Rows" },
            new RecordedAction { Kind = "scrollUntil", Direction = "down", Text = "Row 40" },
            new RecordedAction { Kind = "back" },
        };

        var read = RoundTrip(Entry(actions));

        Assert.Equal(JsonSerializer.Serialize(actions, EntryJson), JsonSerializer.Serialize(read.Actions, EntryJson));
    }

    /// <summary>Partial port of "round-trips an empty typed value: clearing a field is a recordable action": the port has no <c>typeText</c> action.</summary>
    [Fact]
    public void Round_trips_an_empty_filled_value()
    {
        var read = RoundTrip(Entry(new RecordedAction { Kind = "fill", Role = "textbox", Name = "Name", Value = "" }));

        Assert.Equal("", Assert.Single(read.Actions).Value);
    }

    /// <summary>Partial port of "round-trips the postcondition: end path and end anchors": the port stores the end route, and the anchors are <c>appeared</c>.</summary>
    [Fact]
    public void Round_trips_the_end_route_and_the_appeared_anchors()
    {
        var entry = Entry(new RecordedAction { Kind = "tap", Role = "button", Name = "Save" });
        entry.EndRoute = "/settings";
        entry.Appeared = [new RecordedTarget { Role = "status", Name = "Marker", Text = "saved" }, new RecordedTarget { Text = "Playbook saved" }];

        var read = RoundTrip(entry);

        Assert.Equal("/settings", read.EndRoute);
        Assert.Equal(JsonSerializer.Serialize(entry.Appeared, EntryJson), JsonSerializer.Serialize(read.Appeared, EntryJson));
    }

    [Fact]
    public void Round_trips_both_sides_of_the_delta_anchor_states_included_and_a_long_location_whole()
    {
        var longPath = "/search?q=" + new string('x', 1_000);
        var entry = Entry(new RecordedAction { Kind = "tap", Role = "button", Name = "Save" });
        entry.Route = longPath;
        entry.Appeared = [new RecordedTarget { Role = "switch", Name = "Email notifications", States = ["checked", "pressed"] }];
        entry.Gone = [new RecordedTarget { Role = "listitem", Name = "Item A" }];

        var read = RoundTrip(entry);

        Assert.Equal(longPath, read.Route);
        Assert.Equal(["checked", "pressed"], Assert.Single(read.Appeared).States);
        Assert.Equal(("listitem", "Item A"), (Assert.Single(read.Gone).Role, Assert.Single(read.Gone).Name));
    }

    [Fact]
    public void Drops_unknown_fields_instead_of_carrying_them()
    {
        var directory = CoreTests.TempCache();
        var store = new FileStepCache(directory);
        store.Write(Key, Entry(new RecordedAction { Kind = "tap", Role = "button", Name = "Save" }));
        var path = Path.Combine(directory, Key + ".json");
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["extra"] = "x";
        document["actions"]![0]!["extra"] = "x";
        File.WriteAllText(path, document.ToJsonString());

        var entry = store.Read(Key).Entry;
        Assert.NotNull(entry);
        store.Write(Key, entry);

        Assert.DoesNotContain("\"extra\"", File.ReadAllText(path), StringComparison.Ordinal);
    }

    public static TheoryData<string, string> Rejected => new()
    {
        { "not an object", "\"nope\"" },
        { "wrong schema version", """{ "schema": 2, "actions": [{ "kind": "tap", "role": "button" }], "appeared": [], "gone": [] }""" },
        { "non-string descriptor field", """{ "schema": 1, "actions": [{ "kind": "tap", "role": 7 }], "appeared": [], "gone": [] }""" },
        { "non-array anchors", """{ "schema": 1, "actions": [{ "kind": "tap", "role": "button" }], "appeared": { "role": "status" }, "gone": [] }""" },
        { "non-string anchor field", """{ "schema": 1, "actions": [{ "kind": "tap", "role": "button" }], "appeared": [{ "text": 42 }], "gone": [] }""" },
        { "non-array gone anchors", """{ "schema": 1, "actions": [{ "kind": "tap", "role": "button" }], "appeared": [], "gone": { "role": "status" } }""" },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void Rejects(string label, string json)
    {
        var lookup = ReadJson(json);

        Assert.True(lookup.Entry is null, label);
        Assert.Equal("invalid-entry", lookup.Reason);
    }

    /// <summary>Partial port of "rejects empty actions" and "rejects bad scroll direction": a store may return both, and the replay treats each as a miss.</summary>
    [Theory]
    [InlineData("empty actions", null)]
    [InlineData("bad scroll direction", "diagonal")]
    public async Task Rejects_an_entry_the_replay_cannot_use_as_a_miss(string label, string? direction)
    {
        RecordedAction[] actions = direction is null ? [] : [new RecordedAction { Kind = "scroll", Direction = direction }];
        var cache = new FixedCache(new CacheEntry { Route = "/storage", Actions = [.. actions] });

        var step = await StepAsync(StorageWorld(), cache, "/storage", null, Script(ModelResponses.Tap("button", "Save marker")));

        Assert.True(step.Error is null, label);
        Assert.Equal(("missed", "invalid-entry", 0), (step.Cache?.Mode, step.Cache?.Reason, step.Cache?.ReplayedActions));
    }

    private sealed class FixedCache(CacheEntry entry) : IStepCache
    {
        public CacheLookup Read(string key) => new() { Entry = entry };

        public void Write(string key, CacheEntry written)
        {
        }

        public void Delete(string key)
        {
        }
    }
}
