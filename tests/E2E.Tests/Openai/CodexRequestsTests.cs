// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Openai;

/// <summary>
/// "serves generateText with a forced tool call from a backend that only streams" is not ported: the port's model
/// requests always leave the tool choice to the model. "carries the encrypted reasoning of an earlier turn itself
/// instead of referring to it by id" is not ported: the port's model messages carry no reasoning items.
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
