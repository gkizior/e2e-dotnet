// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CheckActions.CheckPage;

namespace E2E.Tests.CheckActions;

/// <summary>
/// <c>check</c> and <c>uncheck</c> against a real page, ported from upstream
/// <c>check-actions.test.ts</c>, on the element an observation names, which the agent acts
/// through. A control the app replaces or navigates away from once it is picked took the
/// click, so the action is done, and one the click did not change fails.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ThroughTheElementTargetTests
{
    [Fact]
    public async Task Checks_a_radio_the_app_replaces_with_its_selected_view()
    {
        await RunAsync(
            """
            <div id="plan"><label><input type="radio" name="plan" id="pro"
              onchange="document.getElementById('plan').innerHTML = '<p>Pro selected</p>'">Pro</label></div>
            """,
            async session =>
            {
                await session.PerformAsync(await FindAsync(session, "radio", "Pro"), new LocatorAction.Check(), CancellationToken.None);
                Assert.Equal("Pro selected", await EvaluateAsync<string>(session, "() => document.getElementById('plan').textContent"));
            });
    }

    [Fact]
    public async Task Leaves_what_replaced_the_control_to_the_next_read_even_an_unchecked_copy()
    {
        await RunAsync(
            """
            <div id="terms"><label><input type="checkbox" id="box"
              onchange="document.getElementById('terms').innerHTML = '<label><input type=checkbox id=box>Agree</label>'">Agree</label></div>
            """,
            async session =>
            {
                await session.PerformAsync(await FindAsync(session, "checkbox", "Agree"), new LocatorAction.Check(), CancellationToken.None);
                Assert.False(await EvaluateAsync<bool>(session, "() => document.getElementById('box').checked"));
            });
    }

    [Fact]
    public async Task Checks_a_radio_that_navigates_on_change()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(
            context,
            context.Request.Url!.AbsolutePath == "/"
                ? """<label><input type="radio" name="plan" id="pro" onchange="location.href = '/next'">Pro</label>"""
                : "<p>Next page</p>"));
        await using var session = await StartAsync();
        await session.OpenAsync(site.Url, CancellationToken.None);

        await session.PerformAsync(await FindAsync(session, "radio", "Pro"), new LocatorAction.Check(), CancellationToken.None);
        var browser = (IBrowserSession)session;
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await browser.GetUrlAsync(CancellationToken.None) != site.Url + "next" && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(site.Url + "next", await browser.GetUrlAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Checks_and_unchecks_a_checkbox_and_clicks_nothing_already_in_the_wanted_state()
    {
        await RunAsync(
            CountClicks + """<label><input type="checkbox" id="box">Notify</label>""",
            async session =>
            {
                var box = await FindAsync(session, "checkbox", "Notify");
                await session.PerformAsync(box, new LocatorAction.Check(), CancellationToken.None);
                await session.PerformAsync(box, new LocatorAction.Check(), CancellationToken.None);
                Assert.True(await EvaluateAsync<bool>(session, "() => document.getElementById('box').checked"));
                await session.PerformAsync(box, new LocatorAction.Uncheck(), CancellationToken.None);
                Assert.False(await EvaluateAsync<bool>(session, "() => document.getElementById('box').checked"));
                Assert.Equal(2, await EvaluateAsync<int>(session, "() => window.clicks"));
            });
    }

    [Fact]
    public async Task Checks_an_ARIA_checkbox()
    {
        await RunAsync(
            """
            <div role="checkbox" id="box" aria-checked="false" tabindex="0"
              onclick="this.setAttribute('aria-checked', String(this.getAttribute('aria-checked') !== 'true'))">Notify</div>
            """,
            async session =>
            {
                await session.PerformAsync(await FindAsync(session, "checkbox", "Notify"), new LocatorAction.Check(), CancellationToken.None);
                Assert.Equal("true", await EvaluateAsync<string>(session, "() => document.getElementById('box').getAttribute('aria-checked')"));
            });
    }

    [Fact]
    public async Task Waits_for_a_controlled_checkbox_that_commits_its_state_after_a_timer()
    {
        await RunAsync(
            """
            <label><input type="checkbox" id="box"
              onclick="const box = this; const next = box.checked; event.preventDefault();
                setTimeout(() => { box.checked = next; }, 300)">Notify</label>
            """,
            async session =>
            {
                await session.PerformAsync(await FindAsync(session, "checkbox", "Notify"), new LocatorAction.Check(), CancellationToken.None);
                Assert.True(await EvaluateAsync<bool>(session, "() => document.getElementById('box').checked"));
            });
    }

    [Fact]
    public async Task Waits_for_a_switch_whose_aria_checked_flips_after_a_timer()
    {
        await RunAsync(
            """
            <button role="switch" id="toggle" aria-checked="true"
              onclick="setTimeout(() => this.setAttribute('aria-checked', String(this.getAttribute('aria-checked') !== 'true')), 300)">Wi-Fi</button>
            """,
            async session =>
            {
                await session.PerformAsync(await FindAsync(session, "switch", "Wi-Fi"), new LocatorAction.Uncheck(), CancellationToken.None);
                Assert.Equal("false", await EvaluateAsync<string>(session, "() => document.getElementById('toggle').getAttribute('aria-checked')"));
            });
    }

    [Fact]
    public async Task Fails_a_rejected_click_on_a_control_a_later_re_render_replaces()
    {
        await RunAsync(
            """
            <div id="terms"><label><input type="checkbox" id="box" onclick="event.preventDefault();
              setTimeout(() => { document.getElementById('terms').innerHTML = '<label><input type=checkbox id=box>Agree</label>'; }, 300)">Agree</label></div>
            """,
            async session =>
            {
                var box = await FindAsync(session, "checkbox", "Agree");
                var error = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(box, new LocatorAction.Check(), CancellationToken.None));
                Assert.Equal("NOT_ACTIONABLE", error.Code);
                Assert.Equal("check clicked the control but its checked state did not change before the control was replaced", error.Message);
            });
    }

    [Fact]
    public async Task Fails_a_click_that_left_the_control_as_it_was()
    {
        await RunAsync(
            """<label><input type="checkbox" id="box" onclick="event.preventDefault()">Locked</label>""",
            async session =>
            {
                var box = await FindAsync(session, "checkbox", "Locked");
                var error = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(box, new LocatorAction.Check(), CancellationToken.None));
                Assert.Equal("NOT_ACTIONABLE", error.Code);
                Assert.Equal("check clicked the control but its checked state did not change", error.Message);
            });
    }

    [Fact]
    public async Task Refuses_to_uncheck_a_radio_without_clicking_it()
    {
        await RunAsync(
            CountClicks + """<label><input type="radio" name="plan" id="pro" checked>Pro</label>""",
            async session =>
            {
                var pro = await FindAsync(session, "radio", "Pro");
                var error = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(pro, new LocatorAction.Uncheck(), CancellationToken.None));
                Assert.Equal("NOT_ACTIONABLE", error.Code);
                Assert.Equal(0, await EvaluateAsync<int>(session, "() => window.clicks"));
            });
    }

    [Fact]
    public async Task Refuses_a_control_that_cannot_be_checked()
    {
        await RunAsync(
            """<button id="go">Go</button>""",
            async session =>
            {
                var go = await FindAsync(session, "button", "Go");
                var error = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(go, new LocatorAction.Check(), CancellationToken.None));
                Assert.Equal("NOT_ACTIONABLE", error.Code);
            });
    }

}
