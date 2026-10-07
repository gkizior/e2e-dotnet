// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using static E2E.Tests.CheckActions.CheckPage;

namespace E2E.Tests.CheckActions;

/// <summary>
/// <c>check</c> and <c>uncheck</c> through a screen locator, ported from upstream
/// <c>check-actions.test.ts</c>. Upstream's locator is a CSS selector; this port finds the
/// control by role and name.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ThroughTheLocatorTargetTests
{
    [Fact]
    public async Task Checks_a_radio_the_app_replaces_with_its_selected_view()
    {
        await RunWithScreenAsync(
            """
            <div id="plan"><label><input type="radio" name="plan" id="pro"
              onchange="document.getElementById('plan').innerHTML = '<p>Pro selected</p>'">Pro</label></div>
            """,
            async session =>
            {
                await session.Screen.GetByRole("radio", "Pro").CheckAsync();
                Assert.Equal("Pro selected", await session.Browser.EvaluateAsync<string>("() => document.getElementById('plan').textContent"));
            });
    }

    [Fact]
    public async Task Leaves_what_replaced_the_control_to_the_next_read_even_an_unchecked_copy()
    {
        await RunWithScreenAsync(
            """
            <div id="terms"><label><input type="checkbox" id="box"
              onchange="document.getElementById('terms').innerHTML = '<label><input type=checkbox id=box>Agree</label>'">Agree</label></div>
            """,
            async session =>
            {
                await session.Screen.GetByRole("checkbox", "Agree").CheckAsync();
                Assert.False(await session.Browser.EvaluateAsync<bool>("() => document.getElementById('box').checked"));
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
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new E2E.Engine.WebEngine(headless: true),
            Model = new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")),
            BaseUrl = site.Url,
            TestTitle = "check actions > through the locator target",
            ActionTimeout = TimeSpan.FromSeconds(2),
        });
        await session.App.OpenAsync("/");

        await session.Screen.GetByRole("radio", "Pro").CheckAsync();
        await session.Browser.WaitForURLAsync("/next");
        session.Complete(null);
    }

    [Fact]
    public async Task Checks_and_unchecks_a_checkbox_and_clicks_nothing_already_in_the_wanted_state()
    {
        await RunWithScreenAsync(
            CountClicks + """<label><input type="checkbox" id="box">Notify</label>""",
            async session =>
            {
                var box = session.Screen.GetByRole("checkbox", "Notify");
                await box.CheckAsync();
                await box.CheckAsync();
                Assert.True(await session.Browser.EvaluateAsync<bool>("() => document.getElementById('box').checked"));
                await box.UncheckAsync();
                Assert.False(await session.Browser.EvaluateAsync<bool>("() => document.getElementById('box').checked"));
                Assert.Equal(2, await session.Browser.EvaluateAsync<int>("() => window.clicks"));
            });
    }

    [Fact]
    public async Task Checks_an_ARIA_checkbox()
    {
        await RunWithScreenAsync(
            """
            <div role="checkbox" id="box" aria-checked="false" tabindex="0"
              onclick="this.setAttribute('aria-checked', String(this.getAttribute('aria-checked') !== 'true'))">Notify</div>
            """,
            async session =>
            {
                await session.Screen.GetByRole("checkbox", "Notify").CheckAsync();
                Assert.Equal("true", await session.Browser.EvaluateAsync<string>("() => document.getElementById('box').getAttribute('aria-checked')"));
            });
    }

    [Fact]
    public async Task Fails_a_click_that_left_the_control_as_it_was()
    {
        await RunWithScreenAsync(
            """<label><input type="checkbox" id="box" onclick="event.preventDefault()">Locked</label>""",
            async session =>
            {
                var error = await Assert.ThrowsAsync<EngineException>(() => session.Screen.GetByRole("checkbox", "Locked").CheckAsync());
                Assert.Equal("NOT_ACTIONABLE", error.Code);
                Assert.Equal("check clicked the control but its checked state did not change", error.Message);
            });
    }

    [Fact]
    public async Task Refuses_to_uncheck_a_radio_without_clicking_it()
    {
        await RunWithScreenAsync(
            CountClicks + """<label><input type="radio" name="plan" id="pro" checked>Pro</label>""",
            async session =>
            {
                var error = await Assert.ThrowsAsync<EngineException>(() => session.Screen.GetByRole("radio", "Pro").UncheckAsync());
                Assert.Equal("NOT_ACTIONABLE", error.Code);
                Assert.Equal(0, await session.Browser.EvaluateAsync<int>("() => window.clicks"));
            });
    }

    [Fact]
    public async Task Refuses_a_control_that_cannot_be_checked()
    {
        await RunWithScreenAsync(
            """<button id="go">Go</button>""",
            async session =>
            {
                var error = await Assert.ThrowsAsync<EngineException>(() => session.Screen.GetByRole("button", "Go").CheckAsync());
                Assert.Equal("NOT_ACTIONABLE", error.Code);
            });
    }
}
