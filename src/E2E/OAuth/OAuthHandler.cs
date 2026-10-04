// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;

namespace E2E.OAuth;

/// <summary>
/// The handler a subscription model's <see cref="HttpClient"/> sends through. Per request it reads the stored
/// credentials, refreshes them ahead of expiry, replaces any API key header with the bearer token, and lets the
/// provider adjust the request. On a 401 it refreshes once and retries, or, with nothing to refresh, reports that
/// the user signs in again. One refresh serves every model and every concurrent call over the same store,
/// because vendors that rotate refresh tokens reject the second concurrent refresh.
/// </summary>
public sealed class OAuthHandler : DelegatingHandler
{
    /// <summary>Refresh this long before expiry, so a call never starts on a token about to lapse.</summary>
    private const long RefreshSkewMs = 120_000;

    private static readonly TimeSpan RotationWait = TimeSpan.FromMilliseconds(500);
    private const int RotationAttempts = 4;

    private static readonly string[] KeyHeaders = ["x-api-key", "x-goog-api-key", "api-key"];

    private static readonly ConcurrentDictionary<string, SharedState> ByPath = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<ICredentialStore, SharedState> ByStore = new();

    private readonly IOAuthProvider _provider;
    private readonly ICredentialStore _store;
    private readonly string _remedy;
    private readonly SharedState _shared;

    public OAuthHandler(IOAuthProvider provider, ICredentialStore store, string? loginHint = null, HttpMessageHandler? inner = null)
        : base(inner ?? new HttpClientHandler())
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(store);
        _provider = provider;
        _store = store;
        _remedy = loginHint ?? "run `e2e login " + provider.Id + "`";
        _shared = store is FileCredentialStore file ? ByPath.GetOrAdd(file.FilePath, _ => new SharedState()) : ByStore.GetValue(store, _ => new SharedState());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Captured once so the request can be sent again after a refresh.
        var body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var contentHeaders = request.Content?.Headers.ToList();

        var credentials = await CurrentAsync(cancellationToken).ConfigureAwait(false);
        if (Expiring(credentials))
        {
            credentials = await RefreshAsync(credentials, cancellationToken).ConfigureAwait(false);
        }

        var response = await AttemptAsync(request, body, contentHeaders, credentials, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return response;
        }

        // Nothing to refresh (Copilot's GitHub token) means the token is revoked for good.
        if (credentials.Refresh.Length == 0)
        {
            var detail = await TokenEndpoint.DescribeAsync(response, cancellationToken).ConfigureAwait(false);
            response.Dispose();
            throw new OAuthException(OAuthException.LoginRequired, _provider.Name + " rejected the stored token (" + detail + "); " + _remedy);
        }

        response.Dispose();
        credentials = await RefreshAsync(credentials, cancellationToken).ConfigureAwait(false);
        return await AttemptAsync(request, body, contentHeaders, credentials, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> AttemptAsync(
        HttpRequestMessage original,
        byte[]? body,
        List<KeyValuePair<string, IEnumerable<string>>>? contentHeaders,
        OAuthCredentials credentials,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(original.Method, original.RequestUri) { Version = original.Version };
        foreach (var header in original.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            foreach (var header in contentHeaders ?? [])
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var name in KeyHeaders)
        {
            request.Headers.Remove(name);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Access);
        request.Headers.Remove("User-Agent");
        request.Headers.TryAddWithoutValidation("User-Agent", Internal.ModelHttp.UserAgent);
        await _provider.PrepareAsync(request, credentials, cancellationToken).ConfigureAwait(false);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OAuthCredentials> CurrentAsync(CancellationToken cancellationToken)
    {
        var stored = await _store.GetAsync(_provider.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new OAuthException(OAuthException.NotLoggedIn, "no " + _provider.Name + " login is stored; " + _remedy);

        // What this process renewed wins over a source that could not keep it.
        return _shared.Renewed.TryGetValue(_provider.Id, out var renewed) ? renewed : stored;
    }

    private Task<OAuthCredentials> RefreshAsync(OAuthCredentials stale, CancellationToken cancellationToken)
    {
        lock (_shared)
        {
            if (_shared.Refreshing.TryGetValue(_provider.Id, out var pending))
            {
                return pending.WaitAsync(cancellationToken);
            }

            // Not tied to one caller's token: a caller that gives up must not fail the others waiting on it.
            pending = RefreshOnceAsync(stale, CancellationToken.None);
            _shared.Refreshing[_provider.Id] = pending;
            _ = pending.ContinueWith(
                _ =>
                {
                    lock (_shared)
                    {
                        _shared.Refreshing.Remove(_provider.Id);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return pending.WaitAsync(cancellationToken);
        }
    }

    private async Task<OAuthCredentials> RefreshOnceAsync(OAuthCredentials stale, CancellationToken cancellationToken)
    {
        // Another process may have refreshed already: prefer what the store holds now.
        var latest = await CurrentAsync(cancellationToken).ConfigureAwait(false);
        if (latest.Access != stale.Access && !Expiring(latest))
        {
            return latest;
        }

        try
        {
            var renewed = await _provider.RefreshAsync(latest, cancellationToken).ConfigureAwait(false);
            await PersistAsync(renewed, cancellationToken).ConfigureAwait(false);
            return renewed;
        }
        catch (OAuthException ex) when (ex.Code == OAuthException.LoginRequired)
        {
            // A rotated refresh token may have been stored by another process meanwhile.
            for (var attempt = 0; attempt < RotationAttempts; attempt++)
            {
                var current = await CurrentAsync(cancellationToken).ConfigureAwait(false);
                if (current.Refresh != latest.Refresh)
                {
                    return current;
                }

                await Task.Delay(RotationWait, cancellationToken).ConfigureAwait(false);
            }

            throw new OAuthException(OAuthException.LoginRequired, ex.Message + "; " + _remedy, ex);
        }
    }

    private async Task PersistAsync(OAuthCredentials renewed, CancellationToken cancellationToken)
    {
        try
        {
            await _store.SetAsync(_provider.Id, renewed, cancellationToken).ConfigureAwait(false);
            _shared.Renewed.TryRemove(_provider.Id, out _);
        }
        catch (OAuthException ex) when (ex.Code == OAuthException.Misconfigured)
        {
            // A read-only store (the environment) keeps the renewal for this process.
            _shared.Renewed[_provider.Id] = renewed;
        }
    }

    private static bool Expiring(OAuthCredentials credentials)
    {
        return credentials.Expires != 0 && credentials.Expires - RefreshSkewMs <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private sealed class SharedState
    {
        public Dictionary<string, Task<OAuthCredentials>> Refreshing { get; } = new(StringComparer.Ordinal);

        public ConcurrentDictionary<string, OAuthCredentials> Renewed { get; } = new(StringComparer.Ordinal);
    }
}
