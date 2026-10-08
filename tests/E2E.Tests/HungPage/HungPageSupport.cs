// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E;
using E2E.Engine;

namespace E2E.Tests.HungPage;

/// <summary>Pages, an engine on a short action timeout, and a bound check shared by the hung-page tests.</summary>
internal static class HungPageSupport
{
    public const string BusyPage = """
        <!DOCTYPE html><title>Busy</title>
        <button onclick="while (true) {}">Freeze</button>
        <button>Next</button>
        """;

    public const string CoveredPage = """
        <!DOCTYPE html><title>Covered</title>
        <button>Pay</button>
        <div id="overlay" style="position: fixed; inset: 0; background: rgba(0, 0, 0, 0.3)"></div>
        """;

    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(1_000);

    // How long past its budget a call may still be pending before the test calls it unbounded.
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(1_000);

    // How much sooner than the budget a call may fail.
    private static readonly TimeSpan Early = TimeSpan.FromMilliseconds(300);

    public static Task<IEngineSession> StartAsync()
    {
        return new WebEngine(headless: true).StartAsync(new EngineStartOptions { ActionTimeout = Budget }, CancellationToken.None);
    }

    /// <summary>Opens the busy page, observes it, and taps the button that starts the endless script.</summary>
    public static async Task FreezeAsync(IEngineSession session, TinySite site)
    {
        await session.OpenAsync(site.Url, CancellationToken.None);
        var freeze = Button(await session.ObserveAsync(CancellationToken.None), "Freeze");
        var tap = await BoundedAsync(() => session.PerformAsync(freeze, new LocatorAction.Tap(), CancellationToken.None));
        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", tap.Code);
    }

    /// <summary>
    /// Settles one call, or gives up on it a while after its budget: a call
    /// still pending then fails the test at once instead of holding it. A call
    /// that failed well before its budget fails the test too: the budget must
    /// bound it, not cut it short.
    /// </summary>
    public static async Task<EngineException> BoundedAsync(Func<Task> call)
    {
        var started = Stopwatch.StartNew();
        var running = call();
        var settled = await Task.WhenAny(running, Task.Delay(Budget + Slack));
        Assert.True(settled == running, $"still pending {(Budget + Slack).TotalMilliseconds}ms into a {Budget.TotalMilliseconds}ms budget");
        Assert.True(started.Elapsed >= Budget - Early, $"failed after {started.ElapsedMilliseconds}ms of a {Budget.TotalMilliseconds}ms budget");
        return await Assert.ThrowsAsync<EngineException>(() => running);
    }

    public static SemanticNode Button(Observation observation, string name)
    {
        return Flatten(observation.Roots).First(node => node.Role == "button" && node.Name == name);
    }

    private static IEnumerable<SemanticNode> Flatten(IEnumerable<SemanticNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
