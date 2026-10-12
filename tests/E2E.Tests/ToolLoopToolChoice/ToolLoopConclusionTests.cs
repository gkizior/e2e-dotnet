// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.ToolLoopToolChoice;

/// <summary>Tool loop conclusion: the first verdict of a turn ends the step.</summary>
public sealed class ToolLoopConclusionTests
{
    [Fact]
    public async Task Ends_the_step_on_the_first_verdict_of_a_turn_that_sends_two()
    {
        var failed = ModelResponses.Done("failed", "the total is wrong", "ASSERTION_FAILED").ToolCalls[0];
        var passed = ModelResponses.Done("passed", "All done.").ToolCalls[0];
        var model = new ScriptedModel(_ => new ModelResponse { ToolCalls = [failed, passed] });

        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/start");
                await ctx.Agent.ActAsync("check the total");
            },
            new DocumentWorld().Map("/start", page => page.Button("Peek")),
            model);

        Assert.Equal("ASSERTION_FAILED", Assert.IsType<AgentException>(attempt.Error).Code);
        Assert.Equal(1, model.CallCount);
    }
}
