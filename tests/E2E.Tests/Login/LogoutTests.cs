// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Login;

public sealed class LogoutTests
{
    [Fact]
    public async Task Reports_whether_a_login_was_stored_and_removes_it()
    {
        var store = new MemoryCredentialStore(new Dictionary<string, OAuthCredentials>
        {
            ["spacexai"] = new() { Access = "a", Refresh = "r", Expires = 0 },
        });

        Assert.False(await OAuthProviders.LogoutAsync("openai", store));
        Assert.True(await OAuthProviders.LogoutAsync("spacexai", store));
        Assert.Empty(await store.ListAsync());

        // A read-only source is not asked to remove what it does not hold.
        Assert.False(await OAuthProviders.LogoutAsync("openai", new EnvironmentCredentialStore("{}")));
    }
}
