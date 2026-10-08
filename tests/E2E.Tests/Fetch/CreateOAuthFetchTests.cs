// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using E2E.OAuth;

namespace E2E.Tests.Fetch;

/// <summary>
/// Upstream's <c>createOAuthFetch</c> is the port's <see cref="OAuthHandler"/>. "lets the provider send the request its
/// own way" is not ported: a port provider prepares a request but cannot send it itself.
/// </summary>
public sealed class CreateOAuthFetchTests
{
    private const string Url = "https://api.test/";

    [Fact]
    public async Task Swaps_the_SDK_key_header_for_the_bearer_token_and_names_the_product()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, """{ "ok": true }"""));
        var store = new MemoryCredentialStore(new() { ["test"] = new OAuthCredentials { Access = "tok", Refresh = "r", Expires = 0 } });
        using var http = Client(new RotatingProvider(), store, api);
        using var request = new HttpRequestMessage(HttpMethod.Post, Url + "v1/thing") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("x-api-key", "oauth");
        request.Headers.TryAddWithoutValidation("authorization", "Bearer oauth");

        using var response = await http.SendAsync(request);

        Assert.Equal("""{ "ok": true }""", await response.Content.ReadAsStringAsync());
        var sent = Assert.Single(api.Requests);
        Assert.Equal("Bearer tok", sent.Header("authorization"));
        Assert.Null(sent.Header("x-api-key"));
        Assert.Equal(E2E.Internal.ModelHttp.UserAgent, sent.Header("user-agent"));
        Assert.Equal("{}", sent.Body);
    }

    [Fact]
    public async Task Fails_with_NOT_LOGGED_IN_and_the_hint_when_nothing_is_stored()
    {
        using var http = Client(new RotatingProvider(), new MemoryCredentialStore(), new FakeApi(_ => (HttpStatusCode.OK, "{}")), "run e2e login test");
        var error = await Assert.ThrowsAsync<OAuthException>(() => http.GetAsync(new Uri(Url)));
        Assert.Equal(OAuthException.NotLoggedIn, error.Code);
        Assert.Contains("run e2e login test", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refreshes_an_expiring_token_once_across_model_instances_that_share_a_store()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, "{}"));
        var store = new MemoryCredentialStore(new() { ["test"] = new OAuthCredentials { Access = "stale", Refresh = "rt-0", Expires = RotatingProvider.Soon() } });
        var provider = new RotatingProvider();
        using var first = Client(provider, store, api);
        using var second = Client(provider, store, api);

        await Task.WhenAll(first.GetAsync(new Uri(Url)), second.GetAsync(new Uri(Url)), first.GetAsync(new Uri(Url)));

        Assert.Equal(["rt-0"], provider.Refreshes);
        Assert.Equal(["Bearer fresh-1", "Bearer fresh-1", "Bearer fresh-1"], api.Requests.Select(request => request.Header("authorization")));
        var stored = await store.GetAsync("test");
        Assert.Equal(("fresh-1", "rt-1"), (stored!.Access, stored.Refresh));
    }

    [Fact]
    public async Task Shares_one_refresh_between_separate_file_stores_over_the_same_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "e2e-oauth-fetch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var file = Path.Combine(directory, "oauth.json");
            await new FileCredentialStore(file).SetAsync("test", new OAuthCredentials { Access = "stale", Refresh = "rt-0", Expires = RotatingProvider.Soon() });
            var api = new FakeApi(_ => (HttpStatusCode.OK, "{}"));
            var provider = new RotatingProvider();
            using var first = Client(provider, new FileCredentialStore(file), api);
            using var second = Client(provider, new FileCredentialStore(file), api);

            await Task.WhenAll(first.GetAsync(new Uri(Url)), second.GetAsync(new Uri(Url)));

            Assert.Equal(["rt-0"], provider.Refreshes);
            Assert.Equal(["Bearer fresh-1", "Bearer fresh-1"], api.Requests.Select(request => request.Header("authorization")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Keeps_credentials_it_renewed_for_a_source_that_cannot_store_them()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, "{}"));
        var expires = RotatingProvider.Soon().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var store = new EnvironmentCredentialStore("""{ "test": { "access": "stale", "refresh": "rt-0", "expires": """ + expires + " } }");
        var provider = new RotatingProvider();
        using var http = Client(provider, store, api);

        await http.GetAsync(new Uri(Url));
        await http.GetAsync(new Uri(Url));

        Assert.Equal(["rt-0"], provider.Refreshes);
        Assert.Equal(["Bearer fresh-1", "Bearer fresh-1"], api.Requests.Select(request => request.Header("authorization")));
    }

    [Fact]
    public async Task Fails_with_LOGIN_REQUIRED_and_the_hint_when_the_refresh_token_is_rejected_for_good()
    {
        var store = new MemoryCredentialStore(new() { ["test"] = new OAuthCredentials { Access = "stale", Refresh = "rt-0", Expires = RotatingProvider.Soon() } });
        var provider = new RotatingProvider();
        provider.Refreshes.Add("rt-0");
        using var http = Client(provider, store, new FakeApi(_ => (HttpStatusCode.OK, "{}")), "run `npx e2e login test`");

        var error = await Assert.ThrowsAsync<OAuthException>(() => http.GetAsync(new Uri(Url)));

        Assert.Equal(OAuthException.LoginRequired, error.Code);
        Assert.Equal("refresh token rt-0 was already used; run `npx e2e login test`", error.Message);
    }

    [Fact]
    public async Task Uses_the_credentials_another_process_stored_when_its_own_refresh_token_was_already_rotated()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, "{}"));
        var store = new MemoryCredentialStore(new() { ["test"] = new OAuthCredentials { Access = "stale", Refresh = "rt-0", Expires = RotatingProvider.Soon() } });
        var provider = new RotatingProvider();
        provider.Refreshes.Add("rt-0");
        using var http = Client(provider, store, api);

        // The other process rotated the token and stores it a moment after this one was rejected.
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            await store.SetAsync("test", new OAuthCredentials { Access = "theirs", Refresh = "rt-1", Expires = RotatingProvider.Later() });
        });
        await http.GetAsync(new Uri(Url));

        Assert.Equal("Bearer theirs", api.Requests.First().Header("authorization"));
    }

    [Fact]
    public async Task Retries_once_with_a_refreshed_token_after_a_401_also_for_a_Request_input()
    {
        var api = new FakeApi(request => request.Header("authorization") == "Bearer stale"
            ? (HttpStatusCode.Unauthorized, """{ "error": "expired" }""")
            : (HttpStatusCode.OK, "{\"echo\":\"" + request.Body + "\"}"));
        var store = new MemoryCredentialStore(new() { ["test"] = new OAuthCredentials { Access = "stale", Refresh = "rt-0", Expires = 0 } });
        using var http = Client(new RotatingProvider(), store, api);

        using var response = await http.PostAsync(new Uri(Url), new StringContent("payload"));

        Assert.Equal("""{"echo":"payload"}""", await response.Content.ReadAsStringAsync());
        Assert.Equal(2, api.Requests.Count);
        Assert.Equal("payload", api.Requests.Last().Body);
    }

    [Fact]
    public async Task Fails_with_LOGIN_REQUIRED_and_the_hint_on_a_401_for_a_token_that_cannot_be_refreshed_without_a_retry()
    {
        var api = new FakeApi(_ => (HttpStatusCode.Unauthorized, """{ "message": "Bad credentials" }"""));
        var store = new MemoryCredentialStore(new() { ["test"] = new OAuthCredentials { Access = "gh", Refresh = "", Expires = 0 } });
        using var http = Client(new RotatingProvider(), store, api, "run `npx e2e login test`");

        var error = await Assert.ThrowsAsync<OAuthException>(() => http.GetAsync(new Uri(Url)));

        Assert.Equal(OAuthException.LoginRequired, error.Code);
        Assert.Equal("Test rejected the stored token (401: Bad credentials); run `npx e2e login test`", error.Message);
        Assert.Single(api.Requests);
    }

    private static HttpClient Client(IOAuthProvider provider, ICredentialStore store, HttpMessageHandler api, string? loginHint = null) =>
        new(new OAuthHandler(provider, store, loginHint, api));
}
