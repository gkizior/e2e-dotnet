// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Web;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.LoginRequired;

/// <summary>
/// An expired sign-in names the login command. The provider's token endpoint is a fake that rejects the refresh for good.
/// </summary>
public sealed class ConstructorTests
{
    /// <summary>
    /// Partial: the test builds the HTTP client the model constructors <c>Subscriptions.ChatGpt</c>, <c>Grok</c>, and
    /// <c>OpenCodeConsole</c> build, over a provider that points at the fake endpoint, because those constructors take no
    /// endpoint. The login command is the port's <c>e2e login</c>, where upstream names <c>npx e2e login</c>.
    /// </summary>
    [Theory]
    [InlineData("openai", "/oauth/token", "ChatGPT token request failed (401: invalid_grant); run `e2e login openai`")]
    [InlineData("spacexai", "/oauth2/token", "SpaceXAI token request failed (401: invalid_grant); run `e2e login spacexai`")]
    [InlineData("opencode-console", "/console/auth/device/token", "OpenCode Console token request failed (401: invalid_grant); run `e2e login opencode-console`")]
    public async Task Fails_with_LOGIN_REQUIRED_naming_the_login_command_when_the_token_endpoint_rejects_the_refresh(string id, string tokenPath, string message)
    {
        var issuer = new FakeApi(request => (request.Uri.AbsolutePath == tokenPath ? HttpStatusCode.Unauthorized : HttpStatusCode.InternalServerError, """{ "error": "invalid_grant" }"""));
        IOAuthProvider provider = id switch
        {
            "openai" => new CodexProvider(new HttpClient(issuer), issuer: "https://auth.test"),
            "spacexai" => new XaiProvider(new HttpClient(issuer), issuer: "https://auth.test"),
            _ => new OpenCodeConsoleProvider(new HttpClient(issuer), consoleUrl: "https://console.test/console"),
        };
        // A login whose token lapses inside the refresh skew, so the first call refreshes before it sends anything.
        var store = new MemoryCredentialStore(new Dictionary<string, OAuthCredentials>
        {
            [id] = new() { Access = "stale", Refresh = "rt-0", Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1_000 },
        });
        using var client = Subscriptions.Client(provider, store);

        var error = await Assert.ThrowsAsync<OAuthException>(() => client.GetAsync("https://api.test/v1/responses"));

        Assert.Equal(OAuthException.LoginRequired, error.Code);
        Assert.Equal(message, error.Message);
        // One refresh, no API call with the stale token, no retry.
        var request = Assert.Single(issuer.Requests);
        Assert.Equal(tokenPath, request.Uri.AbsolutePath);
        var form = HttpUtility.ParseQueryString(request.Body);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("rt-0", form["refresh_token"]);
    }
}
