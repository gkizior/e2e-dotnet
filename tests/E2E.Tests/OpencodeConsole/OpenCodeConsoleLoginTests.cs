// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Web;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.OpencodeConsole;

[Collection(InstantDeviceFlow.Name)]
public sealed class OpenCodeConsoleLoginTests
{
    private const string ConsoleUrl = "https://console.test/console";

    private static readonly OAuthCredentials Login = new()
    {
        Access = "st_tok",
        Refresh = "rt_1",
        Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000,
        Extra = new Dictionary<string, string> { ["orgId"] = "org_1" },
    };

    [Fact]
    public async Task Runs_the_device_flow_for_a_workspace_scoped_login_and_resolves_the_relative_verification_page()
    {
        var polls = 0;
        var console = new FakeApi(request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            Assert.Equal("e2e", form["client_id"]);
            if (request.Uri.AbsolutePath == "/console/auth/device/code")
            {
                Assert.Equal("true", form["supports_org_scope"]);
                return (HttpStatusCode.OK, """{ "device_code": "dc", "user_code": "ABCD-EFGH", "verification_uri": "/console/device", "verification_uri_complete": "/console/device?user_code=ABCD-EFGH&client_id=e2e", "expires_in": 600, "interval": 0.001 }""");
            }

            Assert.Equal("/console/auth/device/token", request.Uri.AbsolutePath);
            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
            Assert.Equal("dc", form["device_code"]);
            return ++polls == 1
                ? (HttpStatusCode.BadRequest, """{ "error": "authorization_pending", "error_description": "The authorization request is still pending" }""")
                : (HttpStatusCode.OK, """{ "access_token": "st_1", "refresh_token": "rt_1", "token_type": "Bearer", "expires_in": 2592000, "org_id": "org_1" }""");
        });
        var provider = new OpenCodeConsoleProvider(new HttpClient(console), ConsoleUrl);
        OAuthAuthInfo? shown = null;
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var credentials = await provider.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = info => shown = info, OnPrompt = (_, _) => Task.FromResult("") },
            new OAuthLoginOptions(),
            CancellationToken.None);

        const string Page = "https://console.test/console/device?user_code=ABCD-EFGH&client_id=e2e";
        Assert.Equal(Page, shown!.Url);
        Assert.Equal("ABCD-EFGH", shown.UserCode);
        Assert.Contains("Open " + Page, shown.Instructions, StringComparison.Ordinal);
        Assert.Equal(("st_1", "rt_1", "org_1"), (credentials.Access, credentials.Refresh, credentials.Get("orgId")));
        Assert.True(credentials.Expires >= before + 2_592_000_000);
    }

    [Fact]
    public async Task Refreshes_with_rotation_and_keeps_the_workspace_when_a_grant_omits_it()
    {
        var console = new FakeApi(request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            Assert.Equal("/console/auth/device/token", request.Uri.AbsolutePath);
            Assert.Equal("refresh_token", form["grant_type"]);
            Assert.Equal("e2e", form["client_id"]);
            Assert.Equal("rt_1", form["refresh_token"]);
            return (HttpStatusCode.OK, """{ "access_token": "st_2", "refresh_token": "rt_2", "token_type": "Bearer", "expires_in": 60 }""");
        });
        var provider = new OpenCodeConsoleProvider(new HttpClient(console), ConsoleUrl);

        var renewed = await provider.RefreshAsync(Login, CancellationToken.None);

        Assert.Equal(("st_2", "rt_2", "org_1"), (renewed.Access, renewed.Refresh, renewed.Get("orgId")));
    }

    [Fact]
    public async Task Keeps_the_refresh_token_when_a_refresh_grant_omits_a_new_one()
    {
        var console = new FakeApi(_ => (HttpStatusCode.OK, """{ "access_token": "st_2", "token_type": "Bearer", "expires_in": 60 }"""));
        var provider = new OpenCodeConsoleProvider(new HttpClient(console), ConsoleUrl);

        var renewed = await provider.RefreshAsync(Login, CancellationToken.None);

        Assert.Equal(("st_2", "rt_1", "org_1"), (renewed.Access, renewed.Refresh, renewed.Get("orgId")));
    }
}
