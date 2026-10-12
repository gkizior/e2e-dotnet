// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Mobile.Tests.RawNodes;
using static E2E.Mobile.Tests.Screens;

namespace E2E.Mobile.Tests.Engine;

public sealed class PerformTests
{
    [Fact]
    public async Task Maps_every_supported_action_onto_one_agent_device_command()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        var root = await RootOf(session);
        var (about, toggle, search, scroller, back) = (Named(root, "About"), Named(root, "Airplane Mode"), Named(root, "Search"), Named(root, "Scroller"), Named(root, "Back"));
        var before = h.Fake.Calls.Count;
        await session.PerformAsync(about, new LocatorAction.Tap(), CancellationToken.None);
        await session.PerformAsync(about, new LocatorAction.DoubleTap(), CancellationToken.None);
        await session.LongPressAsync(about, TimeSpan.FromMilliseconds(900));
        await session.LongPressAsync(about);
        await session.PerformAsync(search, new LocatorAction.Focus(), CancellationToken.None);
        await session.HoverAsync(about);
        await session.PerformAsync(search, new LocatorAction.Fill("blue"), CancellationToken.None);
        await session.PerformAsync(search, new LocatorAction.Clear(), CancellationToken.None);
        await session.PerformAsync(toggle, new LocatorAction.Uncheck(), CancellationToken.None);
        await session.PerformAsync(toggle, new LocatorAction.Check(), CancellationToken.None);
        await session.PerformAsync(search, new LocatorAction.Press("Enter"), CancellationToken.None);
        await session.PerformAsync(search, new LocatorAction.Press("a"), CancellationToken.None);
        await session.PerformAsync(search, new LocatorAction.Press("Space"), CancellationToken.None);
        await session.PerformAsync(scroller, new LocatorAction.Swipe(ScrollDirection.Down), CancellationToken.None);
        await session.SwipeAsync(root, ScrollDirection.Up, Momentum.Fast);
        await session.DragToAsync(about, back);
        JsonAssert.Calls(
            h.Fake.Calls,
            before,
            ("interactions.press", """{"ref":"@e4","settle":true,"settleQuietMs":150}"""),
            ("interactions.press", """{"ref":"@e4","count":2,"settle":true,"settleQuietMs":150}"""),
            ("interactions.longPress", """{"ref":"@e4","settle":true,"settleQuietMs":150,"durationMs":900}"""),

            // No duration named: the engine's one-second hold, past a Pressable's delayLongPress.
            ("interactions.longPress", """{"ref":"@e4","settle":true,"settleQuietMs":150,"durationMs":1000}"""),
            ("interactions.press", """{"ref":"@e7","settle":true,"settleQuietMs":150}"""),
            ("interactions.hover", """{"ref":"@e4"}"""),
            ("interactions.fill", """{"ref":"@e7","text":"blue","settle":true,"settleQuietMs":150}"""),
            ("interactions.fill", """{"ref":"@e7","text":"","settle":true,"settleQuietMs":150}"""),
            ("interactions.press", """{"ref":"@e6","settle":true,"settleQuietMs":150}"""),
            ("command.keyboard", """{"action":"enter"}"""),
            ("interactions.press", """{"ref":"@e7","settle":true,"settleQuietMs":150}"""),
            ("interactions.type", """{"text":"a"}"""),

            // Space is typed like a character, the unfocused field tapped first.
            ("interactions.press", """{"ref":"@e7","settle":true,"settleQuietMs":150}"""),
            ("interactions.type", """{"text":" "}"""),
            ("interactions.swipe", """{"from":{"x":195,"y":620},"to":{"x":195,"y":420}}"""),

            // The root swipe is the viewport swipe: agent-device's whole-screen scroll at its default amount, settled like a tap.
            ("interactions.scroll", """{"direction":"up","settle":true,"settleQuietMs":150}"""),
            ("interactions.drag", """{"source":"@e4","destination":"@e3"}"""));
    }

    [Theory]
    [InlineData("e2")]
    [InlineData("")]
    public async Task Presses_the_innermost_control_even_when_the_row_ref_is(string rowRef)
    {
        var h = Harness.Create();
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SnapshotOf(
        [
            N("e1", index: 0, depth: 0, type: "Application"),
            N(rowRef, index: 1, parentIndex: 0, depth: 1, type: "Switch", label: "Haptic Feedback", value: "1"),
            N("e3", index: 2, parentIndex: 1, depth: 2, type: "Button", label: "Haptic Feedback"),
            N("e4", index: 3, parentIndex: 1, depth: 2, type: "Switch", value: "1"),
            N("e5", index: 4, parentIndex: 0, depth: 1, type: "Switch", label: "Sound", value: "0"),
        ]));
        var session = await OpenAttempt(h);
        var root = await RootOf(session);
        var haptic = Named(root, "Haptic Feedback");
        var sound = Named(root, "Sound");
        var before = h.Fake.Calls.Count;
        await session.PerformAsync(haptic, new LocatorAction.Uncheck(), CancellationToken.None);
        await session.PerformAsync(haptic, new LocatorAction.Tap(), CancellationToken.None);
        await session.PerformAsync(sound, new LocatorAction.Check(), CancellationToken.None);
        await session.PerformAsync(haptic, new LocatorAction.Check(), CancellationToken.None);
        JsonAssert.Calls(
            h.Fake.Calls.Skip(before).Where(call => call.Method == "interactions.press"),
            0,
            ("interactions.press", """{"ref":"@e4","settle":true,"settleQuietMs":150}"""),
            ("interactions.press", """{"ref":"@e4","settle":true,"settleQuietMs":150}"""),
            ("interactions.press", """{"ref":"@e5","settle":true,"settleQuietMs":150}"""));
    }

    [Fact]
    public async Task Refuses_what_a_device_surface_cannot_express_and_reports_daemon_ref_misses_as_retryable()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        var root = await RootOf(session);
        var about = Named(root, "About");
        LocatorAction[] refused =
        [
            new LocatorAction.Select("x"),
            new LocatorAction.ScrollIntoView(),

            // Named keys other than Enter and Space, and every modifier, have no soft-keyboard equivalent.
            new LocatorAction.Press("Escape"),
            new LocatorAction.Press("Tab"),
            new LocatorAction.Press("Control+a"),
            new LocatorAction.Press("Shift+Enter"),

            // A tap would activate the row. Focus is for editable fields only.
            new LocatorAction.Focus(),

            // The row-level cell exposes no checked state. A blind flip could undo a correct one.
            new LocatorAction.Check(),
        ];
        foreach (var action in refused)
        {
            var failure = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(about, action, CancellationToken.None));
            Assert.Equal("UNSUPPORTED_CAPABILITY", failure.Code);
        }

        // The root takes swipe only.
        var rootTap = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(root, new LocatorAction.Tap(), CancellationToken.None));
        Assert.Equal("NOT_ACTIONABLE", rootTap.Code);
        Assert.DoesNotContain("interactions.press", h.Fake.Methods());

        h.Fake.Respond(DeviceMethods.InteractionsPress, _ => throw new AgentDeviceError(
            "COMMAND_FAILED",
            "Ref @e4 not found",
            new Dictionary<string, object?> { ["reason"] = "ref_not_found", ["dispatched"] = "no" }));
        var stale = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(about, new LocatorAction.Tap(), CancellationToken.None));
        Assert.Equal("NODE_STALE", stale.Code);
        Assert.True(stale.Retryable);

        h.Fake.Respond(DeviceMethods.InteractionsPress, _ => throw new AgentDeviceError("COMMAND_FAILED", "XCTest lost the runner"));
        var lost = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(about, new LocatorAction.Tap(), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", lost.Code);
        Assert.False(lost.Retryable);
    }
}
