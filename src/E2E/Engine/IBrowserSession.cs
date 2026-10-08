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

    /// <summary>
    /// Adds a network route for the rest of the attempt, every context it opens
    /// included. The route registered last runs first. When <paramref name="handler"/>
    /// throws, the session keeps the error, fails its next operation with it, and
    /// aborts the request if the handler took no decision. A session that does not
    /// override it fails with <c>UNSUPPORTED_CAPABILITY</c>.
    /// </summary>
    Task RouteAsync(Func<string, bool> matches, Func<IBrowserRoute, Task> handler, CancellationToken cancellationToken) =>
        Task.FromException(new EngineException("UNSUPPORTED_CAPABILITY", "browser.route is not supported by this engine."));

    /// <summary>Removes the route <see cref="RouteAsync"/> added with <paramref name="handler"/>. A session that does not override it fails with <c>UNSUPPORTED_CAPABILITY</c>.</summary>
    Task UnrouteAsync(Func<IBrowserRoute, Task> handler, CancellationToken cancellationToken) =>
        Task.FromException(new EngineException("UNSUPPORTED_CAPABILITY", "browser.unroute is not supported by this engine."));

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

/// <summary>
/// One intercepted request, as an engine hands it to a route handler. The harness
/// has already validated each decision and takes at most one.
/// </summary>
public interface IBrowserRoute
{
    WebRouteRequest Request { get; }

    /// <summary>Answers the request. <see cref="RouteFulfillResponse.Status"/> is set, and <see cref="RouteFulfillResponse.Json"/> has become a <see cref="RouteFulfillResponse.Body"/>.</summary>
    Task FulfillAsync(RouteFulfillResponse response);

    /// <summary>Sends the request to the network, skipping every other route. The configured site headers are added on top of the request headers.</summary>
    Task ContinueAsync(RouteContinueOverrides overrides);

    /// <summary>Hands the request to the route registered before this one, or the network when none matches.</summary>
    Task FallbackAsync();

    Task AbortAsync();
}

/// <summary>The request a route intercepted.</summary>
public sealed record WebRouteRequest
{
    public required string Url { get; init; }

    public required string Method { get; init; }

    /// <summary>Request headers, with lower-cased names.</summary>
    public required IReadOnlyDictionary<string, string> Headers { get; init; }

    public string? PostData { get; init; }
}

/// <summary>
/// How <see cref="E2E.WebRoute.FulfillAsync"/> answers a request. Give at most one of
/// <see cref="Json"/>, <see cref="Body"/>, or <see cref="Path"/>; none is an empty body.
/// </summary>
public sealed record RouteFulfillResponse
{
    /// <summary>HTTP status from 100 to 599. Defaults to 200.</summary>
    public int? Status { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Sets the <c>Content-Type</c> response header.</summary>
    public string? ContentType { get; init; }

    /// <summary>A JSON-safe value, sent serialized as <c>application/json</c> unless <see cref="ContentType"/> says otherwise.</summary>
    public object? Json { get; init; }

    public string? Body { get; init; }

    /// <summary>A file relative to the project root. Its <c>Content-Type</c> follows its extension unless <see cref="ContentType"/> or a <c>content-type</c> header sets one.</summary>
    public string? Path { get; init; }
}

/// <summary>What <see cref="E2E.WebRoute.ContinueAsync"/> changes about the request before it goes to the network.</summary>
public sealed record RouteContinueOverrides
{
    /// <summary>Resolved against the base URL like <c>app.open</c>; must keep the request's scheme.</summary>
    public string? Url { get; init; }

    public string? Method { get; init; }

    /// <summary>Replaces every request header; the configured headers for the app's host are added on top.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public string? PostData { get; init; }
}
