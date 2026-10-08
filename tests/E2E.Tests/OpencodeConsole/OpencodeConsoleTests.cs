// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using E2E.OAuth;
using E2E.Tests.Fetch;
using E2E.Tests.Store;

namespace E2E.Tests.OpencodeConsole;

/// <summary>
/// "marks Anthropic cache breakpoints: the system prompt, the newest message in a tool loop, none over the caller's own"
/// is not ported: the port's model messages carry no per-message provider options, so a caller cannot place its own breakpoint.
/// The class sets OPENCODE_API_KEY in one test, so it runs with the other tests that set the process environment.
/// </summary>
[Collection(ProcessEnvironment.Name)]
public sealed class OpencodeConsoleTests
{
    /// <summary>The port's model names no provider, and its user agent names the port's product, e2e-dotnet.</summary>
    [Fact]
    public async Task Routes_a_Zen_model_to_the_protocol_the_workspace_config_names_naming_the_workspace_and_session_under_one_fixed_name()
    {
        var api = OpencodeConsoleWorkspace.Serve();
        var store = new MemoryCredentialStore(new() { ["opencode-console"] = OpencodeConsoleWorkspace.Login });
        var provider = new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl);
        using var model = new OpenCodeConsoleModel("claude-sonnet-5", provider, Subscriptions.Client(provider, store, inner: api));

        var result = await model.CompleteAsync(new ModelRequest { System = "", Tools = [], Messages = [new ModelMessage { Role = "user", Content = "hi" }] }, CancellationToken.None);

        Assert.Equal("hello", result.Content);
        var config = api.Requests.First();
        Assert.Equal("/console/api/config", config.Uri.AbsolutePath);
        Assert.Equal(("Bearer st_tok", "org_1"), (config.Header("authorization"), config.Header("x-org-id")));
        Assert.Null(config.Header("x-opencode-session"));
        var call = Assert.Single(api.Requests, request => request.Uri.AbsolutePath.StartsWith("/inference/", StringComparison.Ordinal));
        Assert.Equal("/inference/anthropic/v1/messages", call.Uri.AbsolutePath);
        Assert.Equal("Bearer st_tok", call.Header("authorization"));
        Assert.Equal("org_1", call.Header("x-opencode-org-id"));
        // The port writes the session id as 32 hex digits; upstream writes a dashed UUID.
        Assert.Matches(new Regex("^e2e_[0-9a-f]{32}$"), call.Header("x-opencode-session"));
        Assert.Null(call.Header("x-api-key"));
        Assert.Matches(@"^e2e-dotnet/\d+\.\d+\.\d+\S* \(\w+; \w+\)$", call.Header("user-agent"));
        Assert.Equal("claude-sonnet-5", JsonNode.Parse(call.Body)!["model"]!.GetValue<string>());
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/131: Go models ignore provider options under the opencode key")]
    public async Task Serves_Go_models_from_the_Go_provider_remembering_the_route_and_keeping_one_session_per_model()
    {
        var api = OpencodeConsoleWorkspace.Serve();
        var store = Login();
        var provider = new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl);
        using var model = new OpenCodeConsoleModel("go/deepseek-v4.1-flash", provider, Subscriptions.Client(provider, store, inner: api));
        var options = new Dictionary<string, JsonElement> { ["opencode"] = JsonSerializer.SerializeToElement(new { reasoningEffort = "low" }) };
        await model.CompleteAsync(new ModelRequest { System = "", Tools = [], Messages = [new ModelMessage { Role = "user", Content = "hi" }], ProviderOptions = options }, CancellationToken.None);
        await model.CompleteAsync(OpencodeConsoleWorkspace.Prompt("again"), CancellationToken.None);
        using var other = new OpenCodeConsoleModel("go/deepseek-v4.1-flash", provider, Subscriptions.Client(provider, store, inner: api));
        await other.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None);

        const string Go = "/inference/go/openai/v1/chat/completions";
        Assert.Equal(["/console/api/config", Go, Go, "/console/api/config", Go], api.Requests.Select(request => request.Uri.AbsolutePath));
        var calls = OpencodeConsoleWorkspace.Inference(api);
        var body = JsonNode.Parse(calls[0].Body)!;
        Assert.Equal(("deepseek-v4.1-flash", "low"), (body["model"]?.GetValue<string>(), body["reasoning_effort"]?.GetValue<string>()));
        var sessions = calls.Select(call => call.Header("x-opencode-session")).ToList();
        Assert.Equal(sessions[0], sessions[1]);
        Assert.NotEqual(sessions[0], sessions[2]);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/132: a promptless Responses call keys the cache on the empty prompt")]
    public async Task Calls_Responses_models_without_server_storage_under_the_system_prompt_cache_key_and_Google_models_with_the_bearer_alone()
    {
        var api = OpencodeConsoleWorkspace.Serve();
        var store = Login();
        var provider = new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl);
        using (var nano = new OpenCodeConsoleModel("gpt-5-nano", provider, Subscriptions.Client(provider, store, inner: api)))
        {
            await nano.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi", "You test web apps."), CancellationToken.None);
        }

        using (var bareNano = new OpenCodeConsoleModel("gpt-5-nano", provider, Subscriptions.Client(provider, store, inner: api)))
        {
            await bareNano.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None);
        }

        using (var gemini = new OpenCodeConsoleModel("gemini-3.1-pro", provider, Subscriptions.Client(provider, store, inner: api)))
        {
            await gemini.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None);
        }

        var calls = OpencodeConsoleWorkspace.Inference(api);
        var (responses, bare, google) = (calls[0], calls[1], calls[2]);
        Assert.Equal("/inference/openai/v1/responses", responses.Uri.AbsolutePath);
        var body = JsonNode.Parse(responses.Body)!;
        Assert.Equal("gpt-5-nano", body["model"]?.GetValue<string>());
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.Equal(E2E.Internal.ModelHttp.PromptCacheKey("You test web apps."), body["prompt_cache_key"]?.GetValue<string>());
        Assert.Equal(bare.Header("x-opencode-session"), JsonNode.Parse(bare.Body)!["prompt_cache_key"]?.GetValue<string>());
        Assert.Equal("/inference/google/v1beta/models/gemini-3.1-pro:generateContent", google.Uri.AbsolutePath);
        Assert.Equal("Bearer st_tok", google.Header("authorization"));
        Assert.Null(google.Header("x-goog-api-key"));
    }

    [Fact]
    public async Task Says_what_to_do_when_the_login_reaches_no_Go_subscription_or_the_workspace_does_not_serve_the_model()
    {
        var api = OpencodeConsoleWorkspace.Serve(OpencodeConsoleWorkspace.ZenOnlyConfig);
        var store = Login();
        var provider = new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl);
        using var go = new OpenCodeConsoleModel("go/deepseek-v4.1-flash", provider, Subscriptions.Client(provider, store, inner: api));
        var noGo = await Assert.ThrowsAnyAsync<Exception>(() => go.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None));
        var noGoLogin = Assert.IsType<OAuthException>(noGo as OAuthException ?? noGo.InnerException);
        Assert.Equal(OAuthException.Misconfigured, noGoLogin.Code);
        Assert.Contains("OpenCode Go is not available to this login", noGoLogin.Message, StringComparison.Ordinal);

        using var disabled = new OpenCodeConsoleModel("glm-5.1", provider, Subscriptions.Client(provider, store, inner: api));
        var unserved = await Assert.ThrowsAnyAsync<Exception>(() => disabled.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None));
        var unservedLogin = Assert.IsType<OAuthException>(unserved as OAuthException ?? unserved.InnerException);
        Assert.Equal(OAuthException.Misconfigured, unservedLogin.Code);
        // The port names its own CLI command.
        Assert.Equal("the OpenCode Console workspace does not serve glm-5.1; `e2e models opencode-console` lists the ids", unservedLogin.Message);
        Assert.Empty(OpencodeConsoleWorkspace.Inference(api));
    }

    [Fact]
    public async Task Calls_over_chat_when_the_config_cannot_be_read_and_asks_again_on_the_next_call()
    {
        var configs = 0;
        var api = new FakeApi(request =>
        {
            var path = request.Uri.AbsolutePath;
            if (path == "/console/api/config")
            {
                configs++;
                return configs == 1 ? (HttpStatusCode.ServiceUnavailable, """{ "error": "down" }""") : OpencodeConsoleWorkspace.Serve().Respond(request);
            }

            return path.EndsWith("/chat/completions", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, OpencodeConsoleWorkspace.ChatCompletion)
                : (HttpStatusCode.OK, OpencodeConsoleWorkspace.AnthropicMessage);
        });
        var provider = new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl);
        using var model = new OpenCodeConsoleModel("claude-sonnet-5", provider, Subscriptions.Client(provider, Login(), inner: api));

        await model.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None);
        await model.CompleteAsync(OpencodeConsoleWorkspace.Prompt("again"), CancellationToken.None);

        Assert.Equal(
            ["/console/api/config", "/inference/openai/v1/chat/completions", "/console/api/config", "/inference/anthropic/v1/messages"],
            api.Requests.Select(request => request.Uri.AbsolutePath));
        var calls = OpencodeConsoleWorkspace.Inference(api);
        Assert.Equal(calls[0].Header("x-opencode-session"), calls[1].Header("x-opencode-session"));
    }

    [Fact]
    public async Task Uses_OPENCODE_API_KEY_in_place_of_the_stored_login_a_key_with_no_workspace_header_to_send()
    {
        var api = OpencodeConsoleWorkspace.Serve();
        using (ProcessEnvironment.Set("OPENCODE_API_KEY", " oc_sk_env "))
        {
            using var model = Subscriptions.OpenCodeConsole("glm-5.3", Login(), new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl), api);
            await model.CompleteAsync(OpencodeConsoleWorkspace.Prompt("hi"), CancellationToken.None);
        }

        Assert.NotEmpty(api.Requests);
        foreach (var request in api.Requests)
        {
            Assert.Equal("Bearer oc_sk_env", request.Header("authorization"));
            Assert.Null(request.Header("x-org-id"));
            Assert.Null(request.Header("x-opencode-org-id"));
        }
    }

    private static MemoryCredentialStore Login() => new(new() { ["opencode-console"] = OpencodeConsoleWorkspace.Login });
}
