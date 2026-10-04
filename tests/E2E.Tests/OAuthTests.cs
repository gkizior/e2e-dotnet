// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using E2E.OAuth;

namespace E2E.Tests;

public sealed class OAuthTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "e2e-oauth-" + Guid.NewGuid().ToString("N"));

    public OAuthTests()
    {
        DeviceFlow.Delay = (_, _) => Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [Fact]
    public async Task File_store_round_trips_and_keeps_entries_it_does_not_understand()
    {
        var path = Path.Combine(_directory, "e2e", "oauth.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{ "other": { "access": "a", "refresh": "r", "expires": 1, "custom": [1, 2] }, "broken": 3 }""");
        var store = new FileCredentialStore(path);

        await store.SetAsync("openai", new OAuthCredentials { Access = "tok", Refresh = "ref", Expires = 42, Extra = new Dictionary<string, string> { ["accountId"] = "acct" } });

        var read = await store.GetAsync("openai");
        Assert.Equal("tok", read!.Access);
        Assert.Equal(42, read.Expires);
        Assert.Equal("acct", read.Get("accountId"));
        var file = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(2, file["other"]!["custom"]!.AsArray().Count);
        Assert.Equal(3, file["broken"]!.GetValue<int>());
        Assert.Equal(["other", "openai"], await store.ListAsync());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }

        Assert.True(await store.RemoveAsync("openai"));
        Assert.False(await store.RemoveAsync("openai"));
        Assert.Null(await store.GetAsync("openai"));
        Assert.False(File.Exists(path + ".lock"));
    }

    [Fact]
    public async Task Environment_store_reads_and_refuses_writes()
    {
        var store = new EnvironmentCredentialStore("""{ "spacexai": { "access": "a", "refresh": "", "expires": 0 } }""");

        Assert.Equal("a", (await store.GetAsync("spacexai"))!.Access);
        var error = await Assert.ThrowsAsync<OAuthException>(() => store.SetAsync("spacexai", new OAuthCredentials { Access = "b" }));
        Assert.Equal(OAuthException.Misconfigured, error.Code);
    }

    [Fact]
    public async Task Handler_swaps_in_the_bearer_token_and_drops_api_key_headers()
    {
        var store = Store(new OAuthCredentials { Access = "live", Refresh = "r", Expires = Later() });
        var inner = new RecordingHandler("""{ "ok": true }""");
        using var http = new HttpClient(new OAuthHandler(new FakeProvider(), store, inner: inner));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.test/v1/messages") { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        request.Headers.TryAddWithoutValidation("x-api-key", "oauth");

        using var response = await http.SendAsync(request);

        var sent = inner.Requests.Single();
        Assert.Equal("Bearer live", sent.Headers["Authorization"]);
        Assert.False(sent.Headers.ContainsKey("x-api-key"));
        Assert.Equal("yes", sent.Headers["x-fake"]);
        Assert.Equal("{}", sent.Body);
    }

    [Fact]
    public async Task Handler_refreshes_an_expiring_token_and_stores_it()
    {
        var store = Store(new OAuthCredentials { Access = "old", Refresh = "r1", Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000 });
        var provider = new FakeProvider();
        var inner = new RecordingHandler("""{ "ok": true }""");
        using var http = new HttpClient(new OAuthHandler(provider, store, inner: inner));

        using var response = await http.GetAsync(new Uri("https://api.test/v1/models"));

        Assert.Equal(1, provider.Refreshes);
        Assert.Equal("Bearer fresh1", inner.Requests.Single().Headers["Authorization"]);
        Assert.Equal("fresh1", (await store.GetAsync("fake"))!.Access);
    }

    [Fact]
    public async Task Handler_refreshes_once_on_401_and_retries()
    {
        var store = Store(new OAuthCredentials { Access = "revoked", Refresh = "r1", Expires = Later() });
        var provider = new FakeProvider();
        var sequence = new SequenceHandler((HttpStatusCode.Unauthorized, "{}"), (HttpStatusCode.OK, """{ "ok": true }"""));
        using var http = new HttpClient(new OAuthHandler(provider, store, inner: sequence));

        using var response = await http.PostAsync(new Uri("https://api.test/v1/chat"), new StringContent("{\"a\":1}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["Bearer revoked", "Bearer fresh1"], sequence.Authorizations);
        Assert.Equal(["{\"a\":1}", "{\"a\":1}"], sequence.Bodies);
    }

    [Fact]
    public async Task Handler_reports_a_rejected_token_with_nothing_to_refresh()
    {
        var store = Store(new OAuthCredentials { Access = "gh", Refresh = "", Expires = 0 });
        using var http = new HttpClient(new OAuthHandler(new FakeProvider(), store, inner: new SequenceHandler((HttpStatusCode.Unauthorized, """{ "message": "Bad credentials" }"""))));

        var error = await Assert.ThrowsAsync<OAuthException>(() => http.GetAsync(new Uri("https://api.test/v1/models")));

        Assert.Equal(OAuthException.LoginRequired, error.Code);
        Assert.Contains("Bad credentials", error.Message, StringComparison.Ordinal);
        Assert.Contains("e2e login fake", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_login_blocks_the_agent_step()
    {
        var store = new EnvironmentCredentialStore("{}");
        using var model = new OpenAiCompatibleModel(
            new OpenAiCompatibleModelOptions { Model = "grok-test", BaseUrl = XaiProvider.ApiUrl, ApiKey = "oauth" },
            Subscriptions.Client(new XaiProvider(), store, inner: new RecordingHandler("{}")));

        var error = await Assert.ThrowsAsync<AgentException>(() => model.CompleteAsync(ModelProviderTests.Conversation(), CancellationToken.None));

        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.True(error.Blocked);
        Assert.Contains("NOT_LOGGED_IN", error.Message, StringComparison.Ordinal);
        Assert.Contains("e2e login spacexai", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Codex_requests_go_to_the_backend_streamed_without_storage()
    {
        var provider = new CodexProvider(apiUrl: "https://codex.test/backend-api/codex/responses");
        var credentials = new OAuthCredentials { Access = "a", Refresh = "r", Extra = new Dictionary<string, string> { ["accountId"] = "acct", ["residency"] = "eu" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://codex.test/backend-api/codex/responses")
        {
            Content = new StringContent("""{ "model": "gpt-6-luna", "instructions": "", "max_output_tokens": 100, "input": [] }"""),
        };

        await provider.PrepareAsync(request, credentials, CancellationToken.None);

        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.Equal("Follow the user request.", body["instructions"]!.GetValue<string>());
        Assert.Equal("reasoning.encrypted_content", body["include"]![0]!.GetValue<string>());
        Assert.Null(body["max_output_tokens"]);
        Assert.Equal("acct", request.Headers.GetValues("chatgpt-account-id").Single());
        Assert.Equal("eu", request.Headers.GetValues("x-openai-internal-codex-residency").Single());
        Assert.Equal("e2e", request.Headers.GetValues("originator").Single());
    }

    [Fact]
    public void Codex_reads_the_account_and_pasted_codes()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"https://api.openai.com/auth":{"chatgpt_account_id":"acct_1"}}""")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal("acct_1", CodexProvider.AccountId(null, "h." + payload + ".s"));
        Assert.Equal(("abc", "xyz"), CodexProvider.ParseAuthorizationInput("http://localhost:1455/auth/callback?code=abc&state=xyz"));
        Assert.Equal(("abc", "xyz"), CodexProvider.ParseAuthorizationInput("abc#xyz"));
        Assert.Equal(("abc", (string?)null), CodexProvider.ParseAuthorizationInput(" abc "));
    }

    [Fact]
    public async Task Device_flow_polls_until_the_grant_lands()
    {
        var http = new HttpClient(new SequenceHandler(
            (HttpStatusCode.OK, """{ "device_code": "dev", "user_code": "ABCD-1234", "verification_uri": "https://auth.test/device", "interval": 1, "expires_in": 600 }"""),
            (HttpStatusCode.BadRequest, """{ "error": "authorization_pending" }"""),
            (HttpStatusCode.BadRequest, """{ "error": "slow_down" }"""),
            (HttpStatusCode.OK, """{ "access_token": "acc", "refresh_token": "ref", "expires_in": 3600 }""")));
        OAuthAuthInfo? shown = null;
        var provider = new XaiProvider(http, issuer: "https://auth.test");

        var credentials = await provider.LoginAsync(Callbacks(info => shown = info), new OAuthLoginOptions(), CancellationToken.None);

        Assert.Equal("acc", credentials.Access);
        Assert.Equal("ref", credentials.Refresh);
        Assert.Equal("ABCD-1234", shown!.UserCode);
        Assert.Contains("https://auth.test/device", shown.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Device_flow_reports_a_denied_authorization()
    {
        var http = new HttpClient(new SequenceHandler(
            (HttpStatusCode.OK, """{ "device_code": "dev", "user_code": "U", "verification_uri": "https://auth.test/device" }"""),
            (HttpStatusCode.BadRequest, """{ "error": "access_denied" }""")));

        var error = await Assert.ThrowsAsync<OAuthException>(() => new XaiProvider(http, issuer: "https://auth.test").LoginAsync(Callbacks(_ => { }), new OAuthLoginOptions(), CancellationToken.None));

        Assert.Equal(OAuthException.Cancelled, error.Code);
    }

    [Fact]
    public async Task Copilot_login_reuses_the_github_cli_and_keeps_the_enterprise_host()
    {
        var provider = new CopilotProvider(githubCliToken: (host, _) => Task.FromResult<string?>(host == "github.example.com" ? "gho_cli" : null));

        var credentials = await provider.LoginAsync(Callbacks(_ => { }), new OAuthLoginOptions { EnterpriseUrl = "https://GitHub.Example.com" }, CancellationToken.None);

        Assert.Equal("gho_cli", credentials.Access);
        Assert.Equal(0, credentials.Expires);
        Assert.Equal("github.example.com", credentials.Get("enterpriseUrl"));
        Assert.Throws<OAuthException>(() => CopilotProvider.EnterpriseHost("https://evil.test:8443/path"));
        Assert.Equal("https://copilot-api.github.example.com", CopilotProvider.BaseUrl("github.example.com"));
    }

    [Fact]
    public async Task Copilot_requests_name_the_initiator_and_route_enterprise_logins()
    {
        var provider = new CopilotProvider();
        var credentials = new OAuthCredentials { Access = "gh", Extra = new Dictionary<string, string> { ["enterpriseUrl"] = "github.example.com" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, CopilotProvider.ApiUrl + "/chat/completions")
        {
            Content = new StringContent("""{ "messages": [{ "role": "user", "content": "hi" }, { "role": "tool", "content": "x" }] }"""),
        };

        await provider.PrepareAsync(request, credentials, CancellationToken.None);

        Assert.Equal("https://copilot-api.github.example.com/chat/completions", request.RequestUri!.AbsoluteUri);
        Assert.Equal("agent", request.Headers.GetValues("x-initiator").Single());
        Assert.Equal("conversation-edits", request.Headers.GetValues("openai-intent").Single());
    }

    [Fact]
    public async Task Copilot_model_uses_the_responses_api_when_the_plan_serves_the_model_only_there()
    {
        var store = Store(new OAuthCredentials { Access = "gh", Refresh = "", Expires = 0 }, "github-copilot");
        var inner = new RecordingHandler(
            """{ "data": [{ "id": "gpt-6-codex", "supported_endpoints": ["/responses"] }] }""",
            """{ "output": [{ "type": "message", "content": [{ "type": "output_text", "text": "ok" }] }] }""");
        using var direct = new CopilotModel("gpt-6-codex", Subscriptions.Client(new CopilotProvider(), store, inner: inner));

        var response = await direct.CompleteAsync(ModelProviderTests.Conversation(), CancellationToken.None);

        Assert.Equal("ok", response.Content);
        Assert.Equal("https://api.githubcopilot.com/models", inner.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("https://api.githubcopilot.com/responses", inner.Requests[1].Uri.AbsoluteUri);
        Assert.Equal("Bearer gh", inner.Requests[1].Headers["Authorization"]);
    }

    [Fact]
    public async Task OpenCode_console_routes_a_model_to_its_protocol_and_lists_paid_models()
    {
        const string config = """
            {
              "config": {
                "provider": {
                  "opencode": {
                    "npm": "@ai-sdk/openai-compatible",
                    "api": "https://opencode.test/zen/v1",
                    "models": {
                      "claude-test": { "id": "claude-test", "name": "Claude", "cost": { "input": 3, "output": 15 }, "provider": { "npm": "@ai-sdk/anthropic" } },
                      "free-model": { "cost": { "input": 0, "output": 0 } }
                    }
                  }
                }
              }
            }
            """;
        var store = Store(new OAuthCredentials { Access = "oc", Refresh = "", Expires = 0, Extra = new Dictionary<string, string> { ["orgId"] = "org_1" } }, "opencode-console");
        var provider = new OpenCodeConsoleProvider(consoleUrl: "https://opencode.test/console");
        var inner = new RecordingHandler(config, """{ "content": [{ "type": "text", "text": "ok" }] }""").Then(HttpStatusCode.OK, config);
        using var model = new OpenCodeConsoleModel("claude-test", provider, Subscriptions.Client(provider, store, inner: inner));

        var response = await model.CompleteAsync(ModelProviderTests.Conversation(), CancellationToken.None);
        using var http = Subscriptions.Client(provider, store, inner: inner);
        var listed = await provider.ListModelsAsync(http, CancellationToken.None);

        Assert.Equal("ok", response.Content);
        Assert.Equal("org_1", inner.Requests[0].Headers["x-org-id"]);
        Assert.Equal("https://opencode.test/zen/v1/messages", inner.Requests[1].Uri.AbsoluteUri);
        Assert.Equal("org_1", inner.Requests[1].Headers["x-opencode-org-id"]);
        Assert.StartsWith("e2e_", inner.Requests[1].Headers["x-opencode-session"], StringComparison.Ordinal);
        Assert.False(inner.Requests[1].Headers.ContainsKey("x-api-key"));
        var only = Assert.Single(listed);
        Assert.Equal("claude-test", only.Id);
        Assert.Equal("Zen, $3 in, $15 out per 1M", only.Detail);
    }

    [Fact]
    public void Providers_are_known_by_upstream_ids()
    {
        Assert.Equal(["openai", "github-copilot", "opencode-console", "spacexai"], OAuthProviders.Ids);
        Assert.All(OAuthProviders.Ids, id => Assert.Equal(id, OAuthProviders.Get(id).Id));
        Assert.Equal(OAuthException.Misconfigured, Assert.Throws<OAuthException>(() => OAuthProviders.Get("cohere")).Code);
    }

    private FileCredentialStore Store(OAuthCredentials credentials, string id = "fake")
    {
        var store = new FileCredentialStore(Path.Combine(_directory, Guid.NewGuid().ToString("N"), "oauth.json"));
        store.SetAsync(id, credentials).GetAwaiter().GetResult();
        return store;
    }

    private static long Later() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;

    private static OAuthLoginCallbacks Callbacks(Action<OAuthAuthInfo> onAuth)
    {
        return new OAuthLoginCallbacks { OnAuth = onAuth, OnPrompt = (_, _) => Task.FromResult("") };
    }

    private sealed class FakeProvider : IOAuthProvider
    {
        public int Refreshes { get; private set; }

        public string Id => "fake";

        public string Name => "Fake";

        public Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken)
        {
            Refreshes++;
            return Task.FromResult(new OAuthCredentials { Access = "fresh" + Refreshes, Refresh = "r" + (Refreshes + 1), Expires = Later() });
        }

        public Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("x-fake", "yes");
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SubscriptionModel>>([]);
        }
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _answers;

        public SequenceHandler(params (HttpStatusCode Status, string Body)[] answers)
        {
            _answers = new Queue<(HttpStatusCode Status, string Body)>(answers);
        }

        public List<string> Authorizations { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString() ?? "");
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            var (status, body) = _answers.Dequeue();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
