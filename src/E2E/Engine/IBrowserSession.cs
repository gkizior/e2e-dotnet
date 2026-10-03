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
