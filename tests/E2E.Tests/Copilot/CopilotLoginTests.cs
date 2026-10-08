// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Web;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Copilot;

[Collection(InstantDeviceFlow.Name)]
public sealed class CopilotLoginTests
{
    private static readonly OAuthLoginCallbacks Quiet = new() { OnAuth = _ => { }, OnPrompt = (_, _) => Task.FromResult("") };

    private static readonly Func<string?, CancellationToken, Task<string?>> NoCli = (_, _) => Task.FromResult<string?>(null);

    [Fact]
    public async Task Runs_GitHubs_device_flow_with_the_callers_OAuth_App_and_stores_a_non_expiring_token()
    {
        var polls = 0;
        var github = new FakeApi(request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            Assert.Equal("Iv23_my_app", form["client_id"]);
            if (request.Uri.AbsolutePath == "/login/device/code")
            {
                return (HttpStatusCode.OK, """{ "device_code": "dc", "user_code": "WXYZ-1234", "verification_uri": "https://github.com/login/device", "expires_in": 900, "interval": 0.001 }""");
            }

            Assert.Equal("/login/oauth/access_token", request.Uri.AbsolutePath);
            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
            Assert.Equal("dc", form["device_code"]);
            polls++;

            // GitHub answers pending with 200 and an error field.
            return polls < 3
                ? (HttpStatusCode.OK, """{ "error": "authorization_pending" }""")
                : (HttpStatusCode.OK, """{ "access_token": "gho_token", "token_type": "bearer", "scope": "read:user" }""");
        });
        var provider = new CopilotProvider(new HttpClient(github), "https://github.test", NoCli);
        OAuthAuthInfo? shown = null;

        var credentials = await provider.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = info => shown = info, OnPrompt = (_, _) => Task.FromResult("") },
            new OAuthLoginOptions { ClientId = "Iv23_my_app", EnterpriseUrl = "https://gh.acme.com/" },
            CancellationToken.None);

        Assert.Equal(("https://github.com/login/device", "WXYZ-1234"), (shown!.Url, shown.UserCode));
        Assert.Equal(("gho_token", "", 0L, "gh.acme.com"), (credentials.Access, credentials.Refresh, credentials.Expires, credentials.Get("enterpriseUrl")));
    }

    [Fact]
    public async Task Reuses_the_GitHub_CLI_token_by_default_and_explains_what_it_needs_when_there_is_none()
    {
        var withCli = new CopilotProvider(githubCliToken: (hostname, _) => Task.FromResult(hostname is null ? "gho_cli" : null));
        var credentials = await withCli.LoginAsync(Quiet, new OAuthLoginOptions(), CancellationToken.None);
        Assert.Equal(("gho_cli", "", 0L), (credentials.Access, credentials.Refresh, credentials.Expires));
        await Misconfigured(() => withCli.LoginAsync(Quiet, new OAuthLoginOptions { EnterpriseUrl = "gh.acme.com" }, CancellationToken.None));
        Assert.Equal("gho_cli", (await withCli.LoginAsync(Quiet, new OAuthLoginOptions { ClientId = "Iv23", FromGitHubCli = true }, CancellationToken.None)).Access);
        await Misconfigured(() => new CopilotProvider(githubCliToken: NoCli).LoginAsync(Quiet, new OAuthLoginOptions(), CancellationToken.None));
    }

    [Fact]
    public async Task Cannot_refresh_a_rejected_token_means_signing_in_again()
    {
        var error = await Assert.ThrowsAsync<OAuthException>(() => new CopilotProvider().RefreshAsync(new OAuthCredentials { Access = "x" }, CancellationToken.None));
        Assert.Equal(OAuthException.LoginRequired, error.Code);
    }

    [Fact]
    public async Task Derives_the_enterprise_API_host_and_refuses_anything_but_a_plain_hostname()
    {
        Assert.Equal("https://api.githubcopilot.com", CopilotProvider.BaseUrl(null));
        Assert.Equal("https://copilot-api.github.acme.com", CopilotProvider.BaseUrl("https://github.acme.com/"));
        Assert.Equal("github.acme.com", CopilotProvider.EnterpriseHost("GitHub.Acme.com"));
        foreach (var bad in new[] { "evil.com@github.acme.com", "github.acme.com/path", "github.acme.com?x=1", "github.acme.com:8443", "http://github.acme.com", "not a host", "localhost" })
        {
            var error = Assert.Throws<OAuthException>(() => CopilotProvider.EnterpriseHost(bad));
            Assert.Contains("not a GitHub Enterprise host", error.Message, StringComparison.Ordinal);
        }

        var provider = new CopilotProvider(githubCliToken: (_, _) => Task.FromResult<string?>("gho"));
        await Misconfigured(() => provider.LoginAsync(Quiet, new OAuthLoginOptions { EnterpriseUrl = "evil.com@github.acme.com" }, CancellationToken.None));
    }

    private static async Task Misconfigured(Func<Task> run)
    {
        var error = await Assert.ThrowsAsync<OAuthException>(run);
        Assert.Equal(OAuthException.Misconfigured, error.Code);
    }
}
