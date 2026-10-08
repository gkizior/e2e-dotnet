// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using E2E.OAuth;

namespace E2E.Tests.Fetch;

/// <summary>An in-memory store: the tests' way to hand a login to a handler without a file.</summary>
internal sealed class MemoryCredentialStore(Dictionary<string, OAuthCredentials>? initial = null) : ICredentialStore
{
    private readonly ConcurrentDictionary<string, OAuthCredentials> _entries = new(initial ?? [], StringComparer.Ordinal);

    public Task<OAuthCredentials?> GetAsync(string providerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.TryGetValue(providerId, out var credentials) ? credentials : null);

    public Task SetAsync(string providerId, OAuthCredentials credentials, CancellationToken cancellationToken = default)
    {
        _entries[providerId] = credentials;
        return Task.CompletedTask;
    }

    public Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_entries.TryRemove(providerId, out _));

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _entries.Keys]);
}

/// <summary>Refreshes rotate the refresh token and reject a token that was already rotated, like SpaceXAI and ChatGPT do.</summary>
internal sealed class RotatingProvider : IOAuthProvider
{
    public List<string> Refreshes { get; } = [];

    public string Id => "test";

    public string Name => "Test";

    public Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken) =>
        Task.FromResult(new OAuthCredentials { Access = "x", Refresh = "y" });

    public async Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        int n;
        lock (Refreshes)
        {
            if (Refreshes.Contains(credentials.Refresh))
            {
                throw new OAuthException(OAuthException.LoginRequired, "refresh token " + credentials.Refresh + " was already used");
            }

            Refreshes.Add(credentials.Refresh);
            n = Refreshes.Count;
        }

        await Task.Delay(20, cancellationToken);
        return new OAuthCredentials { Access = "fresh-" + n, Refresh = "rt-" + n, Expires = Later() };
    }

    public Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionModel>>([]);

    public static long Later() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;

    public static long Soon() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000;
}

/// <summary>A fake API: records each request it receives and answers through <paramref name="answer"/>.</summary>
internal sealed class FakeApi(Func<FakeApi.Received, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
{
    public ConcurrentQueue<Received> Requests { get; } = new();

    /// <summary>The answer this API gives a request, without recording it.</summary>
    public (HttpStatusCode Status, string Body) Respond(Received request) => answer(request);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var received = new Received(
            request.RequestUri!,
            request.Headers.NonValidated.ToDictionary(header => header.Key.ToLowerInvariant(), header => header.Value.ToString(), StringComparer.Ordinal),
            request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        Requests.Enqueue(received);
        var (status, body) = answer(received);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }

    public sealed record Received(Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }
}

/// <summary>An unsigned JWT around a JSON payload, as upstream's <c>fakeJwt</c> builds one.</summary>
internal static class FakeJwt
{
    public static string Of(string payload) => Part("""{"alg":"none"}""") + "." + Part(payload) + ".sig";

    private static string Part(string json) =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
