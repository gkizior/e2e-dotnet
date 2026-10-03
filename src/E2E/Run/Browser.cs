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
    private readonly TimeSpan _assertionTimeout;
    private readonly Func<CancellationToken> _token;

    internal Browser(IEngineSession session, string platform, string? baseUrl, TimeSpan assertionTimeout, Func<CancellationToken> token)
    {
        _session = session;
        _platform = platform;
        _baseUrl = baseUrl;
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

    /// <summary>Returns the current title.</summary>
    public Task<string> TitleAsync(CancellationToken cancellationToken = default) =>
        Require("title").GetTitleAsync(Token(cancellationToken));

    /// <summary>
    /// Waits until the current URL equals <paramref name="url"/>, resolved
    /// against the base URL. The default timeout is the assertion timeout.
    /// </summary>
    public Task WaitForURLAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var expected = Routes.Resolve(_baseUrl, url);
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
    /// <typeparamref name="T"/>. A throwing script fails with <c>EVALUATE_FAILED</c>.
    /// </summary>
    public Task<T?> EvaluateAsync<T>(string expression, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync<T>(expression, null, hasArg: false, cancellationToken);

    /// <summary>Evaluates a function source in the page with one JSON-safe argument.</summary>
    public Task<T?> EvaluateAsync<T>(string expression, object? arg, CancellationToken cancellationToken = default) =>
        EvaluateCoreAsync<T>(expression, arg, hasArg: true, cancellationToken);

    /// <summary>Returns the cookies visible to the browser context.</summary>
    public Task<IReadOnlyList<BrowserCookie>> CookiesAsync(CancellationToken cancellationToken = default) =>
        Require("cookies").GetCookiesAsync(Token(cancellationToken));

    /// <summary>Sets cookies. Each target URL, or the origin a domain cookie is sent to, must pass the URL rule.</summary>
    public Task SetCookiesAsync(IReadOnlyList<BrowserCookie> cookies, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cookies);
        var session = Require("setCookies");
        var scheme = _baseUrl is not null && _baseUrl.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        foreach (var cookie in cookies)
        {
            ArgumentNullException.ThrowIfNull(cookie);
            if ((cookie.Url is null) == (cookie.Domain is null) || (cookie.Url is not null && cookie.Path is not null))
            {
                throw new TestException("INVALID_ARGUMENT", "Cookie \"" + cookie.Name + "\" needs a url, or a domain with an optional path, not both.");
            }

            Routes.Resolve(_baseUrl, cookie.Url ?? scheme + "://" + cookie.Domain!.TrimStart('.'));
        }

        return session.SetCookiesAsync(cookies, Token(cancellationToken));
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

/// <summary>Keyboard input for whatever holds focus in the active tab.</summary>
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

/// <summary>Pointer input for the active tab, in CSS pixels.</summary>
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
