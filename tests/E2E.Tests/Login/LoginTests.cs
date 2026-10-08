// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;
using E2E.Tests.Fetch;
using E2E.Tests.Store;

namespace E2E.Tests.Login;

[Collection(ProcessEnvironment.Name)]
public sealed class LoginTests
{
    private static readonly OAuthCredentials Credentials = new() { Access = "local-test", Refresh = "local-refresh", Expires = 0 };

    private static readonly OAuthLoginCallbacks Callbacks = new() { OnAuth = _ => { }, OnPrompt = (_, _) => Task.FromResult("") };

    [Fact]
    public async Task Refuses_an_explicit_environment_store_before_starting_provider_authentication()
    {
        var provider = new FakeProvider();
        var error = await Assert.ThrowsAsync<OAuthException>(() => OAuthProviders.LoginAsync(provider, Callbacks, null, new EnvironmentCredentialStore("{}"), CancellationToken.None));
        Assert.Equal(OAuthException.Misconfigured, error.Code);
        Assert.Equal(0, provider.Logins);
    }

    [Fact]
    public async Task Refuses_the_default_environment_store_before_starting_provider_authentication()
    {
        var provider = new FakeProvider();
        using var env = ProcessEnvironment.Set(CredentialStores.EnvironmentVariable, "{}");
        var error = await Assert.ThrowsAsync<OAuthException>(() => OAuthProviders.LoginAsync(provider, Callbacks, null, null, CancellationToken.None));
        Assert.Equal(OAuthException.Misconfigured, error.Code);
        Assert.Equal(0, provider.Logins);
    }

    [Fact]
    public async Task Still_authenticates_and_saves_to_a_supplied_writable_store_with_an_environment_override()
    {
        var provider = new FakeProvider();
        var store = new MemoryCredentialStore();
        using var env = ProcessEnvironment.Set(CredentialStores.EnvironmentVariable, "{}");
        Assert.Same(Credentials, await OAuthProviders.LoginAsync(provider, Callbacks, null, store, CancellationToken.None));
        Assert.Equal(1, provider.Logins);
        Assert.Same(Credentials, await store.GetAsync("fake"));
    }

    private sealed class FakeProvider : IOAuthProvider
    {
        public int Logins { get; private set; }

        public string Id => "fake";

        public string Name => "Fake";

        public Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
        {
            Logins++;
            return Task.FromResult(Credentials);
        }

        public Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
