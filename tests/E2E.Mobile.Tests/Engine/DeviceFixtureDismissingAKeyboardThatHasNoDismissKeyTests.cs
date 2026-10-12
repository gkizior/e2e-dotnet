// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;
using static E2E.Mobile.Tests.RawNodes;
using static E2E.Mobile.Tests.Screens;

namespace E2E.Mobile.Tests.Engine;

public sealed class DeviceFixtureDismissingAKeyboardThatHasNoDismissKeyTests
{
    private static JsonObject KeyboardUp()
    {
        var snapshot = FakeDeviceClient.SettingsSnapshot();
        snapshot["keyboard"] = new JsonObject { ["kind"] = "visible", ["frame"] = new JsonObject { ["x"] = 0, ["y"] = 583, ["width"] = 390, ["height"] = 261 } };
        return snapshot;
    }

    private static JsonObject KeyboardGone()
    {
        var snapshot = FakeDeviceClient.SettingsSnapshot();
        snapshot["keyboard"] = new JsonObject { ["kind"] = "absent" };
        return snapshot;
    }

    private static JsonNode NoDismissKey(JsonObject? options) => throw new AgentDeviceError(
        "UNSUPPORTED_OPERATION",
        "Unable to dismiss the iOS keyboard: the keyboard exposes no dismiss key, and background taps are never attempted");

    /// <summary>Answers the captures the dismissal takes, in order, then the last one forever.</summary>
    private static void Captures(Harness h, params Func<JsonObject>[] snapshots)
    {
        var taken = 0;
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => snapshots[Math.Min(taken++, snapshots.Length - 1)]());
    }

    private static async Task<(Harness H, DeviceSession Session)> Ready()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        await Observed(session, "Back");
        h.Fake.Respond(DeviceMethods.CommandKeyboard, NoDismissKey);
        return (h, session);
    }

    private static List<JsonObject?> Swipes(Harness h) => [.. h.Fake.Calls.Where(call => call.Method == "interactions.swipe").Select(call => call.Args)];

    [Fact]
    public async Task Drags_at_the_centre_of_the_screen_the_way_a_user_does_and_stops_once_the_keyboard_is_gone()
    {
        var (h, session) = await Ready();
        Captures(h, KeyboardUp, KeyboardGone);
        var before = h.Fake.Calls.Count;
        await session.Device.DismissKeyboardAsync();
        Assert.Equal(["command.keyboard", "capture.snapshot", "interactions.swipe", "capture.snapshot"], h.Fake.Methods().Skip(before));

        // The viewport is 390 by 844: a short horizontal drag from the centre, 3 percent of the width.
        var swipe = h.Fake.LastArgs("interactions.swipe")!;
        Assert.Equal(195, swipe["from"]!["x"]!.GetValue<double>());
        Assert.Equal(422, swipe["from"]!["y"]!.GetValue<double>());
        Assert.Equal(183.3, swipe["to"]!["x"]!.GetValue<double>(), 2);
        Assert.Equal(422, swipe["to"]!["y"]!.GetValue<double>());
    }

    [Fact]
    public async Task Tries_a_vertical_drag_when_the_horizontal_one_left_the_keyboard_up()
    {
        var (h, session) = await Ready();
        Captures(h, KeyboardUp, KeyboardUp, KeyboardGone);
        await session.Device.DismissKeyboardAsync();
        var swipes = Swipes(h);
        Assert.Equal(2, swipes.Count);
        Assert.Equal(183.3, swipes[0]!["to"]!["x"]!.GetValue<double>(), 2);
        Assert.Equal(422, swipes[0]!["to"]!["y"]!.GetValue<double>());
        Assert.Equal(195, swipes[1]!["to"]!["x"]!.GetValue<double>());
        Assert.Equal(396.68, swipes[1]!["to"]!["y"]!.GetValue<double>(), 2);
    }

    [Fact]
    public async Task Fails_naming_the_alternatives_when_both_drags_leave_the_keyboard_up()
    {
        var (h, session) = await Ready();
        Captures(h, KeyboardUp);
        var failure = await Assert.ThrowsAsync<EngineException>(() => session.Device.DismissKeyboardAsync());
        Assert.Equal("UNSUPPORTED_CAPABILITY", failure.Code);
        Assert.Matches(@"(?s)stayed up through a horizontal and a vertical drag.*Done or close control.*Enter", failure.Message);
        Assert.Equal(2, Swipes(h).Count);
    }

    [Fact]
    public async Task Reads_the_keyboard_off_its_own_elements_when_the_capture_carries_no_keyboard_fact()
    {
        var (h, session) = await Ready();
        Captures(
            h,
            () => FakeDeviceClient.SettingsSnapshotWith(
                N("@e20", index: 10, parentIndex: 0, depth: 1, type: "Keyboard", rect: new Rect(0, 583, 390, 261)),
                N("@e21", index: 11, parentIndex: 10, depth: 2, type: "Key", label: "q", rect: new Rect(2, 600, 36, 44))),
            FakeDeviceClient.SettingsSnapshot);
        await session.Device.DismissKeyboardAsync();
        Assert.Single(Swipes(h));
    }

    [Fact]
    public async Task Retries_a_sparse_capture_instead_of_reading_it_as_a_dismissed_keyboard()
    {
        var (h, session) = await Ready();
        JsonObject Sparse() => new()
        {
            ["nodes"] = new JsonArray(new JsonObject { ["ref"] = "@e1", ["type"] = "application" }),
            ["snapshotQuality"] = new JsonObject { ["state"] = "sparse" },
        };
        Captures(h, KeyboardUp, Sparse, KeyboardUp, KeyboardGone);
        var before = h.Fake.Calls.Count;
        await session.Device.DismissKeyboardAsync();

        // The sparse tree after the first drag is retried, not trusted: the keyboard it hid is still up, so the second drag follows.
        Assert.Equal(
            ["command.keyboard", "capture.snapshot", "interactions.swipe", "capture.snapshot", "capture.snapshot", "interactions.swipe", "capture.snapshot"],
            h.Fake.Methods().Skip(before));
    }

    [Fact]
    public async Task Presses_Continue_on_the_simulator_typing_tip_before_it_drags()
    {
        var (h, session) = await Ready();
        JsonObject WithTip()
        {
            var snapshot = FakeDeviceClient.SettingsSnapshotWith(
                N("@e30", index: 10, parentIndex: 0, depth: 1, type: "static-text", label: "Speed up your typing by sliding your finger across the letters to compose a word.", rect: new Rect(20, 600, 350, 60)),
                N("@e31", index: 11, parentIndex: 0, depth: 1, type: "button", label: "Continue", rect: new Rect(95, 700, 200, 44)));
            snapshot["keyboard"] = KeyboardUp()["keyboard"]!.DeepClone();
            return snapshot;
        }

        Captures(h, WithTip, KeyboardGone);
        var before = h.Fake.Calls.Count;
        await session.Device.DismissKeyboardAsync();
        Assert.Equal(["command.keyboard", "capture.snapshot", "interactions.press", "capture.snapshot"], h.Fake.Methods().Skip(before));
        JsonAssert.Args("""{"x":195,"y":722}""", h.Fake.LastArgs("interactions.press"));
    }

    [Fact]
    public async Task Leaves_any_other_refusal_to_the_caller()
    {
        var (h, session) = await Ready();
        h.Fake.Respond(DeviceMethods.CommandKeyboard, _ => throw new AgentDeviceError("COMMAND_FAILED", "the runner is busy"));
        var failure = await Assert.ThrowsAsync<EngineException>(() => session.Device.DismissKeyboardAsync());
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.Empty(Swipes(h));
    }
}
