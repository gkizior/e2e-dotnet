// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text.Json.Nodes;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Copilot;

/// <summary>"serves generateText over the chat protocol with the stored GitHub token" is not ported: the port's model messages carry no image parts.</summary>
public sealed class CopilotRequestsTests
{
    [Fact]
    public async Task Marks_agent_turns_and_image_requests_and_routes_an_enterprise_login_at_its_host()
    {
        var provider = new CopilotProvider();
        using var withImage = new HttpRequestMessage(HttpMethod.Post, CopilotProvider.ApiUrl + "/chat/completions")
        {
            Content = new StringContent("""{ "messages": [{ "role": "user", "content": [{ "type": "text", "text": "x" }, { "type": "image_url", "image_url": { "url": "data:image/png;base64,AA==" } }] }, { "role": "assistant", "content": "ok" }] }"""),
        };
        await provider.PrepareAsync(withImage, new OAuthCredentials { Access = "a", Extra = new Dictionary<string, string> { ["enterpriseUrl"] = "gh.acme.com" } }, CancellationToken.None);
        Assert.Equal("https://copilot-api.gh.acme.com/chat/completions", withImage.RequestUri!.AbsoluteUri);
        Assert.Equal("agent", withImage.Headers.GetValues("x-initiator").Single());
        Assert.Equal("true", withImage.Headers.GetValues("copilot-vision-request").Single());
        Assert.Equal("conversation-edits", withImage.Headers.GetValues("openai-intent").Single());

        using var plain = new HttpRequestMessage(HttpMethod.Post, CopilotProvider.ApiUrl + "/chat/completions")
        {
            Content = new StringContent("""{ "messages": [{ "role": "user", "content": "hi" }] }"""),
        };
        await provider.PrepareAsync(plain, new OAuthCredentials { Access = "a" }, CancellationToken.None);
        Assert.Equal("https://api.githubcopilot.com/chat/completions", plain.RequestUri!.AbsoluteUri);
        Assert.Equal("user", plain.Headers.GetValues("x-initiator").Single());
        Assert.False(plain.Headers.Contains("copilot-vision-request"));
    }

    /// <summary>The port's model names no provider, so the check that the delegate switches from chat to Responses is not ported.</summary>
    [Fact]
    public async Task Calls_a_model_Copilot_lists_only_over_Responses_through_the_Responses_API_with_no_server_storage()
    {
        var api = new FakeApi(request => request.Uri.AbsolutePath == "/models"
            ? (HttpStatusCode.OK, """{ "data": [{ "id": "gpt-6-luna", "vendor": "OpenAI", "supported_endpoints": ["/responses", "ws:/responses"], "capabilities": { "type": "chat" } }] }""")
            : (HttpStatusCode.OK, """
                { "id": "resp_1", "created_at": 1, "model": "gpt-6-luna",
                  "output": [{ "type": "message", "id": "msg_1", "role": "assistant", "content": [{ "type": "output_text", "text": "luna", "annotations": [] }] }],
                  "usage": { "input_tokens": 7, "output_tokens": 3, "total_tokens": 10 } }
                """));
        var store = new MemoryCredentialStore(new() { ["github-copilot"] = new OAuthCredentials { Access = "gho_x", Refresh = "", Expires = 0 } });
        using var model = new CopilotModel("gpt-6-luna", Subscriptions.Client(new CopilotProvider(), store, inner: api));

        var result = await model.CompleteAsync(new ModelRequest { System = "", Tools = [], Messages = [new ModelMessage { Role = "user", Content = "color?" }] }, CancellationToken.None);

        Assert.Equal("luna", result.Content);
        var seen = api.Requests.Last();
        Assert.Equal("/responses", seen.Uri.AbsolutePath);
        Assert.Equal("Bearer gho_x", seen.Header("authorization"));
        var body = JsonNode.Parse(seen.Body)!.AsObject();
        Assert.IsType<JsonArray>(body["input"]);
        Assert.False(body.ContainsKey("messages"));
        Assert.False(body["store"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Marks_agent_turns_and_image_requests_from_a_Responses_body_too()
    {
        var provider = new CopilotProvider();
        using var withImage = new HttpRequestMessage(HttpMethod.Post, CopilotProvider.ApiUrl + "/responses")
        {
            Content = new StringContent("""{ "model": "gpt-6-luna", "input": [{ "role": "user", "content": [{ "type": "input_text", "text": "x" }, { "type": "input_image", "image_url": "data:image/png;base64,AA==" }] }, { "type": "function_call_output", "call_id": "call_1", "output": "ok" }] }"""),
        };
        await provider.PrepareAsync(withImage, new OAuthCredentials { Access = "a", Extra = new Dictionary<string, string> { ["enterpriseUrl"] = "gh.acme.com" } }, CancellationToken.None);
        Assert.Equal("https://copilot-api.gh.acme.com/responses", withImage.RequestUri!.AbsoluteUri);
        Assert.Equal("agent", withImage.Headers.GetValues("x-initiator").Single());
        Assert.Equal("true", withImage.Headers.GetValues("copilot-vision-request").Single());
        Assert.Equal("conversation-edits", withImage.Headers.GetValues("openai-intent").Single());

        using var plain = new HttpRequestMessage(HttpMethod.Post, CopilotProvider.ApiUrl + "/responses")
        {
            Content = new StringContent("""{ "model": "gpt-6-luna", "input": [{ "role": "user", "content": [{ "type": "input_text", "text": "hi" }] }] }"""),
        };
        await provider.PrepareAsync(plain, new OAuthCredentials { Access = "a" }, CancellationToken.None);
        Assert.Equal("user", plain.Headers.GetValues("x-initiator").Single());
        Assert.False(plain.Headers.Contains("copilot-vision-request"));
    }

    [Fact]
    public async Task Names_the_login_command_when_GitHub_rejects_the_stored_token_which_has_nothing_to_refresh_it()
    {
        var api = new FakeApi(_ => (HttpStatusCode.Unauthorized, """{ "message": "Bad credentials" }"""));
        var store = new MemoryCredentialStore(new() { ["github-copilot"] = new OAuthCredentials { Access = "gho_revoked", Refresh = "", Expires = 0 } });
        using var model = new CopilotModel("gpt-4.1", Subscriptions.Client(new CopilotProvider(), store, "run `npx e2e login github-copilot`", api));

        // The port's model wraps the login error in its provider failure.
        var failure = await Assert.ThrowsAsync<AgentException>(() => model.CompleteAsync(new ModelRequest { System = "", Tools = [], Messages = [new ModelMessage { Role = "user", Content = "color?" }] }, CancellationToken.None));
        var error = Assert.IsType<OAuthException>(failure.InnerException);

        Assert.Equal(OAuthException.LoginRequired, error.Code);
        Assert.Equal("GitHub Copilot rejected the stored token (401: Bad credentials); run `npx e2e login github-copilot`", error.Message);

        // The model listing meets the rejection first, so the call itself is never sent.
        Assert.Equal(["/models"], api.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Lists_the_chat_models_of_the_plan_through_the_login_leaving_embeddings_out_and_marking_what_copilot_cannot_use()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, """
            { "data": [
              { "id": "claude-sonnet-5", "name": "Claude Sonnet 5", "vendor": "Anthropic", "capabilities": { "type": "chat", "supports": { "tool_calls": true, "vision": true } } },
              { "id": "text-embedding-3-small", "name": "Embedding", "vendor": "Azure OpenAI", "capabilities": { "type": "embeddings" } },
              { "id": "gpt-5.6-luna", "name": "GPT-5.6 Luna", "vendor": "OpenAI", "preview": true, "capabilities": { "type": "chat", "supports": { "tool_calls": true } } },
              { "id": "claude-haiku-4.5", "vendor": "Anthropic", "policy": { "state": "enabled" }, "supported_endpoints": ["/chat/completions", "/v1/messages"], "capabilities": { "type": "chat" } },
              { "id": "claude-opus-5", "vendor": "Anthropic", "policy": { "state": "disabled" }, "supported_endpoints": ["/v1/messages", "/chat/completions"], "capabilities": { "type": "chat" } },
              { "id": "claude-fable-5.1", "vendor": "Anthropic", "policy": { "state": "unconfigured" }, "capabilities": { "type": "chat" } },
              { "id": "claude-messages", "vendor": "Anthropic", "supported_endpoints": ["/v1/messages"], "capabilities": { "type": "chat" } },
              { "id": "gpt-6-luna", "vendor": "OpenAI", "policy": { "state": "enabled" }, "supported_endpoints": ["/responses", "ws:/responses"], "capabilities": { "type": "chat" } },
              { "id": "gpt-5.5", "vendor": "OpenAI", "policy": { "state": "disabled" }, "supported_endpoints": ["/responses"], "capabilities": { "type": "chat" } }
            ] }
            """));
        var provider = new CopilotProvider();
        var store = new MemoryCredentialStore(new() { ["github-copilot"] = new OAuthCredentials { Access = "gho_x", Refresh = "", Expires = 0 } });
        using var http = Subscriptions.Client(provider, store, inner: api);

        var models = await provider.ListModelsAsync(http, CancellationToken.None);

        var seen = Assert.Single(api.Requests);
        Assert.Equal("/models", seen.Uri.AbsolutePath);
        Assert.Equal("conversation-edits", seen.Header("openai-intent"));
        Assert.Equal(
            [
                ("claude-sonnet-5", "Claude Sonnet 5", "Anthropic, tools, vision"),
                ("gpt-5.6-luna", "GPT-5.6 Luna", "OpenAI, tools, preview"),
                ("claude-haiku-4.5", null, "Anthropic"),
                ("claude-opus-5", null, "Anthropic, not enabled"),
                ("claude-fable-5.1", null, "Anthropic, not enabled"),
                ("claude-messages", null, "Anthropic, no chat or responses"),
                ("gpt-6-luna", null, "OpenAI"),
                ("gpt-5.5", null, "OpenAI, not enabled"),
            ],
            models.Select(model => (model.Id, model.Name, model.Detail)));
    }
}
