// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace E2E.Engine;

/// <summary>
/// The browser-shaped surface an engine session can add on top of
/// <see cref="IEngineSession"/>. The <see cref="Browser"/> fixture uses it, and
/// fails with <c>UNSUPPORTED_CAPABILITY</c> when the session does not implement it.
/// The harness has already validated every argument.
/// </summary>
public interface IBrowserSession : IEngineSession
{
    Task ReloadAsync(CancellationToken cancellationToken);

    Task ForwardAsync(CancellationToken cancellationToken);

    Task<string> GetUrlAsync(CancellationToken cancellationToken);

    Task<string> GetTitleAsync(CancellationToken cancellationToken);

    /// <summary>Evaluates trusted test code in the page. <paramref name="hasArg"/> says whether <paramref name="arg"/> is passed.</summary>
    Task<JsonElement?> EvaluateAsync(string expression, object? arg, bool hasArg, CancellationToken cancellationToken);

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for a response whose URL <paramref name="matches"/>
    /// accepts. The timeout bounds the match only: the body read starts once the
    /// response is known and goes on in <see cref="BrowserResponse.Body"/>. A session
    /// that does not override it fails with <c>UNSUPPORTED_CAPABILITY</c>.
    /// </summary>
    Task<BrowserResponse> WaitForResponseAsync(Func<string, bool> matches, TimeSpan timeout, CancellationToken cancellationToken) =>
        Task.FromException<BrowserResponse>(new EngineException("UNSUPPORTED_CAPABILITY", "browser.waitForResponse is not supported by this engine."));

    Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken);

    Task SetCookiesAsync(IReadOnlyList<BrowserCookie> cookies, CancellationToken cancellationToken);

    /// <summary>Sizes the open page, and every page the session opens later in the attempt.</summary>
    Task SetViewportAsync(int width, int height, CancellationToken cancellationToken);

    Task KeyboardTypeAsync(string text, CancellationToken cancellationToken);

    Task MouseMoveAsync(float x, float y, CancellationToken cancellationToken);

    Task MouseWheelAsync(float deltaX, float deltaY, CancellationToken cancellationToken);

    Task MouseDownAsync(CancellationToken cancellationToken);

    Task MouseUpAsync(CancellationToken cancellationToken);
}

/// <summary>
/// A browser cookie. When setting one, give either <see cref="Url"/>, or
/// <see cref="Domain"/> with an optional <see cref="Path"/>, never both.
/// </summary>
public sealed record BrowserCookie
{
    public required string Name { get; init; }

    public required string Value { get; init; }

    public string? Url { get; init; }

    public string? Domain { get; init; }

    public string? Path { get; init; }

    /// <summary>Unix timestamp in whole seconds. Null is a session cookie.</summary>
    public long? Expires { get; init; }

    public bool? HttpOnly { get; init; }

    public bool? Secure { get; init; }

    /// <summary><c>Strict</c>, <c>Lax</c>, or <c>None</c>.</summary>
    public string? SameSite { get; init; }
}

/// <summary>A response an engine observed, with its body still being read.</summary>
public sealed record BrowserResponse
{
    public required string Url { get; init; }

    public required int Status { get; init; }

    /// <summary>Response headers, with lower-cased names.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    /// <summary>The body as text. Faults with <c>ACTION_FAILED</c> when the browser could not read it.</summary>
    public required Task<string> Body { get; init; }
}
