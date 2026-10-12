// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;
using static E2E.Mobile.Tests.RawNodes;
using static E2E.Mobile.Tests.Screens;

namespace E2E.Mobile.Tests.Engine;

public sealed class DeterministicActionsTests
{
    private static readonly TimeSpan ShortTransition = TimeSpan.FromMilliseconds(100);

    private static Harness WithTransition(int milliseconds) =>
        Harness.Create(options => options with { TransitionMilliseconds = milliseconds });

    /// <summary>An attempt whose steps are a test's own: they never settle, and they wait out a transition.</summary>
    private static async Task<DeviceSession> OpenTestAttempt(Harness h)
    {
        var session = await OpenAttempt(h);
        session.Origin = ActionOrigin.Test;
        return session;
    }

    private static Task Tap(DeviceSession session, SemanticNode node) => session.PerformAsync(node, new LocatorAction.Tap(), CancellationToken.None);

    /// <summary>
    /// Taps with the clock stopped. A press that goes out without asking for a delay is one that neither settled nor
    /// waited out a transition budget. The delay is what stands for the wait, whatever the machine's speed.
    /// </summary>
    private static async Task TapsAtOnce(Harness h, DeviceSession session, SemanticNode node, string expected)
    {
        var delays = h.Delays.Count;
        await Tap(session, node);
        Assert.Equal(delays, h.Delays.Count);
        JsonAssert.Args(expected, h.Fake.LastArgs("interactions.press"));
    }

    private static int Snapshots(Harness h) => h.Fake.Methods().Count(method => method == "capture.snapshot");

    [Fact]
    public async Task Acts_at_once_and_without_settling_on_a_control_that_was_already_on_screen_before_the_last_action()
    {
        var h = Harness.Create();
        var session = await OpenTestAttempt(h);

        // Two looks at the screen: the second is the one the action resolves from, the first stands for the screen before the launch.
        await Observed(session, "About");
        await Tap(session, await Observed(session, "Back"));
        var looks = Snapshots(h);
        await TapsAtOnce(h, session, await Observed(session, "About"), """{"ref":"@e4"}""");

        // The observation the tap resolves from is the only new look: nothing is waited out or found again.
        Assert.Equal(looks + 1, Snapshots(h));
    }

    [Fact]
    public async Task Gives_a_control_that_came_with_the_last_action_the_transition_budget_before_acting_on_it()
    {
        var h = WithTransition(120);
        var session = await OpenTestAttempt(h);
        await Tap(session, await Observed(session, "Back"));

        // The next look shows a control that was not there before the tap: a sheet's button.
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshotWith(Submit()));
        var submit = await Observed(session, "Submit");
        var delays = h.Delays.Count;
        await Tap(session, submit);
        Assert.True(h.Delays.Skip(delays).Sum(delay => delay.TotalMilliseconds) >= ShortTransition.TotalMilliseconds);
        JsonAssert.Args("""{"ref":"@e11"}""", h.Fake.LastArgs("interactions.press"));
    }

    [Fact]
    public async Task Acts_on_a_control_that_came_with_the_last_action_where_a_fresh_snapshot_lists_it_once_the_budget_has_passed()
    {
        var h = WithTransition(120);
        var session = await OpenTestAttempt(h);
        await Tap(session, await Observed(session, "Back"));

        // Android reports a sliding modal's frames in flight: the button is first seen below the screen, and lands on
        // it with a new ref by the time the budget has passed.
        JsonObject submit(double y, string reference) =>
            FakeDeviceClient.SettingsSnapshotWith(Submit(reference, y, type: "android.widget.Button", identifier: "submit"));
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => submit(2000, "@e11"));
        var inFlight = await Observed(session, "Submit");
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => submit(600, "@e42"));
        await Tap(session, inFlight);
        JsonAssert.Args("""{"ref":"@e42"}""", h.Fake.LastArgs("interactions.press"));

        // A control the fresh snapshot no longer lists is acted on as it was.
        await Tap(session, await Observed(session, "Back"));
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => submit(600, "@e42"));
        var landed = await Observed(session, "Submit");
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshot());
        await Tap(session, landed);
        JsonAssert.Args("""{"ref":"@e42"}""", h.Fake.LastArgs("interactions.press"));
    }

    [Fact]
    public async Task Acts_on_the_duplicate_nearest_where_the_control_was_when_a_fresh_snapshot_lists_several_alike()
    {
        var h = WithTransition(120);
        var session = await OpenTestAttempt(h);
        await Tap(session, await Observed(session, "Back"));
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshotWith(Submit("@e11", 600, "Add")));
        var arriving = await Observed(session, "Add");

        // A list of rows that each carry an identical Add button: the row the test meant has settled 10 points lower.
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshotWith(Submit("@e20", 200, "Add"), Submit("@e21", 610, "Add", index: 11)));
        await Tap(session, arriving);
        JsonAssert.Args("""{"ref":"@e21"}""", h.Fake.LastArgs("interactions.press"));
    }

    [Fact]
    public async Task Gives_a_control_that_moved_with_the_last_action_the_budget_too_and_skips_it_once_the_budget_has_elapsed()
    {
        var h = WithTransition(120);
        var session = await OpenTestAttempt(h);
        await Tap(session, await Observed(session, "Back"));
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SnapshotOf(
            SettingsNodes.Select(node => node.Identifier == "ABOUT" || node.Label == "About" ? node with { Rect = new Rect(0, 300, 390, 44) } : node)));
        var delays = h.Delays.Count;
        await Tap(session, await Observed(session, "About"));
        Assert.True(h.Delays.Skip(delays).Sum(delay => delay.TotalMilliseconds) >= ShortTransition.TotalMilliseconds);

        // Well after the last action, nothing waits.
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));
        await TapsAtOnce(h, session, await Observed(session, "About"), """{"ref":"@e4"}""");
    }

    [Fact]
    public async Task Fills_goes_back_and_taps_at_a_point_without_settling_the_agent_keeps_the_settle_path()
    {
        var h = Harness.Create();
        var session = await OpenTestAttempt(h);
        var search = await Observed(session, "Search");
        await session.PerformAsync(search, new LocatorAction.Fill("blue"), CancellationToken.None);
        JsonAssert.Args("""{"ref":"@e7","text":"blue"}""", h.Fake.LastArgs("interactions.fill"));
        await session.BackAsync(CancellationToken.None);
        JsonAssert.Args("{}", h.Fake.LastArgs("command.back"));
        var point = new Point(10, 20);
        await session.PerformAtAsync(point, new PointerAction.Tap());
        JsonAssert.Args("""{"x":10,"y":20}""", h.Fake.LastArgs("interactions.press"));
        await session.PerformAtAsync(point, new PointerAction.DoubleTap());
        JsonAssert.Args("""{"x":10,"y":20,"count":2}""", h.Fake.LastArgs("interactions.press"));
        await session.PerformAtAsync(point, new PointerAction.LongPress(TimeSpan.FromMilliseconds(700)));
        JsonAssert.Args("""{"x":10,"y":20,"durationMs":700}""", h.Fake.LastArgs("interactions.longPress"));
        await session.PerformAtAsync(point, new PointerAction.LongPress());
        JsonAssert.Args("""{"x":10,"y":20,"durationMs":1000}""", h.Fake.LastArgs("interactions.longPress"));
        await session.PerformAtAsync(point, new PointerAction.SwipeTo(new Point(10, 300)));
        JsonAssert.Args("""{"from":{"x":10,"y":20},"to":{"x":10,"y":300}}""", h.Fake.LastArgs("interactions.swipe"));
        await session.PerformAtAsync(point, new PointerAction.SwipeTo(new Point(10, 300), TimeSpan.FromMilliseconds(400)));
        JsonAssert.Args("""{"x":10,"y":20,"dx":0,"dy":280,"durationMs":400}""", h.Fake.LastArgs("interactions.pan"));
        Assert.Equal(["tap", "doubleTap", "longPress", "swipeTo"], DeviceActions.PointerActionKinds);

        var about = await Observed(session, "About");
        session.Origin = ActionOrigin.Agent;
        await session.PerformAsync(about, new LocatorAction.Tap(), CancellationToken.None);
        JsonAssert.Args("""{"ref":"@e4","settle":true,"settleQuietMs":150}""", h.Fake.LastArgs("interactions.press"));
    }

    [Fact]
    public async Task Does_not_dispatch_an_action_whose_transition_wait_was_cancelled()
    {
        var h = WithTransition(300);
        h.Waits.Wait = (_, token) => Task.Delay(Timeout.Infinite, token);
        var session = await OpenTestAttempt(h);
        var about = await Observed(session, "About");
        using var controller = new CancellationTokenSource();
        controller.CancelAfter(TimeSpan.FromMilliseconds(20));
        var cancelled = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(about, new LocatorAction.Tap(), controller.Token));
        Assert.Equal("CANCELLED", cancelled.Code);
        Assert.DoesNotContain("interactions.press", h.Fake.Methods());
    }

    [Fact]
    public async Task Treats_every_control_as_arriving_after_a_launch_even_one_the_previous_screen_also_had()
    {
        var h = WithTransition(120);
        var session = await OpenTestAttempt(h);
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));
        await Observed(session, "About");

        // A relaunch: the same About cell at the same place is on the new screen too.
        await session.RestartAsync(CancellationToken.None);
        var delays = h.Delays.Count;
        await Tap(session, await Observed(session, "About"));
        Assert.True(h.Delays.Skip(delays).Sum(delay => delay.TotalMilliseconds) >= ShortTransition.TotalMilliseconds);
    }

    [Fact]
    public async Task Does_not_count_a_toggle_already_in_the_wanted_state_as_an_action()
    {
        var h = WithTransition(120);
        var session = await OpenTestAttempt(h);
        h.Clock.Advance(TimeSpan.FromMilliseconds(150));

        // Airplane Mode reads unchecked, so uncheck sends nothing.
        await session.PerformAsync(await Observed(session, "Airplane Mode"), new LocatorAction.Uncheck(), CancellationToken.None);
        Assert.DoesNotContain("interactions.press", h.Fake.Methods());
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshotWith(Submit("@e11", 600, "Save")));
        await TapsAtOnce(h, session, await Observed(session, "Save"), """{"ref":"@e11"}""");
    }
}
