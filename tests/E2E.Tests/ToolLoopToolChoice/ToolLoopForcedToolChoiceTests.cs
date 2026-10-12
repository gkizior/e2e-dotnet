// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.ToolLoopToolChoice;

/// <summary>
/// The port always sends <c>tool_choice: auto</c> and ends a step with a <c>done</c> tool, so it has no forced choice to
/// refuse and no <c>complete_step</c> tool. Not ported here: "retries with auto and a tool-calls-only rule when the model
/// rejects a forced choice", "remembers the refusal for later steps on the same model", "offers only complete_step under
/// auto on the forced-conclusion turns", the four "downgraded tool choice" cases, "sends the call back as an error and asks
/// again on the same turn budget", "isForcedToolChoiceRejected" and "isForcedToolCallSkipped" (no forced choice, no AI SDK
/// warnings or errors), and "fails with STEP_NO_CONCLUSION when the model never calls complete_step" (the port ends on the
/// call budget with <c>STEP_BUDGET_EXHAUSTED</c>, not <c>STEP_NO_CONCLUSION</c>), and "leaves a 400 about anything else to the ordinary error translation" (the port has no forced-choice refusal to tell it from, and a scripted model would only test itself).
/// </summary>
public sealed class ToolLoopForcedToolChoiceTests
{
    private static DocumentWorld Page() => new DocumentWorld().Map("/start", page => page.Button("Peek"));

    [Fact]
    public async Task Asks_again_when_a_turn_comes_back_as_prose_without_a_tool_call()
    {
        var seen = new List<(int Count, string Role, string? Content)>();
        var model = new ScriptedModel(request =>
        {
            seen.Add((request.Messages.Count, request.Messages[^1].Role, request.Messages[^1].Content));
            return seen.Count == 1 ? new ModelResponse { Content = "I will look around first." } : ModelResponses.Done("passed", "Done.");
        });
        ActResult? result = null;

        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/start");
                result = await ctx.Agent.ActAsync("do the thing");
            },
            Page(),
            model);

        Assert.Null(attempt.Error);
        Assert.Equal(2, model.CallCount);
        Assert.Equal(("user", "Call a tool. End the step with done."), (seen[1].Role, seen[1].Content));
        Assert.Equal(seen[0].Count + 2, seen[1].Count);
        // The prose turn is one the step paid for.
        Assert.Equal(2, result!.ModelCalls);
    }
}
