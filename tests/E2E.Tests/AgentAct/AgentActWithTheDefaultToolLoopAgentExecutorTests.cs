// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentAct;

public sealed class AgentActWithTheDefaultToolLoopAgentExecutorTests
{
    private const string Instruction = "increment the counter once and verify it shows 1";

    [Fact]
    public async Task Drives_the_step_through_the_tool_loop_to_a_passed_verdict()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync(Instruction);
                await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("1");
            },
            CounterPage.World(),
            IncrementThenConclude());

        Assert.Null(result.Error);
    }

    // Upstream lists a toolset the port does not have (hover, drag, upload, and the pixel verbs), and its tool
    // results report changes since a revision. The port's results carry the whole screen instead.
    [Fact]
    public async Task Offers_the_port_toolset_and_concludes_through_done()
    {
        var model = IncrementThenConclude();
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync(Instruction);
            },
            CounterPage.World(),
            model);

        Assert.Null(result.Error);
        Assert.True(model.CallCount >= 2);
        Assert.Equal(
            ["back", "check", "clear", "done", "double_tap", "fill", "fill_secret", "navigate", "observe", "press", "scroll", "scroll_to", "select", "tap", "uncheck"],
            model.Requests[0].Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        var opening = model.Requests[0].Messages[0].Content!;
        Assert.Contains("Goal: " + Instruction, opening, StringComparison.Ordinal);
        Assert.Contains("button \"Increment\"", opening, StringComparison.Ordinal);
        // The tap result shows the screen it produced, not the tree the model already had.
        Assert.Contains("status \"Counter\" text=\"1\"", CounterPage.ToolResults(model)[0], StringComparison.Ordinal);
    }

    // Upstream also checks the step's provenance (provider, model, and the model event of each call), which belong to
    // its report. The port has no report.
    [Fact]
    public async Task Accounts_model_calls_and_tokens_in_the_step_metrics()
    {
        ActResult? act = null;
        ActResult? replayed = null;
        var directory = TempCache();
        var model = new ScriptedModel(request => new ModelResponse
        {
            ToolCalls = request.Messages.Any(message => message.Content?.Contains("Statement:", StringComparison.Ordinal) == true) || request.Messages.Any(message => message.Role == "tool") ? ModelResponses.Done("passed", "upgraded").ToolCalls : ModelResponses.Tap("button", "Upgrade to Pro").ToolCalls,
            Usage = new ModelUsage { InputTokens = 11, OutputTokens = 3 },
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                act = await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
                await ctx.Agent.AssertAsync("the invoice preview shows a prorated amount");
            },
            model: model,
            cacheDirectory: directory);
        var second = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                replayed = await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
                await ctx.Agent.AssertAsync("the invoice preview shows a prorated amount");
            },
            model: model,
            cacheDirectory: directory);

        Assert.Null(result.Error);
        Assert.Null(second.Error);
        Assert.Equal(0, replayed!.ModelCalls);
        Assert.Equal(1, replayed.Actions);
        Assert.True(act!.ModelCalls >= 2);
        Assert.Equal(1, act.Actions);
        // The session also counts the one judgment of the assertion.
        Assert.Equal(act.ModelCalls + 1, result.ModelCalls);
        Assert.Equal(11 * result.ModelCalls, result.InputTokens);
        Assert.Equal(3 * result.ModelCalls, result.OutputTokens);
    }

    private static ScriptedModel IncrementThenConclude() => new(IncrementThenConcludeTurn);

    private static ModelResponse IncrementThenConcludeTurn(ModelRequest request)
    {
        return request.Messages.Any(message => message.Role == "tool")
            ? ModelResponses.Done("passed", "tapped Increment and the counter shows 1")
            : ModelResponses.Tap("button", "Increment");
    }
}
