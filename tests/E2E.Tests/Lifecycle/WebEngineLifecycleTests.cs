// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Lifecycle;

[Collection(BrowserCollection.Name)]
public sealed class WebEngineLifecycleTests
{
    [Fact]
    public async Task Serves_consecutive_attempts_from_one_browser_and_survives_an_attempt_close()
    {
        using var site = await FixtureApp.StartAsync();
        var engine = new WebEngine(new WebEngineOptions { Headless = true });

        await using (var first = await OpenAttemptAsync(engine, site.Url))
        {
            Assert.Equal("Home", await HeadingAsync(first));
            Assert.Equal("/", (await first.ObserveAsync(CancellationToken.None)).Route);
        }

        // The next attempt starts on the same engine once the first one closed.
        await using var second = await OpenAttemptAsync(engine, site.Url);
        Assert.Equal("Home", await HeadingAsync(second));
    }

    [Fact]
    public async Task Relaunches_after_dispose_and_stays_idempotent()
    {
        using var site = await FixtureApp.StartAsync();
        var engine = new WebEngine(new WebEngineOptions { Headless = true });
        var first = await OpenAttemptAsync(engine, site.Url);
        Assert.Equal("Home", await HeadingAsync(first));
        await first.DisposeAsync();
        await first.DisposeAsync();

        await using var second = await OpenAttemptAsync(engine, site.Url);
        Assert.Equal("Home", await HeadingAsync(second));
    }

    [Fact]
    public async Task Follows_the_window_under_viewport_null_and_swipes_by_it()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await WebSemanticsTests.OpenAsync(site.Url + "form", new WebEngineOptions { Headless = true, Viewport = null });
        var page = WebEngine.SurfaceOf(session)!.Page();
        Assert.Null(page.ViewportSize);
        var measured = await page.EvaluateAsync<int[]>("() => [innerWidth, innerHeight]");
        Assert.True(measured[0] > 0);

        await page.SetContentAsync("<div style=\"height: 5000px\">tall</div>");
        await session.SwipeAsync(ScrollDirection.Down, CancellationToken.None);
        await page.WaitForFunctionAsync("() => window.scrollY > 0");
        // The swipe moves three quarters of the window height (upstream moves half).
        Assert.Equal((int)Math.Round(measured[1] * 0.75, MidpointRounding.AwayFromZero), await page.EvaluateAsync<int>("() => window.scrollY"));
    }

    [Fact]
    public async Task Reports_an_unopened_page_as_INVALID_STATE_never_as_a_missing_node()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await new WebEngine(new WebEngineOptions { Headless = true }).StartAsync(
            new EngineStartOptions { BaseUrl = site.Url, ActionTimeout = TimeSpan.FromSeconds(10) },
            CancellationToken.None);
        var error = await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(CancellationToken.None));
        Assert.Equal("INVALID_STATE", error.Code);
    }

    [Fact]
    public async Task Cancels_an_in_flight_operation_when_its_signal_aborts_instead_of_waiting_out_Playwright()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await new WebEngine(new WebEngineOptions { Headless = true }).StartAsync(
            new EngineStartOptions { BaseUrl = site.Url, ActionTimeout = TimeSpan.FromSeconds(10) },
            CancellationToken.None);
        using var controller = new CancellationTokenSource();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var pending = session.OpenAsync(site.Url + "slow", controller.Token);
        controller.CancelAfter(TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAnyAsync<E2EException>(() => pending);
        Assert.Equal("CANCELLED", error.Code);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Reports_the_configured_test_id_attribute_as_testId_on_observed_and_located_nodes_alike()
    {
        using var site = await TinySite.StartAsync("""
            <!DOCTYPE html>
            <html><body><button data-qa="go">Go</button><button data-testid="stop">Stop</button></body></html>
            """);
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(new WebEngineOptions { Headless = true, TestIdAttribute = "data-qa" }),
            BaseUrl = site.Url,
            TestTitle = "web engine lifecycle > test id",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        Exception? failure = null;
        try
        {
            await session.App.OpenAsync("/");
            var observed = WebSemanticsTests.Flatten((await session.Screen.ObserveAsync(CancellationToken.None)).Roots).ToList();
            var go = observed.Single(node => node.Name == "Go");
            var stop = observed.Single(node => node.Name == "Stop");
            Assert.Equal("go", go.TestId);
            Assert.Null(stop.TestId);
            // The tree carries the id as a field, not as an attribute.
            Assert.False(go.Attributes.ContainsKey("data-qa"));

            Assert.Equal(["go"], (await session.Screen.GetByTestId("go").ResolveAsync(CancellationToken.None)).Select(node => node.TestId));
            Assert.Empty(await session.Screen.GetByTestId("stop").ResolveAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            session.Complete(failure);
        }
    }

    /// <summary>Starts an attempt on <paramref name="engine"/> and opens the home page.</summary>
    private static async Task<IEngineSession> OpenAttemptAsync(WebEngine engine, string url)
    {
        var session = await engine.StartAsync(new EngineStartOptions { BaseUrl = url, ActionTimeout = TimeSpan.FromSeconds(10) }, CancellationToken.None);
        await session.OpenAsync(url, CancellationToken.None);
        return session;
    }

    private static async Task<string?> HeadingAsync(IEngineSession session) =>
        WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).First(node => node.Role == "heading").Name;
}
