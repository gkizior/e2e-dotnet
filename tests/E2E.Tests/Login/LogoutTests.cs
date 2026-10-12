// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Login;

public sealed class LogoutTests
{
    /// <summary>
    /// Partial: upstream also asks a read-only environment store to log out a provider it does not hold, and gets false. The
    /// port's <c>LogoutAsync</c> asks the store to remove it, and that store refuses every removal, so that part is not ported.
    /// </summary>
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
    }
}
