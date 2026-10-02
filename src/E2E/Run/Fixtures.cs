// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>Opens the app under test. A relative URL is resolved against the target base URL.</summary>
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

    public Task OpenAsync(string url = "/", CancellationToken cancellationToken = default)
    {
        var token = cancellationToken == default ? _token() : cancellationToken;
        return _session.OpenAsync(Routes.Resolve(_baseUrl, url), token);
    }
}

/// <summary>The fixtures for one test. Call <see cref="Skip"/> before the first step when a precondition is missing.</summary>
public sealed class TestContext
{
    public required App App { get; init; }

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
