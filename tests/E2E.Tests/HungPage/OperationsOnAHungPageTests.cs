// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.HungPage.HungPageSupport;

namespace E2E.Tests.HungPage;

/// <summary>
/// Every operation ends at its budget, whatever the page does: a server that
/// never answers a navigation, which Playwright's own timeout ends, and a
/// renderer stuck in a script a button started. Playwright cannot answer an
/// evaluate or input there at all, so the engine's own deadline ends the call:
/// <c>OPERATION_TIMEOUT</c>, or <c>ACTION_MAY_HAVE_COMMITTED</c> for input,
/// which may have reached the page.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class OperationsOnAHungPageTests
{
    [Fact]
    public async Task Times_out_a_navigation_to_a_server_that_never_answers()
    {
        using var site = await TinySite.StartAsync(_ => Task.Delay(Timeout.Infinite));
        await using var session = await StartAsync();

        var open = await BoundedAsync(() => session.OpenAsync(site.Url + "never", CancellationToken.None));

        Assert.Equal("OPERATION_TIMEOUT", open.Code);
    }

    [Fact]
    public async Task Times_out_a_locate_and_a_point_tap_on_a_page_whose_renderer_is_stuck_in_a_script()
    {
        using var site = await TinySite.StartAsync(BusyPage);
        await using var session = await StartAsync();
        await FreezeAsync(session, site);
        var browser = (IBrowserSession)session;

        // The port locates through an observation, and taps a point with the mouse.
        var locate = await BoundedAsync(() => session.ObserveAsync(CancellationToken.None));
        Assert.Equal("OPERATION_TIMEOUT", locate.Code);
        var move = await BoundedAsync(() => browser.MouseMoveAsync(10, 10, CancellationToken.None));
        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", move.Code);
        var down = await BoundedAsync(() => browser.MouseDownAsync(CancellationToken.None));
        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", down.Code);
        var up = await BoundedAsync(() => browser.MouseUpAsync(CancellationToken.None));
        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", up.Code);
    }

    [Fact]
    public async Task Keeps_the_reason_Playwright_gives_for_a_tap_that_cannot_land_ahead_of_the_deadline()
    {
        using var site = await TinySite.StartAsync(CoveredPage);
        await using var session = await StartAsync();
        await session.OpenAsync(site.Url, CancellationToken.None);
        var pay = Button(await session.ObserveAsync(CancellationToken.None), "Pay");

        var tap = await BoundedAsync(() => session.PerformAsync(pay, new LocatorAction.Tap(), CancellationToken.None));

        Assert.Equal("NOT_ACTIONABLE", tap.Code);
        Assert.Contains("intercepts pointer events", tap.Message, StringComparison.Ordinal);
    }
}
