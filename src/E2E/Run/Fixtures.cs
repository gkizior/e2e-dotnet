// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Opens and resets the app under test. A relative URL is resolved against
/// <see cref="BaseUrl"/>; no URL opens the base URL.
/// </summary>
public sealed class App
{
    private readonly IEngineSession _session;
    private readonly string? _baseUrl;
    private readonly Func<CancellationToken> _token;

    internal App(IEngineSession session, string? baseUrl, Func<CancellationToken> token)
    {
        _session = session;
        _baseUrl = baseUrl;
        _token = token;
    }

    /// <summary>The target base URL relative URLs resolve against, or null when the test declares none.</summary>
    public string? BaseUrl => _baseUrl;

    /// <summary>
    /// Opens a path or URL, resolved against the base URL. No URL opens the base URL itself.
    /// Throws <c>APP_URL_REQUIRED</c> for a relative URL when the test declares no base URL, and <c>POLICY_DENIED</c>
    /// for a malformed URL or a scheme other than <c>http:</c> or <c>https:</c> (the exact <c>about:blank</c> is admitted).
    /// </summary>
    public Task OpenAsync(string? url = null, CancellationToken cancellationToken = default)
    {
        return _session.OpenAsync(Routes.Resolve(_baseUrl, url), Token(cancellationToken));
    }

    /// <summary>Goes back one entry in the history. With no earlier entry it does nothing.</summary>
    public Task BackAsync(CancellationToken cancellationToken = default)
    {
        return _session.BackAsync(Token(cancellationToken));
    }

    /// <summary>Closes the current document and reopens the app at <see cref="BaseUrl"/>. Cookies and storage are kept.</summary>
    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        return SteerAsync(_session.RestartAsync, Token(cancellationToken));
    }

    /// <summary>Discards cookies, storage, and history, and reopens the app at <see cref="BaseUrl"/>.</summary>
    public Task ClearStateAsync(CancellationToken cancellationToken = default)
    {
        return SteerAsync(_session.ClearStateAsync, Token(cancellationToken));
    }

    // A steering hook ends at a blank surface, so the app is reopened at its base URL
    // through the same path as OpenAsync. Without a base URL there is nothing to reopen.
    private async Task SteerAsync(Func<CancellationToken, Task> hook, CancellationToken cancellationToken)
    {
        await hook(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(_baseUrl))
        {
            await OpenAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private CancellationToken Token(CancellationToken cancellationToken) =>
        cancellationToken == default ? _token() : cancellationToken;
}

/// <summary>The fixtures for one test. Call <see cref="Skip"/> before the first step when a precondition is missing.</summary>
public sealed class TestContext
{
    public required App App { get; init; }

    /// <summary>The browser fixture. Its members fail with <c>UNSUPPORTED_CAPABILITY</c> on an engine without a browser.</summary>
    public required Browser Browser { get; init; }

    /// <summary>The engine platform, such as <c>web</c> or <c>document</c>.</summary>
    public required string Platform { get; init; }

    public required Agent Agent { get; init; }

    public required Screen Screen { get; init; }

    public CancellationToken CancellationToken { get; init; }

    public void Skip(bool condition, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (condition)
        {
            throw new SkipException(reason);
        }
    }
}
