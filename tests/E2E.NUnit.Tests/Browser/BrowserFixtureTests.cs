// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;

namespace E2E.NUnit.Tests.Browser;

/// <summary>
/// The browser fixture against the playground's <c>/browser</c> page, ported from
/// upstream's <c>apps/testbed/tests/browser.e2e.ts</c>. "navigation verbs and the URL
/// and title matchers" waits for <c>browser.goto</c> and the browser matchers (#164).
/// </summary>
public sealed class BrowserFixtureTests : E2ETest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """{ "cache": { "mode": "off" } }""",
        AppContext.BaseDirectory,
        _ => null);

    protected override E2EConfig Config => FixtureConfig;

    protected override string? BaseUrl => E2E.Playground.Testbed.Url;

    [SetUp]
    public Task OpenBrowserPageAsync() => App.OpenAsync("/browser");

    [Test]
    public async Task WaitForURL_waits_out_a_delayed_navigation()
    {
        await Screen.GetByRole("button", "Go to about, soon").TapAsync();
        await Browser.WaitForURLAsync("/about");
        await Expect.That(Screen.GetByRole("heading", "About")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Reload_runs_the_page_again()
    {
        await Expect.That(Screen.GetByLabel("Loads")).ToHaveTextAsync("loads: 1");
        await Browser.ReloadAsync();
        await Expect.That(Screen.GetByLabel("Loads")).ToHaveTextAsync("loads: 2");
    }

    [Test]
    public async Task An_init_script_runs_before_the_page_reads_it_from_the_next_load_on()
    {
        await Expect.That(Screen.GetByLabel("Random")).ToHaveTextAsync("random: unseeded");
        await Browser.AddInitScriptAsync(WebInitScript.FromFunction("(value) => { Math.random = () => value; }"), 0.5);
        await Browser.ReloadAsync();
        await Expect.That(Screen.GetByLabel("Random")).ToHaveTextAsync("random: seeded");
    }

    [Test]
    public async Task The_viewport_size_is_what_the_page_measures()
    {
        await Browser.SetViewportAsync(500, 700);
        await Expect.That(Screen.GetByLabel("Viewport")).ToHaveTextAsync("500x700");
        await Browser.SetViewportAsync(1024, 640);
        await Expect.That(Screen.GetByLabel("Viewport")).ToHaveTextAsync("1024x640");
    }

    [Test]
    public async Task Cookies_set_from_the_test_reach_the_page_and_read_back()
    {
        await Expect.That(Screen.GetByLabel("Cookies")).ToHaveTextAsync("no cookies");

        await Browser.SetCookiesAsync([new BrowserCookie { Name = "theme", Value = "dark", Url = await Browser.UrlAsync() }]);
        await Browser.ReloadAsync();
        await Expect.That(Screen.GetByLabel("Cookies")).ToContainTextAsync("theme=dark");
        var theme = (await Browser.CookiesAsync()).FirstOrDefault(cookie => cookie.Name == "theme");
        Assert.That(theme?.Value, Is.EqualTo("dark"));
    }
}
