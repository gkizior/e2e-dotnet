// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Tests.OpenaiSecondTurn;

public sealed class SecondTurnOfAnOpenAiShapedStepTests
{
    private const string FirstTurn = """
        {
          "output": [
            { "type": "reasoning", "id": "rs_1", "encrypted_content": "ENCRYPTED-1", "summary": [] },
            { "type": "function_call", "id": "fc_1", "call_id": "call_1", "name": "tap", "arguments": "{\"role\":\"button\",\"name\":\"Upgrade to Pro\"}", "status": "completed" }
          ],
          "usage": { "input_tokens": 3, "output_tokens": 1 }
        }
        """;

    private const string SecondTurn = """
        {
          "output": [
            { "type": "function_call", "id": "fc_2", "call_id": "call_2", "name": "done", "arguments": "{\"status\":\"passed\",\"summary\":\"upgraded\"}", "status": "completed" }
          ],
          "usage": { "input_tokens": 3, "output_tokens": 1 }
        }
        """;

    [Fact]
    public async Task Replays_the_first_turn_inline_encrypted_reasoning_and_the_tool_exchange_no_item_reference()
    {
        var handler = new RecordingHandler(FirstTurn, SecondTurn);
        using var http = new HttpClient(handler);
        using var model = new OpenAiResponsesModel(new OpenAiResponsesModelOptions { Model = "gpt-6-luna", ApiKey = "test-key", BaseUrl = "https://llm.test/v1" }, http);
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            CacheEnabled = false,
            TestTitle = "openai second turn > case",
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        await session.Context.App.OpenAsync("/settings/billing");
        await session.Context.Agent.ActAsync("upgrade the workspace to the Pro plan");
        session.Complete(null);

        Assert.Equal(2, handler.Requests.Count);
        var first = handler.Requests[0].Json();
        var second = handler.Requests[1].Json();
        Assert.False(first["store"]!.GetValue<bool>());
        Assert.Contains("reasoning.encrypted_content", first["include"]!.AsArray().Select(item => item!.GetValue<string>()));
        Assert.StartsWith("e2e-", first["prompt_cache_key"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.False(second["store"]!.GetValue<bool>());
        var items = second["input"]!.AsArray().OfType<JsonObject>().Where(item => item.ContainsKey("type")).ToList();
        Assert.Equal(["reasoning", "function_call", "function_call_output"], items.Select(item => item["type"]!.GetValue<string>()));
        Assert.Equal("ENCRYPTED-1", items[0]["encrypted_content"]!.GetValue<string>());
        Assert.Equal("call_1", items[1]["call_id"]!.GetValue<string>());
        Assert.Equal("tap", items[1]["name"]!.GetValue<string>());
        Assert.Equal("call_1", items[2]["call_id"]!.GetValue<string>());
    }
}
