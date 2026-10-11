// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using E2E.Engine;
using static E2E.Tests.ProtectedApp.ProtectedAppFixture;

namespace E2E.Tests.ProtectedApp;

/// <summary>
/// Reaching an app behind a gate: configured request headers reach the app's site on every context the
/// attempt opens, and never another site; basic-auth credentials answer a challenge wherever one is
/// issued, as Playwright's own do. A second fixture instance reached as <c>localhost</c> rather than
/// <c>127.0.0.1</c> stands in for a third-party site.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class WebHeadersBasicAuthTests
{
    [Fact]
    public async Task Sends_the_headers_to_the_apps_site_only_on_every_context_of_the_attempt()
    {
        using var app = await StartAsync();
        using var other = await StartAsync("localhost");
        await using var session = await StartSessionAsync(app, new WebEngineOptions { Headless = true, Headers = new Dictionary<string, string> { ["X-Fixture-Header"] = "let-me-in" } });

        Assert.Equal("let-me-in", await HeadingAtAsync(session, app.Url + "headers"));
        // A third-party site gets the request without them.
        Assert.Equal("none", await HeadingAtAsync(session, other.Url + "headers"));
        // A state reset replaces the context; the headers come along.
        await session.ClearStateAsync(CancellationToken.None);
        Assert.Equal("let-me-in", await HeadingAtAsync(session, app.Url + "headers"));
    }

    [Fact]
    public async Task Keeps_the_headers_on_a_request_an_attempt_route_lets_through()
    {
        using var app = await StartAsync();
        await using var session = await StartSessionAsync(app, new WebEngineOptions { Headless = true, Headers = new Dictionary<string, string> { ["x-fixture-header"] = "through-the-route" } });
        var routed = 0;
        // Registered after the header route, so it runs first, as a browser route handler does.
        await ((IBrowserSession)session).RouteAsync(
            global::E2E.Internal.Routes.Matcher("**/headers"),
            route =>
            {
                routed++;
                return route.FallbackAsync();
            },
            CancellationToken.None);

        Assert.Equal("through-the-route", await HeadingAtAsync(session, app.Url + "headers"));
        Assert.Equal(1, routed);
    }

    [Fact]
    public async Task Blocks_service_workers_under_headers_since_routing_never_sees_a_workers_requests()
    {
        using var app = await StartAsync();
        // Registers the fixture's worker and counts what the browser now holds:
        // a blocked registration resolves like a real one but registers nothing.
        const string Registrations = """
            async () => {
              await navigator.serviceWorker.register('/sw.js').catch(() => undefined);
              return (await navigator.serviceWorker.getRegistrations()).length;
            }
            """;
        await using (var plain = await StartSessionAsync(app, new WebEngineOptions { Headless = true }))
        {
            await HeadingAtAsync(plain, app.Url);
            // The fixture itself can register one: 127.0.0.1 is a secure context.
            Assert.Equal(1, await WebEngine.SurfaceOf(plain)!.Page().EvaluateAsync<int>(Registrations));
        }

        await using var session = await StartSessionAsync(app, new WebEngineOptions { Headless = true, Headers = new Dictionary<string, string> { ["x-fixture-header"] = "no-workers" } });
        await HeadingAtAsync(session, app.Url);
        Assert.Equal(0, await WebEngine.SurfaceOf(session)!.Page().EvaluateAsync<int>(Registrations));
    }

    [Fact]
    public async Task Answers_a_basic_auth_challenge_wherever_one_is_issued()
    {
        using var app = await StartAsync();
        using var other = await StartAsync("localhost");
        await using (var unauthenticated = await StartSessionAsync(app, new WebEngineOptions { Headless = true }))
        {
            Assert.Equal("Unauthorized", await HeadingAtAsync(unauthenticated, app.Url + "protected"));
        }

        await using var session = await StartSessionAsync(app, new WebEngineOptions { Headless = true, BasicAuth = new WebBasicAuth(Username, Password) });
        Assert.Equal("Protected", await HeadingAtAsync(session, app.Url + "protected"));
        Assert.Equal("Protected", await HeadingAtAsync(session, other.Url + "protected"));
    }

    [Fact]
    public async Task Answers_the_challenge_with_a_secret_password()
    {
        using var app = await StartAsync();
        // The port has no deferred secret: the password is a Secret value, so there is no resolver to ask.
        await using var session = await StartSessionAsync(app, new WebEngineOptions { Headless = true, BasicAuth = new WebBasicAuth(Username, Secret.Create("previewPassword", Password)) });

        Assert.Equal("Protected", await HeadingAtAsync(session, app.Url + "protected"));
    }

    [Fact]
    public async Task Reports_the_configured_user_agent_to_the_page_after_a_context_reset_too()
    {
        using var app = await StartAsync();
        await using var session = await StartSessionAsync(app, new WebEngineOptions { Headless = true, UserAgent = "Mozilla/5.0 e2e-probe playwright" });
        async Task<string?> UserAgent() => (await ((IBrowserSession)session).EvaluateAsync("() => navigator.userAgent", null, false, CancellationToken.None))?.GetString();

        await session.OpenAsync(app.Url, CancellationToken.None);
        Assert.Equal("Mozilla/5.0 e2e-probe playwright", await UserAgent());
        await session.ClearStateAsync(CancellationToken.None);
        await session.OpenAsync(app.Url, CancellationToken.None);
        Assert.Equal("Mozilla/5.0 e2e-probe playwright", await UserAgent());
    }

    [Fact]
    public async Task Runs_the_page_in_the_configured_locale_and_time_zone_after_a_context_reset_too()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, "<!DOCTYPE html><html><body></body></html>"));
        await using var session = (IBrowserSession)await WebSemanticsTests.OpenAsync(site.Url, new WebEngineOptions
        {
            Headless = true,
            Locale = "de-DE",
            TimezoneId = "Asia/Tokyo",
        });
        const string Read = "() => [navigator.language, Intl.DateTimeFormat().resolvedOptions().timeZone, new Date('2026-01-01T00:00:00Z').getTimezoneOffset()].join(' ')";
        const string Expected = "de-DE Asia/Tokyo -540";

        Assert.Equal(Expected, (await session.EvaluateAsync(Read, null, false, CancellationToken.None))?.GetString());
        await session.ClearStateAsync(CancellationToken.None);
        await session.OpenAsync(site.Url, CancellationToken.None);
        Assert.Equal(Expected, (await session.EvaluateAsync(Read, null, false, CancellationToken.None))?.GetString());
    }

    private static Task<IEngineSession> StartSessionAsync(TinySite app, WebEngineOptions options) =>
        new WebEngine(options).StartAsync(new EngineStartOptions { BaseUrl = app.Url, ActionTimeout = TimeSpan.FromSeconds(10) }, CancellationToken.None);

    /// <summary>Navigates and reads the page's one heading.</summary>
    private static async Task<string?> HeadingAtAsync(IEngineSession session, string url)
    {
        await session.OpenAsync(url, CancellationToken.None);
        return WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).First(node => node.Role == "heading").Name;
    }
}
