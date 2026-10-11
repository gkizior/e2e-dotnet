// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentGrammarTools;

// The port's act tools do not close their schemas, so upstream's closed-schema, scroll_to shape, keyboard-only,
// and point verb cases have no port. The act loop reads the first done of a turn; upstream's tool result text
// ("the errorCode you sent ... was ignored", "The step already concluded.") is not sent back, because the step ends.
public sealed class TheGrammarToolsHaveClosedSchemasTests
{
    [Fact]
    public async Task Accepts_a_passed_verdict_beside_an_error_code_and_records_the_pass_without_the_code()
    {
        var passed = await RunAsync(ModelResponses.Done("passed", "done", "ACTION_FAILED"));
        Assert.Null(passed.Error);
        Assert.Equal("done", passed.Summary);

        var failed = await RunAsync(ModelResponses.Done("failed", "done", "ACTION_FAILED"));
        var error = Assert.IsType<AgentException>(failed.Error);
        Assert.Equal("ACTION_FAILED", error.Code);
        Assert.Equal("done", error.Message);

        var blocked = await RunAsync(ModelResponses.Done("blocked", "done", "ENVIRONMENT_UNAVAILABLE"));
        var blockedError = Assert.IsType<AgentException>(blocked.Error);
        Assert.Equal("ENVIRONMENT_UNAVAILABLE", blockedError.Code);
        Assert.True(blockedError.Blocked);
    }

    [Fact]
    public async Task Keeps_the_first_verdict_a_later_complete_step_in_the_same_turn_cannot_turn_a_failure_into_a_pass()
    {
        var turn = new ModelResponse
        {
            ToolCalls =
            [
                ModelResponses.Done("failed", "total is wrong", "ASSERTION_FAILED").ToolCalls[0],
                ModelResponses.Done("passed", "looks fine").ToolCalls[0],
            ],
        };

        var result = await RunAsync(turn);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.Equal("total is wrong", error.Message);
    }

    [Fact]
    public async Task Refuses_a_blocked_verdict_without_a_blocking_error_code_and_leaves_the_step_open_for_a_real_one()
    {
        var model = new ScriptedModel(request => request.Messages.Count(message => message.Role == "tool") switch
        {
            0 => ModelResponses.Done("blocked", "could not tell"),
            1 => ModelResponses.Done("blocked", "the total is wrong", "ASSERTION_FAILED"),
            2 => ModelResponses.Done("blocked", "too slow", "STEP_TIMEOUT"),
            _ => ModelResponses.Done("blocked", "no login", "AUTH_CREDENTIAL_UNAVAILABLE"),
        });

        var result = await RunAsync(model);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("AUTH_CREDENTIAL_UNAVAILABLE", error.Code);
        Assert.Equal("no login", error.Message);
        Assert.True(error.Blocked);
        Assert.Equal(4, model.CallCount);
        var rejections = model.Requests[3].Messages.Where(message => message.Role == "tool").Select(message => message.Content).Take(3).ToArray();
        Assert.Equal(3, rejections.Length);
        Assert.All(rejections, content => Assert.StartsWith("failed: a blocked verdict requires a code naming what blocked you", content, StringComparison.Ordinal));
    }

    private static Task<Outcome> RunAsync(ModelResponse response) => RunAsync(new ScriptedModel(_ => response));

    private static async Task<Outcome> RunAsync(ScriptedModel model)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            TestTitle = "verdicts",
        });
        await session.App.OpenAsync("/settings/billing");
        try
        {
            var result = await session.Agent.ActAsync("upgrade");
            session.Complete();
            return new Outcome(null, result.Summary);
        }
        catch (AgentException error)
        {
            session.Complete(error);
            return new Outcome(error, null);
        }
    }

    private sealed record Outcome(AgentException? Error, string? Summary);
}
