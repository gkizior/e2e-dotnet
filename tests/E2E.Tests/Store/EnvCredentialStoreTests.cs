// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;

namespace E2E.Tests.Store;

[Collection(ProcessEnvironment.Name)]
public sealed class EnvCredentialStoreTests
{
    [Fact]
    public async Task Is_chosen_when_the_variable_is_set_reads_it_and_refuses_to_change_it()
    {
        var creds = new OAuthCredentials { Access = "a", Refresh = "r", Expires = 123, Extra = new Dictionary<string, string> { ["accountId"] = "acct" } };
        using (ProcessEnvironment.Set(CredentialStores.EnvironmentVariable, """{ "github-copilot": { "access": "a", "refresh": "r", "expires": 123, "accountId": "acct" } }"""))
        {
            var store = Assert.IsType<EnvironmentCredentialStore>(CredentialStores.Default());
            var read = await store.GetAsync("github-copilot");
            Assert.Equal(("a", "r", 123L, "acct"), (read!.Access, read.Refresh, read.Expires, read.Get("accountId")));
            Assert.Equal(["github-copilot"], await store.ListAsync());
            var set = await Assert.ThrowsAsync<OAuthException>(() => store.SetAsync("spacexai", creds));
            Assert.Equal(OAuthException.Misconfigured, set.Code);
            Assert.Contains(CredentialStores.EnvironmentVariable, set.Message, StringComparison.Ordinal);
            Assert.Equal(OAuthException.Misconfigured, (await Assert.ThrowsAsync<OAuthException>(() => store.RemoveAsync("github-copilot"))).Code);
        }

        using (ProcessEnvironment.Set(CredentialStores.EnvironmentVariable, ""))
        {
            Assert.IsType<FileCredentialStore>(CredentialStores.Default());
        }
    }

    [Fact]
    public void Rejects_malformed_JSON_naming_the_variable()
    {
        var error = Assert.Throws<OAuthException>(() => new EnvironmentCredentialStore("nope"));
        Assert.Contains(CredentialStores.EnvironmentVariable + " is not valid JSON", error.Message, StringComparison.Ordinal);
    }
}
