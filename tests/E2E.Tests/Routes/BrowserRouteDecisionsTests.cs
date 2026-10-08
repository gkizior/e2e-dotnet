// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;

namespace E2E.Tests.Routes;

[Collection(BrowserCollection.Name)]
public sealed class BrowserRouteDecisionsTests
{
    [Fact]
    public Task Answers_with_the_route_registered_last_when_two_fulfill() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/**", route => route.FulfillAsync(new RouteFulfillResponse { Body = "earlier" }));
        await routes.Browser.RouteAsync("**/api/quote", route => route.FulfillAsync(new RouteFulfillResponse { Body = "later" }));
        Assert.Equal("later", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
    });

    [Fact]
    public Task Continue_goes_to_the_network_past_an_earlier_route() => WithRoutesAsync(async routes =>
    {
        var earlier = 0;
        await routes.Browser.RouteAsync("**/api/**", route =>
        {
            earlier++;
            return route.FulfillAsync(new RouteFulfillResponse { Body = "earlier" });
        });
        await routes.Browser.RouteAsync("**/api/quote", route => route.ContinueAsync());
        Assert.Equal("network:/api/quote", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Equal(0, earlier);
        Assert.Equal(1, routes.Site.HitsOf("/api/quote"));
    });

    [Fact]
    public Task Fallback_hands_the_request_to_the_route_before_it() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/**", route => route.FulfillAsync(new RouteFulfillResponse { Body = "earlier" }));
        await routes.Browser.RouteAsync("**/api/quote", route => route.FallbackAsync());
        Assert.Equal("earlier", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
    });

    [Theory]
    [InlineData("continue")]
    [InlineData("fallback")]
    public Task Keeps_the_configured_site_header_through(string name) => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/echo", route => name == "continue" ? route.ContinueAsync() : route.FallbackAsync());
        var echo = await routes.FetchEchoAsync(routes.Site.Origin + "/echo");
        Assert.Equal("open-sesame", echo.Headers["x-gate"]);
    });

    [Fact]
    public Task Continue_replaces_the_request_headers_and_adds_the_site_header_on_top() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/echo", route => route.ContinueAsync(new RouteContinueOverrides
        {
            Headers = new Dictionary<string, string>(route.Request.Headers) { ["X-Added"] = "yes", ["X-GATE"] = "forged" },
        }));
        var echo = await routes.FetchEchoAsync(routes.Site.Origin + "/echo");
        Assert.Equal("yes", echo.Headers["x-added"]);
        Assert.Equal("open-sesame", echo.Headers["x-gate"]);
    });

    [Fact]
    public Task Continue_sends_a_rewritten_url_method_and_body_without_touching_the_original() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", route => route.ContinueAsync(new RouteContinueOverrides { Url = "/echo", Method = "PUT", PostData = "payload" }));
        var echo = await routes.FetchEchoAsync(routes.Site.Origin + "/api/quote", new { method = "POST", body = "original" });
        Assert.Equal(("/echo", "PUT", "payload"), (echo.Path, echo.Method, echo.Body));
        Assert.Equal("open-sesame", echo.Headers["x-gate"]);
        Assert.Null(routes.Site.HitsOf("/api/quote"));
        Assert.Equal(1, routes.Site.HitsOf("/echo"));
    });

    [Fact]
    public Task Continue_to_another_site_leaves_the_site_header_behind() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", route => route.ContinueAsync(new RouteContinueOverrides { Url = routes.Site.OtherSite + "/echo" }));
        var echo = await routes.FetchEchoAsync(routes.Site.Origin + "/api/quote");
        Assert.Equal("localhost:" + routes.Site.Port, echo.Headers["host"]);
        Assert.False(echo.Headers.ContainsKey("x-gate"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
    });

    [Theory]
    [InlineData("a forbidden scheme", "file:///etc/hosts", "POLICY_DENIED", "forbidden URL scheme: file:")]
    [InlineData("a wrapped scheme", "view-source:file:///etc/hosts", "POLICY_DENIED", "forbidden URL scheme: view-source:")]
    [InlineData("about:blank", "about:blank", "INVALID_ARGUMENT", "route.continue url must keep the request's http: scheme; got about:")]
    [InlineData("another scheme", "https://127.0.0.1/echo", "INVALID_ARGUMENT", "route.continue url must keep the request's http: scheme; got https:")]
    public Task Continue_to_aborts_the_request_and_fails_the_next_step(string name, string url, string code, string message) => WithRoutesAsync(async routes =>
    {
        _ = name;
        await routes.Browser.RouteAsync("**/api/quote", route => route.ContinueAsync(new RouteContinueOverrides { Url = url }));
        Assert.Equal("failed", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
        var failure = await routes.NextStepFailureAsync();
        Assert.Equal((code, message), (failure?.Code, failure?.Message));
    });

    [Fact]
    public Task Fulfills_a_project_file_with_its_bytes_and_a_type_from_its_extension() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", route => route.FulfillAsync(new RouteFulfillResponse { Path = "quote.json" }));
        Assert.Equal(new Reply("application/json", "{\"cents\":4200}\n"), await routes.FetchReplyAsync(routes.Site.Origin + "/api/quote"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
    });

    [Fact]
    public Task Fulfills_under_the_given_contentType_or_a_content_type_header() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", route => route.FulfillAsync(new RouteFulfillResponse { Json = new { cents = 1 }, ContentType = "application/vnd.quote+json" }));
        Assert.Equal(new Reply("application/vnd.quote+json", "{\"cents\":1}"), await routes.FetchReplyAsync(routes.Site.Origin + "/api/quote"));
        await routes.Browser.RouteAsync("**/api/quote", route => route.FulfillAsync(new RouteFulfillResponse
        {
            Path = "quote.json",
            Headers = new Dictionary<string, string> { ["Content-Type"] = "text/plain" },
        }));
        Assert.Equal(new Reply("text/plain", "{\"cents\":4200}\n"), await routes.FetchReplyAsync(routes.Site.Origin + "/api/quote"));
    });

    // Not portable, because the C# types refuse them at compile time: an unknown
    // fulfill key, an unknown continue key, non-string continue headers, and an
    // abort error code.
    [Theory]
    [InlineData("two fulfill sources")]
    [InlineData("a missing fulfill file")]
    [InlineData("a fulfill status out of range")]
    [InlineData("a continue header name outside the token grammar")]
    [InlineData("a line break in a fulfill header value")]
    public Task Rejects_before_deciding_and_aborts_the_request(string name) => WithRoutesAsync(async routes =>
    {
        var (decide, message) = name switch
        {
            "two fulfill sources" => (
                (Func<WebRoute, Task>)(route => route.FulfillAsync(new RouteFulfillResponse { Body = "a", Json = new { b = 1 } })),
                "route.fulfill takes one of json, body, or path; got json and body"),
            "a missing fulfill file" => (
                route => route.FulfillAsync(new RouteFulfillResponse { Path = "missing.json" }),
                "route.fulfill path is not a readable file: " + Path.Combine(routes.ProjectRoot, "missing.json")),
            "a fulfill status out of range" => (
                route => route.FulfillAsync(new RouteFulfillResponse { Status = 42 }),
                "route.fulfill status must be an integer from 100 to 599"),
            "a continue header name outside the token grammar" => (
                route => route.ContinueAsync(new RouteContinueOverrides { Headers = new Dictionary<string, string> { ["x a"] = "b" } }),
                "route.continue has an invalid header name: \"x a\""),
            _ => (
                (Func<WebRoute, Task>)(route => route.FulfillAsync(new RouteFulfillResponse { Headers = new Dictionary<string, string> { ["x-a"] = "b\r\nset-cookie: c=d" } })),
                "route.fulfill header \"x-a\" must not contain a control character"),
        };
        await routes.Browser.RouteAsync("**/api/quote", decide);
        Assert.Equal("failed", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
        var failure = await routes.NextStepFailureAsync();
        Assert.Equal(("INVALID_ARGUMENT", message), (failure?.Code, failure?.Message));
    });

    [Fact]
    public Task Aborts_the_request_when_the_handler_returns_without_deciding_and_fails_the_next_step() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", _ => Task.CompletedTask);
        Assert.Equal("failed", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Null(routes.Site.HitsOf("/api/quote"));
        var failure = await routes.NextStepFailureAsync();
        Assert.Equal(
            ("ACTION_FAILED", "route handler returned without calling fulfill, continue, fallback, or abort"),
            (failure?.Code, failure?.Message));
    });

    [Fact]
    public Task Keeps_the_first_decision_when_the_handler_decides_twice_and_fails_the_next_step() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", route =>
        {
            _ = route.FulfillAsync(new RouteFulfillResponse { Body = "first" });
            return route.FulfillAsync(new RouteFulfillResponse { Body = "second" });
        });
        Assert.Equal("first", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        var failure = await routes.NextStepFailureAsync();
        Assert.Equal(("ACTION_FAILED", "route handler already decided; fulfill called twice"), (failure?.Code, failure?.Message));
    });

    [Fact]
    public Task Takes_the_decision_the_moment_fulfill_is_called_awaited_or_not() => WithRoutesAsync(async routes =>
    {
        await routes.Browser.RouteAsync("**/api/quote", route =>
        {
            _ = route.FulfillAsync(new RouteFulfillResponse { Path = "quote.json" });
            return Task.CompletedTask;
        });
        Assert.Equal("{\"cents\":4200}\n", await routes.FetchTextAsync(routes.Site.Origin + "/api/quote"));
        Assert.Equal(routes.Site.Origin + "/", await routes.Browser.UrlAsync());
    });

    [Fact]
    public async Task Sets_a_cookie_with_a_relative_url_on_the_base_URL()
    {
        using var site = await TinySite.StartAsync("<!DOCTYPE html><html><body></body></html>");
        var session = await WebEngineTests.StartAsync(site, new ScriptedModel(_ => ModelResponses.Done("passed", "unused")), cache: null);
        await WebEngineTests.RunAsync(session, async () =>
        {
            var browser = session.Browser;
            await browser.SetCookiesAsync([new BrowserCookie { Name = "flavor", Value = "oatmeal", Url = "/" }]);
            var cookie = Assert.Single(await browser.CookiesAsync());
            Assert.Equal(("flavor", "oatmeal", "127.0.0.1", "/"), (cookie.Name, cookie.Value, cookie.Domain, cookie.Path));
        });
    }

    /// <summary>
    /// Runs <paramref name="body"/> against a counting server, in a session whose app
    /// is that server, with a configured site header and a project root that holds
    /// <c>quote.json</c>. The page is open on the server's root.
    /// </summary>
    private static async Task WithRoutesAsync(Func<RouteSession, Task> body)
    {
        using var site = CountingSite.Start();
        var projectRoot = Directory.CreateTempSubdirectory("e2e-routes-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(projectRoot, "quote.json"), "{\"cents\":4200}\n");
            var session = await E2ESession.StartAsync(new E2ESessionOptions
            {
                Engine = new WebEngine(new WebEngineOptions
                {
                    Headless = true,
                    Headers = new Dictionary<string, string> { ["X-Gate"] = "open-sesame" },
                }),
                BaseUrl = site.Origin + "/",
                ProjectRoot = projectRoot,
                TestTitle = "browser.route decisions",
                ActionTimeout = TimeSpan.FromSeconds(10),
                TestTimeout = TimeSpan.FromSeconds(30),
            });
            await WebEngineTests.RunAsync(session, async () =>
            {
                await session.App.OpenAsync("/");
                site.Hits.Clear();
                await body(new RouteSession(site, session.Browser, projectRoot));
            });
        }
        finally
        {
            Directory.Delete(projectRoot, recursive: true);
        }
    }

    private sealed record Reply(string? ContentType, string Body);

    private sealed record Echo(string Path, string Method, Dictionary<string, string> Headers, string Body);

    private sealed record RouteSession(CountingSite Site, Browser Browser, string ProjectRoot)
    {
        /// <summary>Fetches <paramref name="url"/> from the page; a failed fetch reads as <c>failed</c>.</summary>
        public async Task<string> FetchTextAsync(string url) =>
            (await Browser.EvaluateAsync<string>("(target) => fetch(target).then((reply) => reply.text(), () => 'failed')", url))!;

        /// <summary>Fetches <paramref name="url"/> from the page and returns its content type and body.</summary>
        public async Task<Reply> FetchReplyAsync(string url) =>
            (await Browser.EvaluateAsync<Reply>(
                "async (target) => { const reply = await fetch(target); return { contentType: reply.headers.get('content-type'), body: await reply.text() }; }",
                url))!;

        /// <summary>Fetches <paramref name="url"/> from the page and parses what <c>/echo</c> received.</summary>
        public async Task<Echo> FetchEchoAsync(string url, object? init = null)
        {
            var text = await Browser.EvaluateAsync<string>("([target, options]) => fetch(target, options ?? undefined).then((reply) => reply.text())", new[] { url, init });
            return JsonSerializer.Deserialize<Echo>(text!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        }

        /// <summary>The route failure the next step reports, or null when it passes.</summary>
        public async Task<E2EException?> NextStepFailureAsync()
        {
            try
            {
                await Browser.UrlAsync();
                return null;
            }
            catch (E2EException ex)
            {
                return ex;
            }
        }
    }
}
