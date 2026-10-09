// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Web;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Openai;

[Collection(InstantDeviceFlow.Name)]
public sealed class CodexLoginTests
{
    private const string Issuer = "https://auth.test";

    private static readonly string IdToken = FakeJwt.Of("""{"https://api.openai.com/auth":{"chatgpt_account_id":"acct_123"}}""");

    private static readonly string AccessToken = FakeJwt.Of("""{"https://api.openai.com/auth":{"chatgpt_compute_residency":"eu"}}""");

    private static readonly string TokenReply = "{ \"access_token\": \"" + AccessToken + "\", \"refresh_token\": \"ref\", \"id_token\": \"" + IdToken + "\", \"expires_in\": 600 }";

    [Fact]
    public async Task Runs_the_browser_flow_PKCE_authorize_URL_local_callback_code_exchange_account_id_and_residency()
    {
        var issuer = new FakeApi(request => (request.Uri.AbsolutePath == "/oauth/token" ? HttpStatusCode.OK : HttpStatusCode.NotFound, TokenReply));
        var provider = new CodexProvider(new HttpClient(issuer), originator: "my-tool", issuer: Issuer, callbackPort: FreePort());
        Uri? authorize = null;
        Task<HttpResponseMessage>? browser = null;

        var credentials = await provider.LoginAsync(
            new OAuthLoginCallbacks
            {
                OnAuth = info =>
                {
                    authorize = new Uri(info.Url);
                    browser = BrowserReturns(info.Url, new() { ["code"] = "the-code", ["state"] = "$state" });
                },
                OnPrompt = (_, _) => throw new InvalidOperationException("should not prompt"),
            },
            new OAuthLoginOptions(),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, (await browser!).StatusCode);
        Assert.Equal(Issuer + "/oauth/authorize", authorize!.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(authorize.Query);
        Assert.Equal("my-tool", query["originator"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("app_EMoamEEZ73f0CkXaXp7hrann", query["client_id"]);
        var exchange = HttpUtility.ParseQueryString(issuer.Requests.First(request => request.Uri.AbsolutePath == "/oauth/token").Body);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal("the-code", exchange["code"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", exchange["code_verifier"]);
        Assert.Equal(query["redirect_uri"], exchange["redirect_uri"]);
        Assert.Equal((AccessToken, "ref", "acct_123", "eu"), (credentials.Access, credentials.Refresh, credentials.Get("accountId"), credentials.Get("residency")));
        Assert.True(credentials.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Cancels_a_held_authorization_code_exchange_after_the_browser_returns()
    {
        var issuer = new HeldExchange(_ => (HttpStatusCode.OK, TokenReply));
        var provider = new CodexProvider(new HttpClient(issuer), issuer: Issuer, callbackPort: FreePort());
        using var cancellation = new CancellationTokenSource();
        Task<HttpResponseMessage>? browser = null;

        var pending = provider.LoginAsync(
            new OAuthLoginCallbacks
            {
                OnAuth = info => browser = BrowserReturns(info.Url, new() { ["code"] = "the-code", ["state"] = "$state" }),
                OnPrompt = (_, _) => Task.FromResult(""),
            },
            new OAuthLoginOptions(),
            cancellation.Token);
        await issuer.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();

        var error = await Assert.ThrowsAsync<OAuthException>(() => pending);
        Assert.Equal(OAuthException.Cancelled, error.Code);
        Assert.Equal(HttpStatusCode.OK, (await browser!).StatusCode);
        Assert.Single(issuer.Requests);
    }

    [Fact]
    public async Task Cancels_a_held_authorization_code_exchange_after_the_device_code_is_granted()
    {
        var issuer = new HeldExchange(request => request.Uri.AbsolutePath switch
        {
            "/api/accounts/deviceauth/usercode" => (HttpStatusCode.OK, """{ "device_auth_id": "dev-1", "user_code": "LOCAL", "interval": "0.001" }"""),
            "/api/accounts/deviceauth/token" => (HttpStatusCode.OK, """{ "authorization_code": "granted-code", "code_verifier": "verifier" }"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        });
        var provider = new CodexProvider(new HttpClient(issuer), issuer: Issuer);
        using var cancellation = new CancellationTokenSource();

        var pending = provider.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = _ => { }, OnPrompt = (_, _) => Task.FromResult("") },
            new OAuthLoginOptions { Device = true },
            cancellation.Token);
        await issuer.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();

        var error = await Assert.ThrowsAsync<OAuthException>(() => pending);
        Assert.Equal(OAuthException.Cancelled, error.Code);
        Assert.Equal(3, issuer.Requests.Count);
    }

    [Fact]
    public async Task Ignores_a_callback_with_the_wrong_state_and_treats_a_declined_sign_in_as_cancelled()
    {
        var issuer = new FakeApi(_ => (HttpStatusCode.OK, TokenReply));
        var provider = new CodexProvider(new HttpClient(issuer), issuer: Issuer, callbackPort: FreePort());
        var statuses = new List<HttpStatusCode>();
        Task? browser = null;

        var error = await Assert.ThrowsAsync<OAuthException>(() => provider.LoginAsync(
            new OAuthLoginCallbacks
            {
                OnAuth = info =>
                {
                    // The bogus callbacks are answered before the decline, which closes the server behind them.
                    browser = Task.Run(async () =>
                    {
                        statuses.Add((await BrowserReturns(info.Url, new() { ["code"] = "stolen", ["state"] = "not-ours" })).StatusCode);
                        statuses.Add((await BrowserReturns(info.Url, new() { ["error"] = "x", ["state"] = "not-ours" })).StatusCode);
                        statuses.Add((await BrowserReturns(info.Url, new() { ["error"] = "access_denied", ["state"] = "$state" })).StatusCode);
                    });
                },
                OnPrompt = (_, _) => Task.FromResult(""),
            },
            new OAuthLoginOptions(),
            CancellationToken.None));

        Assert.Equal(OAuthException.Cancelled, error.Code);
        await browser!;
        Assert.Equal([HttpStatusCode.BadRequest, HttpStatusCode.BadRequest, HttpStatusCode.BadRequest], statuses);
        Assert.Empty(issuer.Requests);
    }

    [Fact]
    public async Task Asks_for_the_pasted_code_when_the_callback_port_is_taken_or_the_browser_never_returns()
    {
        var issuer = new FakeApi(_ => (HttpStatusCode.OK, TokenReply));
        var squatted = FreePort();
        using var squatter = new HttpListener();
        squatter.Prefixes.Add("http://localhost:" + squatted.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
        squatter.Start();
        try
        {
            var taken = new CodexProvider(new HttpClient(issuer), issuer: Issuer, callbackPort: squatted);
            var credentials = await taken.LoginAsync(
                new OAuthLoginCallbacks { OnAuth = _ => { }, OnPrompt = (_, _) => Task.FromResult("http://localhost/auth/callback?code=pasted-code") },
                new OAuthLoginOptions(),
                CancellationToken.None);
            Assert.Equal(AccessToken, credentials.Access);
            Assert.Equal("pasted-code", HttpUtility.ParseQueryString(issuer.Requests.Last().Body)["code"]);
        }
        finally
        {
            squatter.Stop();
        }

        // After the browser fails to return in time, the terminal asks for the URL instead of failing.
        var slow = new CodexProvider(new HttpClient(issuer), issuer: Issuer, callbackPort: FreePort(), loginTimeout: TimeSpan.FromMilliseconds(50));
        var pasted = await slow.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = _ => { }, OnPrompt = (_, _) => Task.FromResult("late-code") },
            new OAuthLoginOptions(),
            CancellationToken.None);
        Assert.Equal(AccessToken, pasted.Access);
        Assert.Equal("late-code", HttpUtility.ParseQueryString(issuer.Requests.Last().Body)["code"]);
    }

    [Fact]
    public async Task Is_cancelled_at_once_by_an_already_aborted_signal_and_by_a_malformed_callback()
    {
        var issuer = new FakeApi(_ => (HttpStatusCode.OK, TokenReply));
        var provider = new CodexProvider(new HttpClient(issuer), issuer: Issuer, callbackPort: FreePort(), loginTimeout: TimeSpan.FromSeconds(60));
        var quiet = new OAuthLoginCallbacks { OnAuth = _ => { }, OnPrompt = (_, _) => Task.FromResult("") };
        var aborted = await Assert.ThrowsAsync<OAuthException>(() => provider.LoginAsync(quiet, new OAuthLoginOptions(), new CancellationToken(canceled: true)));
        Assert.Equal(OAuthException.Cancelled, aborted.Code);

        var malformed = await Assert.ThrowsAsync<OAuthException>(() => provider.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = info => _ = BrowserReturns(info.Url, new() { ["state"] = "$state" }), OnPrompt = (_, _) => Task.FromResult("") },
            new OAuthLoginOptions(),
            CancellationToken.None));
        Assert.Equal(OAuthException.FlowFailed, malformed.Code);
        Assert.Contains("invalid_callback", malformed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runs_the_device_flow_through_OpenAIs_user_code_endpoints()
    {
        var polls = 0;
        var issuer = new FakeApi(request =>
        {
            switch (request.Uri.AbsolutePath)
            {
                case "/api/accounts/deviceauth/usercode":
                    return (HttpStatusCode.OK, """{ "device_auth_id": "dev-1", "user_code": "ABCD-EFGH", "interval": "0.001" }""");
                case "/api/accounts/deviceauth/token":
                    polls++;
                    return polls < 3 ? (HttpStatusCode.Forbidden, "{}") : (HttpStatusCode.OK, """{ "authorization_code": "granted-code", "code_verifier": "verifier" }""");
                case "/oauth/token":
                    var form = HttpUtility.ParseQueryString(request.Body);
                    Assert.Equal("granted-code", form["code"]);
                    Assert.Equal("verifier", form["code_verifier"]);
                    Assert.Equal(Issuer + "/deviceauth/callback", form["redirect_uri"]);
                    return (HttpStatusCode.OK, TokenReply);
                default:
                    return (HttpStatusCode.NotFound, "{}");
            }
        });
        var provider = new CodexProvider(new HttpClient(issuer), issuer: Issuer);
        OAuthAuthInfo? shown = null;

        var credentials = await provider.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = info => shown = info, OnPrompt = (_, _) => Task.FromResult("") },
            new OAuthLoginOptions { Device = true },
            CancellationToken.None);

        Assert.Equal((Issuer + "/codex/device", "ABCD-EFGH"), (shown!.Url, shown.UserCode));
        Assert.Equal("acct_123", credentials.Get("accountId"));
    }

    [Fact]
    public void Parses_every_form_the_user_may_paste()
    {
        Assert.Equal(("abc", "st"), CodexProvider.ParseAuthorizationInput("http://localhost:1455/auth/callback?code=abc&state=st"));
        Assert.Equal(("abc", "st"), CodexProvider.ParseAuthorizationInput("abc#st"));
        Assert.Equal(("abc", "st"), CodexProvider.ParseAuthorizationInput("code=abc&state=st"));
        Assert.Equal(("abc", (string?)null), CodexProvider.ParseAuthorizationInput("  abc "));
        Assert.Equal(((string?)null, (string?)null), CodexProvider.ParseAuthorizationInput(""));
    }

    [Fact]
    public void Reads_the_account_id_from_either_token()
    {
        Assert.Equal("acct_123", CodexProvider.AccountId(IdToken, null));
        Assert.Equal("org_1", CodexProvider.AccountId(null, FakeJwt.Of("""{"organizations":[{"id":"org_1"}]}""")));
        Assert.Null(CodexProvider.AccountId(null, "opaque"));
    }

    [Fact]
    public async Task Refreshes_keeping_the_account_id_and_the_old_refresh_token_when_none_comes_back_and_asks_for_a_new_login_on_a_rejected_one()
    {
        var reply = (HttpStatusCode.OK, """{ "access_token": "new", "expires_in": "garbage" }""");
        var issuer = new FakeApi(request =>
        {
            Assert.Equal("refresh_token", HttpUtility.ParseQueryString(request.Body)["grant_type"]);
            return reply;
        });
        var provider = new CodexProvider(new HttpClient(issuer), issuer: Issuer);
        var renewed = await provider.RefreshAsync(
            new OAuthCredentials { Access = "old", Refresh = "old-r", Expires = 0, Extra = new Dictionary<string, string> { ["accountId"] = "acct_123", ["residency"] = "eu" } },
            CancellationToken.None);
        Assert.Equal(("new", "old-r", "acct_123", "eu"), (renewed.Access, renewed.Refresh, renewed.Get("accountId"), renewed.Get("residency")));
        Assert.True(renewed.Expires > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        reply = (HttpStatusCode.BadRequest, """{ "error": "invalid_grant" }""");

        // The provider names the failure alone; the handler that asked for the refresh appends the login command.
        var error = await Assert.ThrowsAsync<OAuthException>(() => provider.RefreshAsync(renewed, CancellationToken.None));
        Assert.Equal(OAuthException.LoginRequired, error.Code);
        Assert.Equal("ChatGPT token request failed (400: invalid_grant)", error.Message);
    }

    /// <summary>Answers like a <see cref="FakeApi"/>, but holds the token exchange until the caller gives up.</summary>
    private sealed class HeldExchange(Func<FakeApi.Received, (HttpStatusCode Status, string Body)> answer) : HttpMessageHandler
    {
        private readonly FakeApi _api = new(answer);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<FakeApi.Received> Requests => _api.Requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath != "/oauth/token")
            {
                return await new HttpMessageInvoker(_api).SendAsync(request, cancellationToken);
            }

            _api.Requests.Enqueue(new FakeApi.Received(request.RequestUri, new Dictionary<string, string>(), ""));
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>Simulates the browser returning to the callback server with the given query; "$state" stands for the flow's state.</summary>
    private static async Task<HttpResponseMessage> BrowserReturns(string authorizeUrl, Dictionary<string, string> query)
    {
        var authorize = HttpUtility.ParseQueryString(new Uri(authorizeUrl).Query);
        var redirect = new UriBuilder(authorize["redirect_uri"]!);
        var values = HttpUtility.ParseQueryString(redirect.Query);
        foreach (var (key, value) in query)
        {
            values[key] = value == "$state" ? authorize["state"] : value;
        }

        redirect.Query = values.ToString();
        using var http = new HttpClient();
        return await http.GetAsync(redirect.Uri);
    }
}
