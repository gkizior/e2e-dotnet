// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.WebPlatform;

[Collection(BrowserCollection.Name)]
public sealed class WebPlatformIntegrationTests
{
    // Not ported, the port has no such feature: frameLocator, waitForDownload, browser.route, browser.onDialog, browser.goto,
    // CSS locators, the expect(browser) matchers, click modifiers and secondaryTap, harness step records, and report.json.
    [Theory]
    [InlineData("deterministic queries and reads")]
    [InlineData("a textarea value compares raw")]
    [InlineData("toHaveAttribute reads the value attribute of plain fields")]
    [InlineData("a viewport set before the first navigation holds through app.open")]
    public async Task Passes(string title)
    {
        Assert.Null(await RunAsync(Scenarios[title]));
    }

    [Fact]
    public async Task Sets_a_viewport_before_the_first_navigation_without_opening_a_page()
    {
        var error = Assert.IsType<TestException>(await RunAsync(Scenarios["a viewport set before the first navigation opens no page"]));
        Assert.Equal("APP_NOT_OPEN", error.Code);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/113: the message prints the newlines raw, not escaped")]
    public async Task Fails_toHaveValue_on_a_textarea_against_the_raw_value_and_prints_what_it_compared()
    {
        // The web engine reports the value raw, and the matcher neither collapses the newlines to match nor prints a string it never compared.
        var error = Assert.IsType<TestException>(await RunAsync(Scenarios["a textarea value is never normalized to match"]));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.Contains("observed value \"line1\\n\\nline2  \"", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denies_toHaveAttribute_on_a_password_field_that_has_a_value_attribute_with_POLICY_DENIED_negated_too()
    {
        // The engine withholds a secure field's value attribute, so the matcher must not read the gap as absent and pass the negated form.
        foreach (var title in new[] { "toHaveAttribute never judges a secure field", "a negated toHaveAttribute never judges a secure field" })
        {
            var error = Assert.IsType<TestException>(await RunAsync(Scenarios[title]));
            Assert.Equal("POLICY_DENIED", error.Code);
            Assert.Contains("reading values from a secure field is denied", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("marker-5e0c", error.ToString(), StringComparison.Ordinal);
        }

        var control = Assert.IsType<TestException>(await RunAsync(Scenarios["a negated toHaveAttribute fails on a plain field that holds the same marker"]));
        Assert.Equal("ASSERTION_FAILED", control.Code);
        Assert.Contains("observed attribute \"value\" \"marker-5e0c\"", control.Message, StringComparison.Ordinal);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/112: InputValueAsync returns null instead of POLICY_DENIED")]
    public async Task Denies_secure_value_reads_with_POLICY_DENIED()
    {
        var error = Assert.IsType<TestException>(await RunAsync(Scenarios["secure fields refuse value reads"]));
        Assert.Equal("POLICY_DENIED", error.Code);
    }

    [Fact]
    public async Task Denies_toHaveValue_on_a_filled_password_field_with_POLICY_DENIED_never_judging_it_empty()
    {
        // The engine withholds a secure field's value, so the matcher must not read the gap as '' and pass a filled password field as cleared.
        var error = Assert.IsType<TestException>(await RunAsync(Scenarios["toHaveValue never judges a secure field"]));
        Assert.Equal("POLICY_DENIED", error.Code);
        Assert.Contains("reading values from a secure field is denied", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("marker-9b1f", error.ToString(), StringComparison.Ordinal);
        var control = Assert.IsType<TestException>(await RunAsync(Scenarios["toHaveValue reads a plain field that holds the same marker"]));
        Assert.Equal("ASSERTION_FAILED", control.Code);
        Assert.Contains("observed value \"marker-9b1f\"", control.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Denies_navigation_to_a_forbidden_scheme()
    {
        var error = Assert.IsType<TestException>(await RunAsync(Scenarios["forbidden URL schemes are refused"]));
        Assert.Equal("POLICY_DENIED", error.Code);
    }

    [Fact]
    public async Task Denies_a_wrapped_or_non_http_s_scheme_on_app_open_and_browser_goto_before_it_loads()
    {
        // A local file the wrapped schemes would load. A navigation that went through would show its text.
        var directory = Directory.CreateTempSubdirectory("e2e-schemes");
        var markerPath = Path.Combine(directory.FullName, "marker.txt");
        await File.WriteAllTextAsync(markerPath, "marker-local-file\n");
        var marker = new Uri(markerPath).AbsoluteUri;

        // The browser.goto steps are not ported: the port has no goto.
        foreach (var url in new[] { "view-source:" + marker, "VIEW-SOURCE:" + marker, "  view-source:" + marker, "blob:http://127.0.0.1/x", "about:srcdoc" })
        {
            var error = Assert.IsType<TestException>(await RunAsync(async session =>
            {
                await session.App.OpenAsync();
                await session.App.OpenAsync(url);
                Assert.DoesNotContain("marker-local-file", await session.Browser.EvaluateAsync<string>("() => document.body?.innerText ?? ''"));
            }));
            Assert.Equal("POLICY_DENIED", error.Code);
            Assert.Matches("^forbidden URL scheme: (view-source|blob|about):$", error.Message);
        }

        Assert.Null(await RunAsync(async session =>
        {
            await session.App.OpenAsync();
            await session.App.OpenAsync("about:blank");
            Assert.Equal("about:blank", await session.Browser.UrlAsync());
        }));

        var cookie = Assert.IsType<TestException>(await RunAsync(async session =>
        {
            await session.App.OpenAsync();
            await session.Browser.SetCookiesAsync([new BrowserCookie { Name = "flavor", Value = "oatmeal", Url = "about:blank" }]);
        }));
        Assert.Equal("POLICY_DENIED", cookie.Code);
        Assert.Equal("cookie URL must be http(s): about:blank", cookie.Message);
        directory.Delete(recursive: true);
    }

    // The upstream kitchen-sink tests these assertions read, by title.
    private static readonly Dictionary<string, Func<E2ESession, Task>> Scenarios = new()
    {
        ["deterministic queries and reads"] = async session =>
        {
            var screen = session.Screen;
            await session.App.OpenAsync();

            await Expect.That(screen.GetByRole("heading", "Home")).ToBeVisibleAsync();
            await Expect.That(screen.GetByLabel("Email")).ToBeVisibleAsync();
            await Expect.That(screen.GetByPlaceholder("you@example.test")).ToBeVisibleAsync();
            await Expect.That(screen.GetByText("Item Alpha")).ToBeVisibleAsync();
            await Expect.That(screen.GetByDisplayValue("hello-value")).ToBeVisibleAsync();
            await Expect.That(screen.GetByTestId("items")).ToBeVisibleAsync();

            await Expect.That(screen.GetByTestId("item")).ToHaveCountAsync(3);
            await Expect.That(screen.GetByTestId("item").Filter("Beta")).ToHaveCountAsync(1);

            Assert.Equal(3, await screen.GetByTestId("item").CountAsync());

            var texts = new List<string>();
            foreach (var item in await screen.GetByTestId("item").AllAsync())
            {
                texts.Add(await item.TextContentAsync() ?? string.Empty);
            }

            Assert.Equal(["Item Alpha", "Item Beta", "Item Gamma"], texts);
            Assert.Equal(texts, await screen.GetByTestId("item").AllTextContentsAsync());
            Assert.Empty(await screen.GetByTestId("missing").AllAsync());
            await Expect.That(screen.GetByTestId("item")).ToHaveTextAsync(["Item Alpha", new Regex("Beta"), "Item Gamma"]);
            await Expect.That(screen.GetByTestId("item")).ToContainTextAsync(["Alpha", "Beta", "Gamma"]);
            await Expect.That(screen.GetByTestId("item")).ToContainTextAsync(["Alpha", "Gamma"]);
            await Expect.That(screen.GetByTestId("item")).Not.ToContainTextAsync(["Gamma", "Alpha"], timeout: TimeSpan.FromMilliseconds(200));
            await Expect.That(screen.GetByTestId("item")).Not.ToHaveTextAsync(["Item Alpha", "Item Beta"], timeout: TimeSpan.FromMilliseconds(200));

            // The browser.locator('#class-card') and '#fixture-image' reads are not ported: the port has no CSS locator.
            Assert.Null(await screen.GetByTestId("items").GetAttributeAsync("class"));
            Assert.Equal(string.Empty, await screen.GetByLabel("Readonly").GetAttributeAsync("readonly"));
            Assert.Null(await screen.GetByTestId("items").GetAttributeAsync("constructor"));
            Assert.Null(await screen.GetByTestId("items").GetAttributeAsync("toString"));
            Assert.Null(await screen.GetByTestId("items").GetAttributeAsync("__proto__"));
        },
        ["a textarea value compares raw"] = async session =>
        {
            await session.App.OpenAsync();
            await Expect.That(session.Screen.GetByLabel("Notes")).ToHaveValueAsync("line1\n\nline2  ");
            await Expect.That(session.Screen.GetByLabel("Notes")).ToHaveValueAsync(new Regex("^line1\n\nline2 {2}$"));
            await Expect.That(session.Screen.GetByLabel("Notes")).Not.ToHaveValueAsync("line1 line2", TimeSpan.FromMilliseconds(200));
        },
        ["a textarea value is never normalized to match"] = async session =>
        {
            await session.App.OpenAsync();
            await Expect.That(session.Screen.GetByLabel("Notes")).ToHaveValueAsync("line1 line2", TimeSpan.FromMilliseconds(500));
        },
        ["toHaveAttribute reads the value attribute of plain fields"] = async session =>
        {
            await session.App.OpenAsync("/value-attributes");
            await Expect.That(session.Screen.GetByLabel("Plain")).ToHaveAttributeAsync("value", "marker-5e0c");
            await Expect.That(session.Screen.GetByLabel("Blank")).Not.ToHaveAttributeAsync("value", TimeSpan.FromMilliseconds(200));
        },
        ["toHaveAttribute never judges a secure field"] = async session =>
        {
            await session.App.OpenAsync("/value-attributes");
            await Expect.That(session.Screen.GetByLabel("Secret")).ToHaveAttributeAsync("value");
        },
        ["a negated toHaveAttribute never judges a secure field"] = async session =>
        {
            await session.App.OpenAsync("/value-attributes");
            await Expect.That(session.Screen.GetByLabel("Secret")).Not.ToHaveAttributeAsync("value");
        },
        ["a negated toHaveAttribute fails on a plain field that holds the same marker"] = async session =>
        {
            await session.App.OpenAsync("/value-attributes");
            await Expect.That(session.Screen.GetByLabel("Plain")).Not.ToHaveAttributeAsync("value", TimeSpan.FromMilliseconds(500));
        },
        ["secure fields refuse value reads"] = async session =>
        {
            await session.App.OpenAsync();
            await session.Screen.GetByLabel("Password").FillAsync("hunter2");
            await session.Screen.GetByLabel("Password").InputValueAsync();
        },
        ["toHaveValue never judges a secure field"] = async session =>
        {
            await session.App.OpenAsync();
            await session.Screen.GetByLabel("Password").FillAsync("marker-9b1f");
            await Expect.That(session.Screen.GetByLabel("Password")).ToHaveValueAsync(string.Empty);
        },
        ["toHaveValue reads a plain field that holds the same marker"] = async session =>
        {
            await session.App.OpenAsync();
            await session.Screen.GetByLabel("Email").FillAsync("marker-9b1f");
            await Expect.That(session.Screen.GetByLabel("Email")).ToHaveValueAsync(string.Empty, TimeSpan.FromMilliseconds(500));
        },
        ["a viewport set before the first navigation holds through app.open"] = async session =>
        {
            await session.Browser.SetViewportAsync(390, 600);
            await session.App.OpenAsync();
            Assert.Equal(390, await session.Browser.EvaluateAsync<int>("() => window.innerWidth"));
            await session.App.RestartAsync();
            Assert.Equal(390, await session.Browser.EvaluateAsync<int>("() => window.innerWidth"));
            await session.App.ClearStateAsync();
            await session.App.OpenAsync();
            Assert.Equal(390, await session.Browser.EvaluateAsync<int>("() => window.innerWidth"));
        },
        ["a viewport set before the first navigation opens no page"] = async session =>
        {
            await session.Browser.SetViewportAsync(390, 600);
            await session.Screen.GetByTestId("items").TapAsync(new ActionOptions { Timeout = TimeSpan.FromMilliseconds(200) });
        },
        ["forbidden URL schemes are refused"] = async session =>
        {
            await session.App.OpenAsync();
            await session.App.OpenAsync("javascript:alert(1)");
        },
    };

    // Runs one upstream test body in its own session and returns what failed it, as the upstream run's result does.
    private static async Task<Exception?> RunAsync(Func<E2ESession, Task> body)
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = new ScriptedModel(_ => ModelResponses.Done("passed", "unused")),
            BaseUrl = site.Url,
            TestTitle = "web platform",
            ActionTimeout = TimeSpan.FromSeconds(5),
            AssertionTimeout = TimeSpan.FromSeconds(4),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        Exception? error = null;
        try
        {
            await body(session);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return error;
    }
}
