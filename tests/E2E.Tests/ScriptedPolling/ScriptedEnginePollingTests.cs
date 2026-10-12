// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Tests.ScriptedScreen;

namespace E2E.Tests.ScriptedPolling;

/// <summary>
/// Polling over the scripted reference screen: matchers poll until the tree catches up, and a negated matcher
/// holds for its grace window. The screen's clocks run in real time, as upstream's do. Upstream runs each case
/// as a test in the real runner and reads the step durations from the report. The port has no runner and no
/// report, so each case runs in the test body and times itself.
/// </summary>
public sealed class ScriptedEnginePollingTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public async Task Polls_a_late_node_a_growing_count_and_a_changing_value_until_the_tree_catches_up()
    {
        using var late = ReferenceScene.Create();
        await Expect.That(late.Screen.GetByRole("button", "Late arrival")).ToHaveCountAsync(0);
        var watch = Stopwatch.StartNew();
        await late.Screen.GetByRole("button", "Reveal").TapAsync();
        await Expect.That(late.Screen.GetByRole("button", "Late arrival")).ToBeVisibleAsync(timeout: Soon);
        await late.Screen.GetByRole("button", "Late arrival").WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Visible });
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(200), "toBeVisible took " + watch.Elapsed);

        using var growing = ReferenceScene.Create();
        watch.Restart();
        await growing.Screen.GetByRole("button", "Load more").TapAsync();
        await Expect.That(growing.Screen.GetByRole("list", "Growing").GetByRole("listitem")).ToHaveCountAsync(3, timeout: Soon);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(300), "toHaveCount took " + watch.Elapsed);

        using var changing = ReferenceScene.Create();
        await Expect.That(changing.Screen.GetByRole("menuitem", "Save")).ToBeDisabledAsync();
        watch.Restart();
        await changing.Screen.GetByRole("button", "Advance").TapAsync();
        await Expect.That(changing.Screen.GetByRole("spinbutton", "Progress")).ToHaveValueAsync("100", timeout: Soon);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(150), "toHaveValue took " + watch.Elapsed);
        await Expect.That(changing.Screen.GetByRole("status", "Ticker")).ToHaveTextAsync("tock", timeout: Soon);
        await Expect.That(changing.Screen.GetByRole("menuitem", "Save")).ToBeEnabledAsync(timeout: Soon);
    }

    [Fact]
    public async Task Holds_a_negated_matcher_for_the_grace_window_and_after_a_flip_for_at_least_that_long()
    {
        using var held = ReferenceScene.Create();
        var watch = Stopwatch.StartNew();
        await Expect.That(held.Screen.GetByText("Hidden content")).Not.ToBeVisibleAsync(timeout: Soon);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(900), "not.toBeVisible took " + watch.Elapsed);

        using var flipped = ReferenceScene.Create();
        watch.Restart();
        await flipped.Screen.GetByRole("button", "Reveal").TapAsync();
        await Expect.That(flipped.Screen.GetByRole("status", "Loading")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(2500));
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(1150), "not.toBeVisible took " + watch.Elapsed);
        await flipped.Screen.GetByRole("status", "Loading").WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Hidden });
    }
}
