// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Tests.AgentAct;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentPolicy;

public sealed class ToolCallsOutsideTheVocabularyTests
{
    // Upstream also reads the verdict turn from its report, with the note that the code was ignored.
    [Fact]
    public async Task Drops_the_error_code_a_passed_verdict_carries_and_ends_the_step_on_that_call_with_no_repair_turn()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "the counter shows 1", "ACTION_FAILED")
                : ModelResponses.Tap("button", "Increment");
        });
        ActResult? act = null;
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/");
            act = await ctx.Agent.ActAsync("increment the counter once");
            await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("1");
        }, CounterPage.World(), model);

        // One model sends a pass beside ACTION_FAILED about half the time. The code is dropped, never refused.
        Assert.Null(result.Error);
        Assert.Equal(2, model.CallCount);
        Assert.Equal(("the counter shows 1", 2, 1), (act?.Summary, act?.ModelCalls, act?.Actions));
    }

    // Upstream also checks that the refusal lists the available tools and that the report records the turn. The port's refusal names the tool only.
    [Fact]
    public async Task Refuses_a_tool_the_step_does_not_offer_and_runs_nothing()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "the counter shows 1");
            }

            return text.Contains("Unknown tool click", StringComparison.Ordinal)
                ? ModelResponses.Tap("button", "Increment")
                : ModelResponses.Call("click", new { role = "button", name = "Increment" });
        });
        ActResult? act = null;
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/");
            act = await ctx.Agent.ActAsync("increment the counter once");
            await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("1");
        }, CounterPage.World(), model);

        Assert.Null(result.Error);
        Assert.Equal(3, model.CallCount);
        Assert.Equal(1, act?.Actions);
        Assert.Equal("failed: Unknown tool click.", CounterPage.ToolResults(model)[0]);
    }
}
