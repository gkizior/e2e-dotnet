// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Openai;

/// <summary>
/// "carries the encrypted reasoning of an earlier turn itself instead of referring to it by id" is not ported: the port's
/// model messages carry no reasoning items.
/// </summary>
public sealed class CodexRequestsTests
{
    private const string Backend = "https://chatgpt.com/backend-api/codex/responses";

    /// <summary>The port's Codex model sends to the backend itself, so the request already has the backend URL.</summary>
    [Fact]
    public async Task Routes_a_Responses_request_at_the_Codex_backend_with_the_body_the_backend_requires()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Backend)
        {
            Content = new StringContent("""{ "model": "gpt-5.5", "input": [], "max_output_tokens": 100, "include": ["x"] }"""),
        };
        var credentials = new OAuthCredentials { Access = "a", Refresh = "r", Extra = new Dictionary<string, string> { ["accountId"] = "acct", ["residency"] = "eu" } };

        await new CodexProvider(originator: "e2e", apiUrl: Backend).PrepareAsync(request, credentials, CancellationToken.None);

        Assert.Equal(Backend, request.RequestUri!.AbsoluteUri);
        Assert.Equal("acct", request.Headers.GetValues("chatgpt-account-id").Single());
        Assert.Equal("e2e", request.Headers.GetValues("originator").Single());
        Assert.Equal("eu", request.Headers.GetValues("x-openai-internal-codex-residency").Single());
        var expected = JsonNode.Parse("""{ "model": "gpt-5.5", "input": [], "stream": true, "store": false, "instructions": "Follow the user request.", "include": ["x", "reasoning.encrypted_content"] }""");
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(await request.Content!.ReadAsStringAsync())), await request.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Partial: the port's model requests leave the tool choice to the model, so the call is not forced. The model is
    /// built as <c>Subscriptions.ChatGpt</c> builds it, over a fake backend, because that constructor takes no handler.
    /// </summary>
    [Fact]
    public async Task Serves_generateText_with_a_tool_call_from_a_backend_that_only_streams()
    {
        // As the Codex backend answers: items streamed one by one, and an empty output on the final event.
        var stream = string.Concat(
            """event: response.created""" + "\n" + """data: {"type":"response.created","response":{"id":"resp_1"}}""" + "\n\n",
            """event: response.output_item.done""" + "\n" + """data: {"type":"response.output_item.done","output_index":0,"item":{"type":"function_call","id":"fc_1","call_id":"call_1","name":"report","arguments":"{\"color\":\"red\"}","status":"completed"}}""" + "\n\n",
            """event: response.completed""" + "\n" + """data: {"type":"response.completed","response":{"id":"resp_1","object":"response","created_at":1,"status":"completed","model":"gpt-5.5","output":[],"usage":{"input_tokens":10,"output_tokens":5,"total_tokens":15,"input_tokens_details":{"cached_tokens":0},"output_tokens_details":{"reasoning_tokens":0}}}}""" + "\n\n");
        var api = new FakeApi(_ => (System.Net.HttpStatusCode.OK, stream));
        var store = new MemoryCredentialStore(new() { ["openai"] = new OAuthCredentials { Access = "tok", Refresh = "r", Expires = 0, Extra = new Dictionary<string, string> { ["accountId"] = "acct_9" } } });
        using var model = new OpenAiResponsesModel(
            new OpenAiResponsesModelOptions
            {
                Model = "gpt-5.5",
                BaseUrl = CodexProvider.DefaultApiUrl[..^"/responses".Length],
                ApiKey = "oauth",
                PromptCacheHints = false,
            },
            Subscriptions.Client(new CodexProvider(), store, inner: api));
        var report = new ModelTool
        {
            Name = "report",
            Description = "report",
            Parameters = System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object", properties = new { color = new { type = "string" } } }),
        };

        var result = await model.CompleteAsync(new ModelRequest { System = "", Tools = [report], Messages = [new ModelMessage { Role = "user", Content = "What color?" }] }, CancellationToken.None);

        var call = Assert.Single(result.ToolCalls);
        Assert.Equal("report", call.Name);
        Assert.Equal("red", call.Arguments.GetProperty("color").GetString());
        Assert.Equal(10, result.Usage!.InputTokens);
        var seen = api.Requests.Single();
        var body = JsonNode.Parse(seen.Body)!;
        Assert.True(body["stream"]!.GetValue<bool>());
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.Equal("Bearer tok", seen.Header("authorization"));
        Assert.Equal("acct_9", seen.Header("chatgpt-account-id"));
    }

    [Fact]
    public async Task Leaves_other_paths_untouched_apart_from_the_headers()
    {
        using var other = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        await new CodexProvider(originator: "e2e", apiUrl: "https://c/x").PrepareAsync(other, new OAuthCredentials { Access = "a", Refresh = "r" }, CancellationToken.None);
        Assert.Equal("https://api.openai.com/v1/models", other.RequestUri!.AbsoluteUri);
        Assert.Equal("e2e", other.Headers.GetValues("originator").Single());
    }

    [Fact]
    public async Task Lists_the_models_the_backend_serves_hidden_ones_marked_through_the_login()
    {
        var api = new FakeApi(_ => (System.Net.HttpStatusCode.OK, """
            { "models": [
              { "slug": "gpt-5.4-mini", "display_name": "GPT-5.4 mini", "visibility": "hide", "priority": 5 },
              { "slug": "gpt-5.6-luna", "display_name": "GPT-5.6 Luna", "visibility": "list", "priority": 1, "default_reasoning_level": "medium", "supported_reasoning_levels": [{ "effort": "low" }, { "effort": "medium" }, { "effort": "high" }] },
              { "slug": "gpt-6-astra", "display_name": "GPT-6 Astra", "visibility": "list", "priority": 0, "supported_reasoning_levels": ["medium", "xhigh"] },
              { "display_name": "no slug" }
            ] }
            """));
        var store = new MemoryCredentialStore(new() { ["openai"] = new OAuthCredentials { Access = "tok", Refresh = "r", Expires = 0, Extra = new Dictionary<string, string> { ["accountId"] = "acct_9" } } });
        var provider = new CodexProvider(originator: "e2e", apiUrl: "https://backend.test/codex/responses");
        using var http = Subscriptions.Client(provider, store, inner: api);

        var models = await provider.ListModelsAsync(http, CancellationToken.None);

        var seen = Assert.Single(api.Requests);
        Assert.Equal("/codex/models", seen.Uri.AbsolutePath);
        Assert.Matches(@"^\d+\.\d+\.\d+$", System.Web.HttpUtility.ParseQueryString(seen.Uri.Query)["client_version"]);
        Assert.Equal(("acct_9", "e2e"), (seen.Header("chatgpt-account-id"), seen.Header("originator")));
        Assert.Equal(
            [
                ("gpt-6-astra", "GPT-6 Astra", "reasoning medium/xhigh"),
                ("gpt-5.6-luna", "GPT-5.6 Luna", "reasoning low/medium/high (default medium)"),
                ("gpt-5.4-mini", "GPT-5.4 mini", "hidden in Codex"),
            ],
            models.Select(model => (model.Id, model.Name, model.Detail)));
        var error = await Assert.ThrowsAsync<OAuthException>(() => OAuthProviders.ListModelsAsync("openai", new MemoryCredentialStore()));
        Assert.Equal(OAuthException.NotLoggedIn, error.Code);
    }
}
