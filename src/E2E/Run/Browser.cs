// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// The browser the attempt drives, showing one active tab. It needs an engine
/// session that implements <see cref="IBrowserSession"/>, such as
/// <see cref="WebEngine"/>; on any other engine each member fails with
/// <c>UNSUPPORTED_CAPABILITY</c>.
/// </summary>
public sealed class Browser
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly IEngineSession _session;
    private readonly string _platform;
    private readonly string? _baseUrl;
    private readonly string _projectRoot;
    private readonly TimeSpan _actionTimeout;
    private readonly TimeSpan _assertionTimeout;
    private readonly Func<CancellationToken> _token;
    private readonly List<(object Pattern, Func<IBrowserRoute, Task> Handler)> _routes = [];

    internal Browser(IEngineSession session, string platform, string? baseUrl, string projectRoot, TimeSpan actionTimeout, TimeSpan assertionTimeout, Func<CancellationToken> token)
    {
        _session = session;
        _platform = platform;
        _baseUrl = baseUrl;
        _projectRoot = projectRoot;
        _actionTimeout = actionTimeout;
        _assertionTimeout = assertionTimeout;
        _token = token;
        Keyboard = new BrowserKeyboard(this);
        Mouse = new BrowserMouse(this);
    }

    /// <summary>Viewport-level keyboard, for whatever has focus.</summary>
    public BrowserKeyboard Keyboard { get; }

    /// <summary>Direct pointer input from test code, independent of agent observations.</summary>
    public BrowserMouse Mouse { get; }

    /// <summary>Reloads the current document.</summary>
    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        Require("reload").ReloadAsync(Token(cancellationToken));

    /// <summary>Navigates the history back once.</summary>
    public Task BackAsync(CancellationToken cancellationToken = default) =>
        Require("back").BackAsync(Token(cancellationToken));

    /// <summary>Navigates the history forward once.</summary>
    public Task ForwardAsync(CancellationToken cancellationToken = default) =>
        Require("forward").ForwardAsync(Token(cancellationToken));

    /// <summary>Returns the current URL.</summary>
    public Task<string> UrlAsync(CancellationToken cancellationToken = default) =>
        Require("url").GetUrlAsync(Token(cancellationToken));

    /// <summary>Returns the current title. A page that does not answer within the action timeout fails with <c>OPERATION_TIMEOUT</c>.</summary>
    public Task<string> TitleAsync(CancellationToken cancellationToken = default) =>
        Require("title").GetTitleAsync(Token(cancellationToken));

    /// <summary>
    /// Waits until the current URL equals <paramref name="url"/>, resolved
    /// against the base URL. The default timeout is the assertion timeout.
    /// </summary>
    public Task WaitForURLAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var expected = Routes.Absolute(_baseUrl, url).AbsoluteUri;
        return WaitForURLAsync(current => string.Equals(current, expected, StringComparison.Ordinal), url, timeout, cancellationToken);
    }

    /// <summary>Waits until the current URL matches <paramref name="pattern"/>. The default timeout is the assertion timeout.</summary>
    public Task WaitForURLAsync(Regex pattern, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return WaitForURLAsync(pattern.IsMatch, "/" + pattern + "/", timeout, cancellationToken);
    }

    /// <summary>
    /// Evaluates trusted test code in the page: an expression, or a function
    /// source that is called. The result must be JSON-safe and is deserialized to
    /// <typeparamref name="T"/>. A throwing script fails with <c>EVALUATE_FAILED</c>, and a
    /// script that does not finish within the action timeout with <c>OPERATION_TIMEOUT</c>.
    /// </summary>
    public Task<T?> EvaluateAsync<T>(string expression, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync<T>(expression, null, hasArg: false, cancellationToken);

    /// <summary>Evaluates a function source in the page with one JSON-safe argument.</summary>
    public Task<T?> EvaluateAsync<T>(string expression, object? arg, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync<T>(expression, arg, hasArg: true, cancellationToken);

    /// <summary>
    /// Waits for a response whose complete URL matches the glob <paramref name="pattern"/>:
    /// <c>*</c> matches within one path segment, <c>**</c> crosses <c>/</c>, <c>?</c>
    /// matches one character, and <c>\</c> escapes the next one. The timeout bounds
    /// the match only, and defaults to the action timeout.
    /// </summary>
    public Task<WebResponse> WaitForResponseAsync(string pattern, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return WaitForResponseCoreAsync(Routes.CompilePattern(pattern).IsMatch, timeout, cancellationToken);
    }

    /// <summary>Waits for a response whose URL matches <paramref name="pattern"/>. The timeout bounds the match only, and defaults to the action timeout.</summary>
    public Task<WebResponse> WaitForResponseAsync(Regex pattern, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return WaitForResponseCoreAsync(pattern.IsMatch, timeout, cancellationToken);
    }

    /// <summary>
    /// Adds a network route for the rest of the attempt, matching a complete URL with
    /// the glob <paramref name="pattern"/> that <see cref="WaitForResponseAsync(string, TimeSpan?, CancellationToken)"/>
    /// takes. The route registered last runs first. <paramref name="handler"/> must take
    /// exactly one decision on the <see cref="WebRoute"/>. A handler that takes none,
    /// takes two, or throws fails the next step with that error, and an undecided
    /// request is aborted.
    /// </summary>
    public Task RouteAsync(string pattern, Func<WebRoute, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return RouteCoreAsync(pattern, handler, cancellationToken);
    }

    /// <summary>Adds a network route for the URLs <paramref name="pattern"/> matches. See <see cref="RouteAsync(string, Func{WebRoute, Task}, CancellationToken)"/>.</summary>
    public Task RouteAsync(Regex pattern, Func<WebRoute, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return RouteCoreAsync(pattern, handler, cancellationToken);
    }

    /// <summary>Removes every route added with the same glob.</summary>
    public Task UnrouteAsync(string pattern, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return UnrouteCoreAsync(pattern, cancellationToken);
    }

    /// <summary>Removes every route added with a regex of the same source and options.</summary>
    public Task UnrouteAsync(Regex pattern, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return UnrouteCoreAsync(pattern, cancellationToken);
    }

    /// <summary>Returns the cookies visible to the browser context.</summary>
    public Task<IReadOnlyList<BrowserCookie>> CookiesAsync(CancellationToken cancellationToken = default) =>
        Require("cookies").GetCookiesAsync(Token(cancellationToken));

    /// <summary>
    /// Sets cookies. Each target URL, or the origin a domain cookie is sent to, must pass the URL rule. A relative url resolves against the base URL.
    /// A cookie URL that does not parse, or uses a scheme other than <c>http:</c> or <c>https:</c> (<c>about:blank</c> included),
    /// is <c>POLICY_DENIED</c>.
    /// </summary>
    public Task SetCookiesAsync(IReadOnlyList<BrowserCookie> cookies, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        var session = Require("setCookies");
        var scheme = _baseUrl is not null && _baseUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var resolved = new List<BrowserCookie>(cookies.Count);
        foreach (var cookie in cookies)
        {
            ArgumentNullException.ThrowIfNull(cookie);
            if ((cookie.Url is null) == (cookie.Domain is null) || (cookie.Url is not null && cookie.Path is not null))
            {
                throw new TestException("INVALID_ARGUMENT", "Cookie \"" + cookie.Name + "\" needs a url, or a domain with an optional path, not both.");
            }

            // A url cookie is set on the URL the rule resolved, so a relative one
            // lands on the base URL the way OpenAsync would. The rule admits
            // about:blank for navigation, which holds no cookie.
            var target = Routes.Resolve(_baseUrl, cookie.Url ?? scheme + "://" + cookie.Domain!.TrimStart('.'));
            if (!target.StartsWith("http:", StringComparison.Ordinal) && !target.StartsWith("https:", StringComparison.Ordinal))
            {
                throw new TestException("POLICY_DENIED", "cookie URL must be http(s): " + target);
            }

            resolved.Add(cookie.Url is null ? cookie : cookie with { Url = target });
        }

        return session.SetCookiesAsync(resolved, Token(cancellationToken));
    }

    /// <summary>Sets the viewport size for the rest of the attempt.</summary>
    public Task SetViewportAsync(int width, int height, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return Require("setViewport").SetViewportAsync(width, height, Token(cancellationToken));
    }

    internal IBrowserSession Require(string operation)
    {
        return _session as IBrowserSession
            ?? throw new EngineException(
                "UNSUPPORTED_CAPABILITY",
                "browser." + operation + " needs a browser engine; the " + _platform + " engine does not provide one.");
    }

    internal CancellationToken Token(CancellationToken cancellationToken) =>
        cancellationToken == default ? _token() : cancellationToken;

    private async Task<T?> EvaluateCoreAsync<T>(string expression, object? arg, bool hasArg, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);
        var session = Require("evaluate");
        var result = await session.EvaluateAsync(expression, arg, hasArg, Token(cancellationToken)).ConfigureAwait(false);
        if (result is not JsonElement element || element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return default;
        }

        try
        {
            return element.Deserialize<T>(JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            throw new TestException("EVALUATE_FAILED", "evaluate result does not convert to " + typeof(T).Name + ": " + ex.Message, ex);
        }
    }

    private async Task<WebResponse> WaitForResponseCoreAsync(Func<string, bool> matches, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var session = Require("waitForResponse");
        var response = await session.WaitForResponseAsync(matches, timeout ?? _actionTimeout, Token(cancellationToken)).ConfigureAwait(false);
        return new WebResponse(response, _actionTimeout, _token);
    }

    private async Task RouteCoreAsync(object pattern, Func<WebRoute, Task> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var session = Require("route");
        async Task Decide(IBrowserRoute engineRoute)
        {
            var route = new WebRoute(engineRoute, url => Routes.Resolve(_baseUrl, url), file => System.IO.Path.GetFullPath(file, _projectRoot));
            await handler(route).ConfigureAwait(false);
            if (!route.Decided)
            {
                throw new TestException("ACTION_FAILED", "route handler returned without calling fulfill, continue, fallback, or abort");
            }
        }

        await session.RouteAsync(Routes.Matcher(pattern), Decide, Token(cancellationToken)).ConfigureAwait(false);
        _routes.Add((pattern, Decide));
    }

    private async Task UnrouteCoreAsync(object pattern, CancellationToken cancellationToken)
    {
        var session = Require("unroute");
        for (var i = _routes.Count - 1; i >= 0; i--)
        {
            if (Routes.PatternsEqual(_routes[i].Pattern, pattern))
            {
                await session.UnrouteAsync(_routes[i].Handler, Token(cancellationToken)).ConfigureAwait(false);
                _routes.RemoveAt(i);
            }
        }
    }

    private async Task WaitForURLAsync(Func<string, bool> matches, string label, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var session = Require("waitForURL");
        var token = Token(cancellationToken);
        var budget = timeout ?? _assertionTimeout;
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var current = await session.GetUrlAsync(token).ConfigureAwait(false);
            if (matches(current))
            {
                return;
            }

            if (clock.Elapsed >= budget)
            {
                throw new TestException("ASSERTION_FAILED", "browser.waitForURL failed\nexpected: URL " + label + "\nobserved: URL " + current);
            }

            await Task.Delay(PollInterval, token).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// One request a <see cref="Browser.RouteAsync(string, Func{WebRoute, Task}, CancellationToken)"/>
/// handler intercepted. Take exactly one decision. Each validates its options before
/// it decides, so a rejected option leaves the request undecided, and the request is aborted.
/// </summary>
public sealed class WebRoute
{
    private readonly IBrowserRoute _route;
    private readonly Func<string, string> _resolveUrl;
    private readonly Func<string, string> _resolvePath;

    internal WebRoute(IBrowserRoute route, Func<string, string> resolveUrl, Func<string, string> resolvePath)
    {
        _route = route;
        _resolveUrl = resolveUrl;
        _resolvePath = resolvePath;
    }

    /// <summary>The intercepted request.</summary>
    public WebRouteRequest Request => _route.Request;

    internal bool Decided { get; private set; }

    /// <summary>Answers the request.</summary>
    public async Task FulfillAsync(RouteFulfillResponse response)
    {
        var decision = RouteOptions.ParseFulfill(response, _resolvePath);
        Decide("fulfill");
        await _route.FulfillAsync(decision).ConfigureAwait(false);
    }

    /// <summary>Sends the request to the network once, skipping every other route.</summary>
    public async Task ContinueAsync(RouteContinueOverrides? overrides = null)
    {
        var decision = RouteOptions.ParseContinue(overrides, _route.Request.Url, _resolveUrl);
        Decide("continue");
        await _route.ContinueAsync(decision).ConfigureAwait(false);
    }

    /// <summary>Hands the request to the route registered before this one, or the network when none matches.</summary>
    public async Task FallbackAsync()
    {
        Decide("fallback");
        await _route.FallbackAsync().ConfigureAwait(false);
    }

    /// <summary>Aborts the request.</summary>
    public async Task AbortAsync()
    {
        Decide("abort");
        await _route.AbortAsync().ConfigureAwait(false);
    }

    private void Decide(string name)
    {
        if (Decided)
        {
            throw new TestException("ACTION_FAILED", "route handler already decided; " + name + " called twice");
        }

        Decided = true;
    }
}

/// <summary>A response <see cref="Browser.WaitForResponseAsync(string, TimeSpan?, CancellationToken)"/> matched.</summary>
public sealed class WebResponse
{
    private readonly Task<string> _body;
    private readonly TimeSpan _actionTimeout;
    private readonly Func<CancellationToken> _token;

    internal WebResponse(BrowserResponse response, TimeSpan actionTimeout, Func<CancellationToken> token)
    {
        Url = response.Url;
        Status = response.Status;
        Headers = response.Headers;
        _body = response.Body;
        _actionTimeout = actionTimeout;
        _token = token;
    }

    /// <summary>Response URL.</summary>
    public string Url { get; }

    /// <summary>HTTP status.</summary>
    public int Status { get; }

    /// <summary>Response headers, with lower-cased names.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Waits for the body, up to the action timeout, and reads it as text. Fails with
    /// <c>ACTION_FAILED</c> when the body could not be read or did not finish in time.
    /// </summary>
    public async Task<string> TextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _body.WaitAsync(_actionTimeout, cancellationToken == default ? _token() : cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException) when (!_body.IsCompleted)
        {
            var budget = ((long)_actionTimeout.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            throw new TestException("ACTION_FAILED", "waitForResponse: response body did not finish within " + budget + "ms");
        }
    }

    /// <summary>Waits for the body as <see cref="TextAsync"/> does and deserializes it as JSON.</summary>
    public async Task<T?> JsonAsync<T>(CancellationToken cancellationToken = default) =>
        JsonSerializer.Deserialize<T>(await TextAsync(cancellationToken).ConfigureAwait(false), JsonDefaults.Options);
}

/// <summary>
/// Keyboard input for whatever holds focus in the active tab. A call the page does not
/// answer within the action timeout fails with <c>ACTION_MAY_HAVE_COMMITTED</c>: the input may have reached the page.
/// </summary>
public sealed class BrowserKeyboard
{
    private readonly Browser _browser;

    internal BrowserKeyboard(Browser browser)
    {
        _browser = browser;
    }

    /// <summary>Sends one key, such as <c>Enter</c> or <c>Control+A</c>.</summary>
    public Task PressAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _browser.Require("keyboard.press").PressAsync(key, _browser.Token(cancellationToken));
    }

    /// <summary>Types plain text.</summary>
    public Task TypeAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _browser.Require("keyboard.type").KeyboardTypeAsync(text, _browser.Token(cancellationToken));
    }
}

/// <summary>
/// Pointer input for the active tab, in CSS pixels. A call the page does not answer
/// within the action timeout fails with <c>ACTION_MAY_HAVE_COMMITTED</c>: the input may have reached the page.
/// </summary>
public sealed class BrowserMouse
{
    private readonly Browser _browser;

    internal BrowserMouse(Browser browser)
    {
        _browser = browser;
    }

    public Task MoveAsync(float x, float y, CancellationToken cancellationToken = default) =>
        _browser.Require("mouse.move").MouseMoveAsync(x, y, _browser.Token(cancellationToken));

    public Task WheelAsync(float deltaX, float deltaY, CancellationToken cancellationToken = default) =>
        _browser.Require("mouse.wheel").MouseWheelAsync(deltaX, deltaY, _browser.Token(cancellationToken));

    /// <summary>Presses the primary button.</summary>
    public Task DownAsync(CancellationToken cancellationToken = default) =>
        _browser.Require("mouse.down").MouseDownAsync(_browser.Token(cancellationToken));

    /// <summary>Releases the primary button.</summary>
    public Task UpAsync(CancellationToken cancellationToken = default) =>
        _browser.Require("mouse.up").MouseUpAsync(_browser.Token(cancellationToken));
}
