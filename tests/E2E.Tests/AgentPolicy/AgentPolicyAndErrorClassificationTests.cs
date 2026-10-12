// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Tests.AgentAct;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentPolicy;

public sealed class AgentPolicyAndErrorClassificationTests
{
    // Upstream also checks the error category, which only its report carries.
    [Fact]
    public async Task Reports_an_unconfigured_credential_with_AUTH_CREDENTIAL_UNAVAILABLE()
    {
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/");
            await ctx.Screen.GetByLabel("Email").FillAsync(Credentials.User("ghost").Password);
        }, CounterPage.World());

        Assert.Equal("AUTH_CREDENTIAL_UNAVAILABLE", Assert.IsType<TestException>(result.Error).Code);
    }

    [Fact]
    public async Task Spends_one_repair_call_to_satisfy_the_caller_schema()
    {
        // The first answer is a bare value. The repair round wraps it.
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return ModelResponses.Call("extract", new { data = text.Contains("<previous-attempt-rejected>", StringComparison.Ordinal) ? (object)new { counter = "0" } : "0" });
        });
        CounterReading? reading = null;
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/");
            reading = await ctx.Agent.ExtractAsync<CounterReading>("the counter value");
        }, CounterPage.World(), model);

        Assert.Null(result.Error);
        Assert.Equal("0", reading?.Counter);
        Assert.Equal(2, model.CallCount);
        Assert.Equal(2, result.ModelCalls);
    }

    private sealed class CounterReading
    {
        public required string Counter { get; init; }
    }
}
