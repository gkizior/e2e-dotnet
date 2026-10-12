// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile.Tests.Engine;

public sealed class ObservationTests
{
    private static JsonObject Sparse() => new()
    {
        ["nodes"] = new JsonArray(new JsonObject { ["ref"] = "@e1", ["type"] = "application" }),
        ["snapshotQuality"] = new JsonObject { ["state"] = "sparse" },
    };

    private static SemanticNode Named(SemanticNode root, string name) => Walk(root).First(node => node.Name == name);

    /// <summary>Every node under the root, the root excluded: the device's own tree.</summary>
    private static IEnumerable<SemanticNode> Walk(SemanticNode root)
    {
        foreach (var node in root.Children)
        {
            yield return node;
            foreach (var inner in Walk(node))
            {
                yield return inner;
            }
        }
    }

    private static async Task<DeviceSession> OpenAttempt(Harness h)
    {
        var session = await h.Boot();
        if (h.App.BundleId is not null || h.App.AppPath is not null)
        {
            await session.RestartAsync(CancellationToken.None);
        }

        return session;
    }

    [Fact]
    public async Task Projects_the_snapshot_under_one_stable_screen_root_with_a_viewport_and_a_fresh_id_generation_each_time_without_the_tap()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        var first = await session.ObserveAsync(CancellationToken.None);
        Assert.Equal("com.apple.Preferences / General", first.Route);

        // The device's application node sits under a root the engine mints: same id every time, the viewport as its box.
        var root = Assert.Single(first.Roots);
        Assert.Equal("root", root.Ref);
        Assert.Equal("screen", root.Role);
        Assert.Equal(new BoundingBox(0, 0, 390, 844), root.Rect);
        Assert.Equal("application", Assert.Single(root.Children).Role);
        JsonAssert.Args("""{"interactiveOnly":false}""", h.Fake.LastArgs("capture.snapshot"));
        var about = Named(root, "About");

        var second = await session.ObserveAsync(CancellationToken.None);
        Assert.Equal(root.Ref, second.Roots[0].Ref);
        Assert.NotEqual(about.Ref, Named(second.Roots[0], "About").Ref);
    }

    [Fact]
    public async Task Measures_the_screen_in_the_viewport_the_snapshot_publishes_and_in_its_tree_when_it_publishes_none()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        // The application node reads 390x844; a published viewport outranks it.
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ =>
        {
            var snapshot = FakeDeviceClient.SettingsSnapshot();
            snapshot["viewport"] = new JsonObject { ["width"] = 402, ["height"] = 874 };
            return snapshot;
        });
        Assert.Equal(new BoundingBox(0, 0, 402, 874), (await session.ObserveAsync(CancellationToken.None)).Roots[0].Rect);
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshot());
        Assert.Equal(new BoundingBox(0, 0, 390, 844), (await session.ObserveAsync(CancellationToken.None)).Roots[0].Rect);
        Assert.DoesNotContain("capture.screenshot", h.Fake.Methods());

        // A published size that is not positive is no measurement: the tree decides.
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ =>
        {
            var snapshot = FakeDeviceClient.SettingsSnapshot();
            snapshot["viewport"] = new JsonObject { ["width"] = 0, ["height"] = 0 };
            return snapshot;
        });
        Assert.Equal(new BoundingBox(0, 0, 390, 844), (await session.ObserveAsync(CancellationToken.None)).Roots[0].Rect);
    }

    [Fact]
    public async Task Asks_for_interactive_only_snapshots_when_configured()
    {
        var h = Harness.Create(options => options with { Snapshot = SnapshotMode.Interactive });
        var session = await OpenAttempt(h);
        await session.ObserveAsync(CancellationToken.None);
        JsonAssert.Args("""{"interactiveOnly":true}""", h.Fake.LastArgs("capture.snapshot"));
    }

    [Fact]
    public async Task Retries_a_sparse_snapshot_device_side_before_returning_it()
    {
        var h = Harness.Create();
        var calls = 0;
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => ++calls < 2 ? Sparse() : FakeDeviceClient.SettingsSnapshot());
        var session = await OpenAttempt(h);
        var snapshot = await session.ObserveAsync(CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal(10, Walk(snapshot.Roots[0]).Count());
        Assert.Equal([TimeSpan.FromMilliseconds(1200)], h.Delays);
    }

    [Fact]
    public async Task Flags_a_tree_agent_device_cut_or_one_still_sparse_after_the_retries_as_truncated_a_whole_tree_carries_no_flag()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        Assert.False((await session.ObserveAsync(CancellationToken.None)).Truncated);

        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ =>
        {
            var cut = FakeDeviceClient.SettingsSnapshot();
            cut["truncated"] = true;
            return cut;
        });
        Assert.True((await session.ObserveAsync(CancellationToken.None)).Truncated);

        // One application node after every retry: the screen holds more than the listing shows.
        var calls = 0;
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ =>
        {
            calls++;
            return Sparse();
        });
        var sparse = await session.ObserveAsync(CancellationToken.None);
        Assert.Equal(3, calls);
        Assert.Single(Walk(sparse.Roots[0]));
        Assert.True(sparse.Truncated);
    }

    [Fact]
    public async Task Is_an_empty_screen_before_any_app_is_open_when_no_app_is_pinned_and_a_fault_when_one_is()
    {
        var free = Harness.Create(pinned: false);
        free.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => throw new AgentDeviceError("SESSION_NOT_FOUND", "No active app session"));

        // No tree means no geometry: the viewport comes from the device's screenshot metadata, once.
        free.Fake.Respond(DeviceMethods.CaptureScreenshot, _ => new JsonObject { ["logicalWidth"] = 390, ["logicalHeight"] = 844, ["width"] = 1170, ["height"] = 2532, ["pixelDensity"] = 3 });
        var session = await OpenAttempt(free);
        var empty = await session.ObserveAsync(CancellationToken.None);
        var root = Assert.Single(empty.Roots);
        Assert.Equal("root", root.Ref);
        Assert.Equal("screen", root.Role);
        Assert.Equal(new BoundingBox(0, 0, 390, 844), root.Rect);
        Assert.Empty(root.Children);
        Assert.Equal("", empty.Route);
        Assert.False(empty.Truncated);
        await session.ObserveAsync(CancellationToken.None);
        Assert.Single(free.Fake.Methods(), method => method == "capture.screenshot");

        // A device that reports no geometry at all cannot be observed: an invented viewport would misplace every tap.
        var blind = Harness.Create(pinned: false);
        blind.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => new JsonObject { ["nodes"] = new JsonArray() });
        blind.Fake.Respond(DeviceMethods.CaptureScreenshot, _ => new JsonObject());
        var blindSession = await OpenAttempt(blind);
        var unknown = await Assert.ThrowsAsync<EngineException>(() => blindSession.ObserveAsync(CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", unknown.Code);
        Assert.Contains("viewport is unknown", unknown.Message, StringComparison.Ordinal);

        var pinned = Harness.Create();
        pinned.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => throw new AgentDeviceError("SESSION_NOT_FOUND", "No active app session"));
        var pinnedSession = await OpenAttempt(pinned);
        var fault = await Assert.ThrowsAsync<EngineException>(() => pinnedSession.ObserveAsync(CancellationToken.None));
        Assert.Equal("INVALID_STATE", fault.Code);
    }

    [Fact]
    public async Task Cancels_a_snapshot_when_the_operation_aborts()
    {
        var h = Harness.Create();
        h.Fake.RespondAsync(DeviceMethods.CaptureSnapshot, (_, _) => new TaskCompletionSource<JsonNode?>().Task);
        var session = await OpenAttempt(h);
        using var controller = new CancellationTokenSource();
        var pending = session.ObserveAsync(controller.Token);
        await controller.CancelAsync();
        var cancelled = await Assert.ThrowsAsync<EngineException>(() => pending);
        Assert.Equal("CANCELLED", cancelled.Code);
    }
}
