// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.ModelRequestHeaders;

public sealed class ModelRequestHeadersTests
{
    /// <summary>A chat completion that calls <paramref name="tool"/> with <paramref name="arguments"/>.</summary>
    private static string Completion(string tool, string arguments)
    {
        return $$"""
            {
              "choices": [{
                "message": {
                  "content": null,
                  "tool_calls": [{ "id": "call_{{tool}}", "type": "function", "function": { "name": "{{tool}}", "arguments": {{System.Text.Json.JsonSerializer.Serialize(arguments)}} } }]
                },
                "finish_reason": "tool_calls"
              }],
              "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
            }
            """;
    }

    internal static void AssertIdentified(RecordedRequest request)
    {
        Assert.Matches(new Regex(@"^e2e-dotnet/\S+ \(\S+; \S+\)$"), ModelHttp.UserAgent);
        Assert.StartsWith(ModelHttp.UserAgent + " ", request.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Contains(" runtime/dotnet/", request.Headers["User-Agent"], StringComparison.Ordinal);
        Assert.Equal("https://github.com/hardkoded/e2e-dotnet", request.Headers["HTTP-Referer"]);
        Assert.Equal("e2e-dotnet", request.Headers["X-Title"]);
    }

    [Fact]
    // The port has no AI SDK; the .NET runtime token takes the place of its user agent.
    public async Task Identifies_e2e_on_a_judgment_call_ahead_of_the_AI_SDK_user_agent()
    {
        var handler = new RecordingHandler(Completion("done", """{"status":"passed","summary":"holds"}"""));

        await RunAsync(handler, ctx => ctx.Agent.AssertAsync("the plan is Free"));

        AssertIdentified(Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task Identifies_e2e_on_every_turn_of_an_act_loop()
    {
        var handler = new RecordingHandler(
            Completion("tap", """{"role":"button","name":"Upgrade to Pro"}"""),
            Completion("done", """{"status":"passed","summary":"upgraded"}"""));

        ActResult? result = null;
        await RunAsync(handler, async ctx => result = await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan"));

        Assert.Equal("upgraded", result!.Summary);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, AssertIdentified);
    }

    private static async Task RunAsync(RecordingHandler handler, Func<TestContext, Task> step)
    {
        using var http = new HttpClient(handler);
        using var model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "scripted", ApiKey = "k", BaseUrl = "https://llm.test/v1" }, http);
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            CacheEnabled = false,
            TestTitle = "model request headers > case",
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });

        await session.Context.App.OpenAsync("/settings/billing");
        await step(session.Context);
        session.Complete(null);
    }
}
