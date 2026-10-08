// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Web;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Xai;

[Collection(InstantDeviceFlow.Name)]
public sealed class SpaceXaiLoginTests
{
    [Fact]
    public async Task Runs_the_RFC_8628_device_flow_against_auth_x_ai_and_reads_expiry_from_the_JWT()
    {
        var exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 900;
        var token = FakeJwt.Of("{\"exp\":" + exp.ToString(CultureInfo.InvariantCulture) + "}");
        var polls = 0;
        var issuer = new FakeApi(request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            if (request.Uri.AbsolutePath == "/oauth2/device/code")
            {
                Assert.Equal("b1a00492-073a-47ea-816f-4c329264a828", form["client_id"]);
                Assert.Contains("grok-cli:access", form["scope"], StringComparison.Ordinal);
                Assert.Equal("my-tool", form["referrer"]);
                return (HttpStatusCode.OK, """{ "device_code": "dc", "user_code": "ABCD-EFGH", "verification_uri": "https://accounts.x.ai/oauth2/device", "verification_uri_complete": "https://accounts.x.ai/oauth2/device?user_code=ABCD-EFGH", "expires_in": 1800, "interval": 0.001 }""");
            }

            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
            polls++;

            // SpaceXAI answers pending with 400 and an error field.
            return polls switch
            {
                1 => (HttpStatusCode.BadRequest, """{ "error": "authorization_pending", "error_description": "User has not yet authorized" }"""),
                2 => (HttpStatusCode.BadRequest, """{ "error": "slow_down" }"""),
                _ => (HttpStatusCode.OK, "{ \"access_token\": \"" + token + "\", \"refresh_token\": \"rt-1\", \"expires_in\": 900 }"),
            };
        });
        var provider = new XaiProvider(new HttpClient(issuer), referrer: "my-tool", issuer: "https://auth.test");
        OAuthAuthInfo? shown = null;

        var credentials = await provider.LoginAsync(
            new OAuthLoginCallbacks { OnAuth = info => shown = info, OnPrompt = (_, _) => Task.FromResult("") },
            new OAuthLoginOptions(),
            CancellationToken.None);

        Assert.Equal(("https://accounts.x.ai/oauth2/device?user_code=ABCD-EFGH", "ABCD-EFGH"), (shown!.Url, shown.UserCode));
        Assert.Equal((token, "rt-1", exp * 1000), (credentials.Access, credentials.Refresh, credentials.Expires));
    }

    [Fact]
    public async Task Refreshes_with_rotation_and_keeps_the_old_refresh_token_when_none_is_returned()
    {
        var rotate = true;
        var issuer = new FakeApi(request =>
        {
            var form = HttpUtility.ParseQueryString(request.Body);
            Assert.Equal("refresh_token", form["grant_type"]);
            Assert.Equal(rotate ? "rt-1" : "rt-2", form["refresh_token"]);
            return (HttpStatusCode.OK, rotate ? """{ "access_token": "a2", "refresh_token": "rt-2", "expires_in": 60 }""" : """{ "access_token": "a3", "expires_in": 60 }""");
        });
        var provider = new XaiProvider(new HttpClient(issuer), issuer: "https://auth.test");
        var second = await provider.RefreshAsync(new OAuthCredentials { Access = "a1", Refresh = "rt-1", Expires = 0 }, CancellationToken.None);
        Assert.Equal(("a2", "rt-2"), (second.Access, second.Refresh));
        rotate = false;
        var third = await provider.RefreshAsync(second, CancellationToken.None);
        Assert.Equal(("a3", "rt-2"), (third.Access, third.Refresh));
    }

    /// <summary>The port's model names no provider, and its user agent names the port's product, e2e-dotnet.</summary>
    [Fact]
    public async Task Serves_generateText_with_the_bearer_token_against_the_SpaceXAI_API()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, """{ "id": "c1", "object": "chat.completion", "created": 1, "model": "grok-4", "choices": [{ "index": 0, "finish_reason": "stop", "message": { "role": "assistant", "content": "hello" } }], "usage": { "prompt_tokens": 3, "completion_tokens": 1, "total_tokens": 4 } }"""));
        var store = new MemoryCredentialStore(new() { ["spacexai"] = new OAuthCredentials { Access = "xai-tok", Refresh = "rt", Expires = 0 } });
        using var model = new OpenAiCompatibleModel(
            new OpenAiCompatibleModelOptions { Model = "grok-4", BaseUrl = XaiProvider.ApiUrl, ApiKey = "oauth" },
            Subscriptions.Client(new XaiProvider(), store, inner: api));

        var result = await model.CompleteAsync(new ModelRequest { System = "", Tools = [], Messages = [new ModelMessage { Role = "user", Content = "hi" }] }, CancellationToken.None);

        Assert.Equal("hello", result.Content);
        var seen = api.Requests.Last();
        Assert.Equal("Bearer xai-tok", seen.Header("authorization"));
        Assert.Matches(@"^e2e-dotnet/\d+\.\d+\.\d+\S* \(\w+; \w+\)$", seen.Header("user-agent"));
    }

    [Fact]
    public async Task Lists_the_language_models_the_token_reaches_with_aliases_and_vision()
    {
        var api = new FakeApi(_ => (HttpStatusCode.OK, """{ "models": [{ "id": "grok-4", "aliases": ["grok-4-latest"], "input_modalities": ["text", "image"] }, { "id": "grok-4-fast", "aliases": [], "input_modalities": ["text"] }] }"""));
        var store = new MemoryCredentialStore(new() { ["spacexai"] = new OAuthCredentials { Access = "xai-tok", Refresh = "rt", Expires = 0 } });
        var provider = new XaiProvider(modelsUrl: "https://api.test/v1/language-models");
        using var http = Subscriptions.Client(provider, store, inner: api);

        var models = await provider.ListModelsAsync(http, CancellationToken.None);

        Assert.Equal("Bearer xai-tok", Assert.Single(api.Requests).Header("authorization"));
        Assert.Equal([("grok-4", (string?)"vision; also grok-4-latest"), ("grok-4-fast", null)], models.Select(model => (model.Id, model.Detail)));
    }
}
