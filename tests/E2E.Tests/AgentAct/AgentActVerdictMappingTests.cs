// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentAct;

public sealed class AgentActVerdictMappingTests
{
    // Upstream also checks the configuration exit code and the blocked report status. Those belong to its runner,
    // which this port does not have: a blocked verdict is marked Blocked, and the caller maps that.
    [Fact]
    public async Task Maps_a_blocked_verdict_onto_its_error_code()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync("log in with the staging account");
            },
            CounterPage.World(),
            new ScriptedModel(_ => ModelResponses.Done("blocked", "the staging credential is not configured", "AUTH_CREDENTIAL_UNAVAILABLE")));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("AUTH_CREDENTIAL_UNAVAILABLE", error.Code);
        Assert.True(error.Blocked);
        Assert.Equal("the staging credential is not configured", error.Message);
    }

    [Fact]
    public async Task Fails_closed_when_the_model_claims_success_over_an_exhausted_action_budget()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("exhausted its action budget of 2", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "all good, nothing to see")
                : ModelResponses.Tap("button", "Increment");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync("keep clicking forever", new ActOptions { MaxSteps = 2 });
            },
            CounterPage.World(),
            model);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.True(error.Blocked);
        Assert.Equal(error.Message, error.Explanation);
        Assert.Equal(4, model.CallCount);
    }

    [Fact]
    public async Task Cuts_a_hanging_model_at_the_step_timeout()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync("do something eventually", new ActOptions { Timeout = TimeSpan.FromMilliseconds(500) });
            },
            CounterPage.World(),
            new HangingModel());

        Assert.Equal("STEP_TIMEOUT", Assert.IsType<AgentException>(result.Error).Code);
        // The step settled at its own 500 ms deadline, not the test timeout.
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4), "settled after " + watch.Elapsed);
    }

    [Fact]
    public async Task Hard_stops_a_model_that_overspends_the_model_call_budget()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync("think very hard", new ActOptions { MaxModelCalls = 2 });
            },
            CounterPage.World(),
            new ScriptedModel(_ => new ModelResponse { Content = "thinking" }));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.True(error.Blocked);
        Assert.Equal("agent.act exhausted its model-call budget of 2", error.Message);
        Assert.Equal(2, result.ModelCalls);
    }

    [Fact]
    public async Task Stamps_the_budget_code_onto_a_code_less_failed_verdict_after_a_hard_stop()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("exhausted its action budget of 1", StringComparison.Ordinal)
                ? ModelResponses.Done("failed", "the counter looked wrong")
                : ModelResponses.Tap("button", "Increment");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync("tap twice", new ActOptions { MaxSteps = 1 });
            },
            CounterPage.World(),
            model);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.True(error.Blocked);
        Assert.Contains("the counter looked wrong", error.Message, StringComparison.Ordinal);
        Assert.Equal(error.Message, error.Explanation);
        Assert.Equal(3, model.CallCount);
    }

    // Upstream's generic secret in a plain textbox and its provider-backed secret are not ported: the port has no
    // secret purpose check for a plain field and no credential provider functions.
    [Fact]
    public async Task Fills_a_declared_secret_through_fill_secret_and_refuses_an_undeclared_one()
    {
        string? typed = null;
        var world = new DocumentWorld().Map("/", page => page.Roots.Add(new DocumentElement
        {
            Role = "textbox",
            Name = "Password",
            Secure = true,
            InputPurpose = "password",
            OnFill = value => typed = value,
        }));
        var model = new ScriptedModel(request => request.Messages.Count(message => message.Role == "tool") switch
        {
            0 => ModelResponses.Call("fill_secret", new { role = "textbox", name = "Password", secret = "admin.password" }),
            // A configured secret the step never declared is refused.
            1 => ModelResponses.Call("fill_secret", new { role = "textbox", name = "Password", secret = "stripe-key" }),
            _ => ModelResponses.Done("passed", "filled the declared secret only"),
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.ActAsync("sign in with the given credentials", new ActOptions
                {
                    Params = new Dictionary<string, object?>
                    {
                        ["password"] = Secret.Create("admin.password", "admin-pass-1", "member password"),
                    },
                });
            },
            world,
            model);

        Assert.Null(result.Error);
        Assert.Equal("admin-pass-1", typed);
        Assert.DoesNotContain("admin-pass-1", CounterPage.Everything(model), StringComparison.Ordinal);
        var results = CounterPage.ToolResults(model);
        Assert.Contains("<secret:admin.password>", results[0], StringComparison.Ordinal);
        Assert.Contains("No secret named 'stripe-key'", results[1], StringComparison.Ordinal);
    }

    private sealed class HangingModel : IAgentModel
    {
        public string Name => "hanging";

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new ModelResponse();
        }
    }
}
