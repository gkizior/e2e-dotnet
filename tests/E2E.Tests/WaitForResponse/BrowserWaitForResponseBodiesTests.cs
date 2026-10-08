// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.WaitForResponse;

[Collection(BrowserCollection.Name)]
public sealed class BrowserWaitForResponseBodiesTests
{
    [Fact]
    public async Task Reads_a_full_body_as_text_and_JSON()
    {
        await RunAsync(TimeSpan.FromSeconds(2), async (session, site) =>
        {
            var response = await ObserveAsync(session, site, "/api/full");
            Assert.Equal(200, response.Status);
            Assert.Equal("{\"ok\":true}", await response.TextAsync());
            Assert.Equal(new Dictionary<string, bool> { ["ok"] = true }, await response.JsonAsync<Dictionary<string, bool>>());
        });
    }

    [Theory]
    [InlineData("/api/empty", 200)]
    [InlineData("/api/no-content", 204)]
    public async Task Resolves_a_genuinely_empty_body_to_an_empty_string(string pathname, int status)
    {
        await RunAsync(TimeSpan.FromSeconds(2), async (session, site) =>
        {
            var response = await ObserveAsync(session, site, pathname);
            Assert.Equal(status, response.Status);
            Assert.Equal("", await response.TextAsync());
        });
    }

    [Fact]
    public async Task Keeps_the_status_of_a_body_the_connection_cut_short_and_rejects_reading_it()
    {
        await RunAsync(TimeSpan.FromSeconds(2), async (session, site) =>
        {
            var response = await ObserveAsync(session, site, "/api/cut");
            Assert.Equal(200, response.Status);
            Assert.Equal(site.Origin + "/api/cut", response.Url);
            Assert.Equal("1000", response.Headers["content-length"]);
            var text = await Assert.ThrowsAsync<TestException>(() => response.TextAsync());
            Assert.Equal("ACTION_FAILED", text.Code);
            Assert.Equal("waitForResponse: response body could not be read: net::ERR_CONTENT_LENGTH_MISMATCH", text.Message);
            var json = await Assert.ThrowsAsync<TestException>(() => response.JsonAsync<object>());
            Assert.Equal("ACTION_FAILED", json.Code);
        });
    }

    [Fact]
    public async Task Matches_on_headers_inside_its_timeout_and_lets_text_wait_for_a_slower_body()
    {
        await RunAsync(TimeSpan.FromSeconds(2), async (session, site) =>
        {
            var response = await ObserveAsync(session, site, "/api/slow", TimeSpan.FromMilliseconds(500));
            Assert.Equal(200, response.Status);
            Assert.Equal("slow-body", await response.TextAsync());
        });
    }

    [Fact]
    public async Task Rejects_reading_a_body_that_never_finishes_once_the_action_budget_runs_out()
    {
        // The port's action budget is fixed for the session, so the session starts
        // with the 300ms budget upstream sets after the match.
        await RunAsync(TimeSpan.FromMilliseconds(300), async (session, site) =>
        {
            var response = await ObserveAsync(session, site, "/api/endless", TimeSpan.FromMilliseconds(500));
            Assert.Equal(200, response.Status);
            var error = await Assert.ThrowsAsync<TestException>(() => response.TextAsync());
            Assert.Equal("ACTION_FAILED", error.Code);
            Assert.Equal("waitForResponse: response body did not finish within 300ms", error.Message);
        });
    }

    [Fact]
    public async Task Keeps_the_status_of_a_redirect_and_rejects_reading_its_body()
    {
        await RunAsync(TimeSpan.FromSeconds(2), async (session, site) =>
        {
            var response = await ObserveAsync(session, site, "/api/redirect");
            Assert.Equal(302, response.Status);
            var error = await Assert.ThrowsAsync<TestException>(() => response.TextAsync());
            Assert.Equal("ACTION_FAILED", error.Code);
            Assert.Contains("Response body is unavailable for redirect responses", error.Message, StringComparison.Ordinal);
        });
    }

    /// <summary>Opens the body site's page in a session with the given action budget.</summary>
    private static async Task RunAsync(TimeSpan actionTimeout, Func<E2ESession, BodySite, Task> test)
    {
        using var site = BodySite.Start();
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")),
            BaseUrl = site.Origin + "/",
            TestTitle = "browser.waitForResponse bodies",
            ActionTimeout = actionTimeout,
        });
        await session.App.OpenAsync("/");
        await test(session, site);
        session.Complete(null);
    }

    /// <summary>
    /// Fires one page-side fetch that reads its body the way an app does and
    /// returns the response <c>WaitForResponseAsync</c> observed for it.
    /// </summary>
    private static async Task<WebResponse> ObserveAsync(E2ESession session, BodySite site, string pathname, TimeSpan? timeout = null)
    {
        // The step bound upstream's harness applies to every browser call.
        var bound = timeout ?? TimeSpan.FromSeconds(2);
        var waiting = session.Browser.WaitForResponseAsync("**" + pathname, timeout);
        await session.Browser.EvaluateAsync<object>(
            "(url) => { void fetch(url).then((reply) => reply.text()).catch(() => undefined); }",
            site.Origin + pathname);
        return await waiting.WaitAsync(bound);
    }
}
