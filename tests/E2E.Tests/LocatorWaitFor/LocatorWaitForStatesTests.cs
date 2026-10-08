// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Engine;

namespace E2E.Tests.LocatorWaitFor;

public sealed class LocatorWaitForStatesTests
{
    private static readonly TimeSpan ChangeAfter = TimeSpan.FromMilliseconds(1200);

    private static readonly SemanticNode Banner = new() { Ref = "banner", Role = "alert", Name = "Saved", TestId = "banner" };

    private static readonly SemanticNode HiddenBanner = new() { Ref = "banner", Role = "alert", Name = "Saved", TestId = "banner", States = new NodeStates { Hidden = true } };

    private static readonly SemanticNode Twin = new() { Ref = "twin", Role = "alert", Name = "Saved", TestId = "banner" };

    public static TheoryData<WaitForState, string, string> Changes => new()
    {
        { WaitForState.Attached, "", "hidden" },
        { WaitForState.Detached, "hidden", "" },
        { WaitForState.Visible, "hidden", "shown" },
        { WaitForState.Hidden, "shown", "hidden" },
        { WaitForState.Hidden, "shown", "" },
    };

    public static TheoryData<WaitForState, string> Holding => new()
    {
        { WaitForState.Attached, "hidden" },
        { WaitForState.Detached, "" },
        { WaitForState.Visible, "shown" },
        { WaitForState.Hidden, "" },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public async Task Waits_for_a_state_until_the_node_changes_after_1200_ms(WaitForState state, string initial, string later)
    {
        var locator = Changing(Nodes(initial), Nodes(later));
        Assert.True(await Timed(locator, state) >= ChangeAfter - TimeSpan.FromMilliseconds(50));
    }

    [Theory]
    [MemberData(nameof(Holding))]
    public async Task Passes_a_state_at_once_when_the_state_already_holds(WaitForState state, string nodes)
    {
        var locator = Changing(Nodes(nodes), Nodes(nodes));
        Assert.True(await Timed(locator, state) < ChangeAfter);
    }

    [Fact]
    public async Task Attached_accepts_a_hidden_node_that_visible_keeps_waiting_for()
    {
        var locator = Changing([HiddenBanner], [HiddenBanner]);
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Attached });
        var error = await Assert.ThrowsAsync<TestException>(() => locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Visible, Timeout = TimeSpan.FromMilliseconds(100) }));
        Assert.Equal("TIMEOUT", error.Code);
    }

    [Theory]
    [InlineData(WaitForState.Attached, "")]
    [InlineData(WaitForState.Detached, "hidden")]
    public async Task Times_out_a_state_when_the_state_never_holds(WaitForState state, string nodes)
    {
        var locator = Changing(Nodes(nodes), Nodes(nodes));
        var error = await Assert.ThrowsAsync<TestException>(() => locator.WaitForAsync(new LocatorWaitForOptions { State = state, Timeout = TimeSpan.FromMilliseconds(100) }));
        Assert.Equal("TIMEOUT", error.Code);
    }

    [Fact]
    public async Task Fails_every_state_at_once_on_several_matches()
    {
        var locator = Changing([Banner, Twin], [Banner, Twin]);
        foreach (var state in new[] { WaitForState.Attached, WaitForState.Detached, WaitForState.Visible, WaitForState.Hidden })
        {
            var error = await Assert.ThrowsAsync<TestException>(() => locator.WaitForAsync(new LocatorWaitForOptions { State = state, Timeout = TimeSpan.FromSeconds(1) }));
            Assert.Equal("STRICT_MODE", error.Code);
        }
    }

    private static SemanticNode[] Nodes(string kind) => kind switch
    {
        "shown" => [Banner],
        "hidden" => [HiddenBanner],
        _ => [],
    };

    /// <summary>A banner locator over a screen whose nodes are <paramref name="initial"/>, then <paramref name="later"/> after 1200 ms.</summary>
    private static Locator Changing(SemanticNode[] initial, SemanticNode[] later)
    {
        var clock = Stopwatch.StartNew();
        var screen = new Screen(
            _ => Task.FromResult(new Observation { Route = "/", Roots = clock.Elapsed < ChangeAfter ? initial : later }),
            (_, _, _) => Task.CompletedTask,
            () => CancellationToken.None,
            () => { },
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(4));
        return screen.GetByTestId("banner");
    }

    private static async Task<TimeSpan> Timed(Locator locator, WaitForState state)
    {
        var watch = Stopwatch.StartNew();
        await locator.WaitForAsync(new LocatorWaitForOptions { State = state });
        return watch.Elapsed;
    }
}
