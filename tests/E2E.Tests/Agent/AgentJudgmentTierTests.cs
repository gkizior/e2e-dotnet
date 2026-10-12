// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using E2E.Tests.AgentAct;

namespace E2E.Tests.Agent;

/// <summary>
/// Judgment-tier tests with scripted models. Upstream runs one project through the runner and reads each result from
/// its report. The port runs each test body in a session.
/// </summary>
public sealed class AgentJudgmentTierTests
{
    private const string Context = "This is the e2e fixture application.";

    [Fact]
    public async Task Spends_one_judgment_while_the_page_it_is_waiting_on_does_not_change()
    {
        // The condition is false and the About page is static, so re-judging could only repeat the same answer.
        var judge = new ScriptedModel(_ => ModelResponses.Done("failed", "no checkout button here"));
        var engine = new ObservationCountingEngine(World());
        var seen = 0;
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/about");
                await Expect.That(ctx.Screen.GetByRole("heading")).ToHaveTextAsync("About");
                seen = engine.Observations;
                await ctx.Agent.WaitForAsync("a checkout button is on the About page", new WaitForOptions { Interval = TimeSpan.FromMilliseconds(100), Timeout = TimeSpan.FromSeconds(3), MaxModelCalls = 4 });
            },
            engine,
            judge: judge);

        Assert.Equal("STEP_TIMEOUT", Assert.IsType<AgentException>(result.Error).Code);
        Assert.Equal(1, judge.CallCount);
        // It kept looking, though: observations cost no model call.
        Assert.True(engine.Observations - seen > 2, "observations while waiting: " + (engine.Observations - seen));
    }

    [Fact]
    public async Task Repairs_a_malformed_judgment_with_a_second_model_call()
    {
        // A reasoning model that ran out of output mid-answer: no verdict.
        var judge = new ScriptedModel(request => request.Messages.Count == 1
            ? new ModelResponse { Content = "thinking" }
            : ModelResponses.Done("passed", "the Home heading is visible"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/about");
                await ctx.Agent.AssertAsync("the About heading is visible after a malformed first answer");
            },
            judge: judge);

        Assert.Null(result.Error);
        Assert.Equal(2, judge.CallCount);
        Assert.Equal(2, result.ModelCalls);
        Assert.Contains("Statement: the About heading is visible after a malformed first answer", judge.Requests[1].Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("Call done with status passed or failed.", judge.Requests[1].Messages[^1].Content);
    }

    // Upstream answers in the retired agent-judgment-1 shape. The port has no judgment protocol versions, so its second answer has no verdict.
    [Fact]
    public async Task Fails_MODEL_OUTPUT_INVALID_when_the_repair_round_gives_no_verdict_either()
    {
        var judge = new ScriptedModel(_ => new ModelResponse { Content = "thinking" });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/about");
                await ctx.Agent.AssertAsync("the About heading is visible, answered without a verdict twice");
            },
            judge: judge);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("MODEL_OUTPUT_INVALID", error.Code);
        Assert.Equal("The judge did not call done.", error.Message);
        // One judgment plus one repair round is the whole budget: no third call, no guessed verdict.
        Assert.Equal(2, judge.CallCount);
    }

    // Upstream also checks the token ceiling and that no temperature is pinned. Those belong to the provider models in the port.
    [Fact]
    public async Task Sends_provider_options_with_the_judgment()
    {
        var judge = new ScriptedModel(_ => ModelResponses.Done("passed", "the observation supports the assertion"));
        var options = new Dictionary<string, JsonElement> { ["fake"] = JsonSerializer.SerializeToElement(new { reasoningEffort = "low" }) };
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/about");
                await ctx.Agent.AssertAsync("the About heading is visible");
            },
            judge: judge,
            providerOptions: options);

        Assert.Null(result.Error);
        Assert.Equal("{\"reasoningEffort\":\"low\"}", judge.Requests[0].ProviderOptions!["fake"].GetRawText());
    }

    [Fact]
    public async Task Fails_the_test_with_ASSERTION_FAILED_on_a_false_judgment()
    {
        var judge = new ScriptedModel(_ => ModelResponses.Done("failed", "the observation shows the Home page, not checkout"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.AssertAsync("the checkout page is visible");
            },
            judge: judge);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.Contains("Home page, not checkout", error.Message, StringComparison.Ordinal);
    }

    // Upstream also checks the hint that points at vision, which the port does not have.
    [Fact]
    public async Task Fails_with_ASSERTION_INCONCLUSIVE_and_the_explanation_when_the_screen_shows_too_little_to_judge()
    {
        // A judge that cannot see the evidence must not pass the step: a guess in either direction is how a broken flow stays green.
        var judge = new ScriptedModel(_ => ModelResponses.Done("failed", "no order total or line items are on this screen", "ASSERTION_INCONCLUSIVE"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.AssertAsync("the order total equals the sum of the line items");
            },
            judge: judge);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("ASSERTION_INCONCLUSIVE", error.Code);
        Assert.Contains("no order total", error.Message, StringComparison.Ordinal);
    }

    // Upstream also checks that the timeout points at vision, which the port does not have.
    [Fact]
    public async Task Times_a_waitFor_out_with_the_explanation_of_its_last_inconclusive_judgment()
    {
        var judge = new ScriptedModel(_ => ModelResponses.Done("failed", "no order total is on the About page", "ASSERTION_INCONCLUSIVE"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/about");
                await Expect.That(ctx.Screen.GetByRole("heading")).ToHaveTextAsync("About");
                await ctx.Agent.WaitForAsync("the order total on the About page adds up", new WaitForOptions { Interval = TimeSpan.FromMilliseconds(100), Timeout = TimeSpan.FromMilliseconds(1500) });
            },
            judge: judge);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_TIMEOUT", error.Code);
        Assert.Contains("last judgment: no order total is on the About page", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Routes_every_judgment_to_the_judge_model_never_the_act_model()
    {
        var actor = new ScriptedModel(_ => throw new InvalidOperationException("the act model must not judge"));
        var judge = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("Return data matching", StringComparison.Ordinal)
                ? ModelResponses.Call("extract", new { data = new { counter = "0" } })
                : ModelResponses.Done("passed", "the observation supports the assertion");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.AssertAsync("the Home heading is visible");
                await ctx.Agent.WaitForAsync("the counter is on screen", new WaitForOptions { Interval = TimeSpan.FromMilliseconds(100) });
                await ctx.Agent.ExtractAsync<CounterReading>("the counter value");
            },
            actor: actor,
            judge: judge);

        Assert.Null(result.Error);
        Assert.Equal(3, judge.CallCount);
        Assert.Equal(0, actor.CallCount);
    }

    [Fact]
    public async Task Shows_the_judge_the_screen_and_the_question_never_the_prior_steps()
    {
        // An assert and then a waitFor. The waitFor's judgment must not carry the assert's statement or explanation:
        // a judge that reads the actor's account of what happened is grading a story, not a screen.
        var judge = new ScriptedModel(_ => ModelResponses.Done("passed", "the observation supports the assertion"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.AssertAsync("the Home heading is visible");
                await ctx.Agent.WaitForAsync("the Late arrival button exists", new WaitForOptions { Interval = TimeSpan.FromMilliseconds(100) });
            },
            judge: judge);

        Assert.Null(result.Error);
        var polled = judge.Requests[1];
        Assert.Contains("Statement: the Late arrival button exists", polled.Messages[0].Content, StringComparison.Ordinal);
        var everything = polled.System + "\n" + string.Join('\n', polled.Messages.Select(message => message.Content));
        Assert.DoesNotContain("<ledger>", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("the Home heading is visible", everything, StringComparison.Ordinal);
        Assert.DoesNotContain("the observation supports the assertion", everything, StringComparison.Ordinal);
    }

    // Upstream also checks the policy version the system prompt names, which the port does not have.
    [Fact]
    public async Task Puts_application_authored_instructions_after_the_policy_in_a_context_block()
    {
        var judge = new ScriptedModel(_ => ModelResponses.Done("passed", "the observation supports the assertion"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                await ctx.Agent.AssertAsync("the Home heading is visible");
            },
            judge: judge,
            context: Context);

        Assert.Null(result.Error);
        var system = judge.Requests[0].System;
        var block = "<project-context>\n" + Context + "\n</project-context>";
        Assert.EndsWith(block, system, StringComparison.Ordinal);
        Assert.True(system.Length > block.Length, "the policy comes first");
    }

    [Fact]
    public async Task Sends_the_semantic_tree_with_refs_and_a_secure_field_without_its_value()
    {
        var world = World().Map("/login", page =>
        {
            page.Button("Increment");
            page.Textbox("Password", "hunter2-secret", secure: true);
        });
        var judge = new ScriptedModel(_ => ModelResponses.Done("passed", "the field has a value"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/login");
                await ctx.Agent.AssertAsync("the password field has a value");
            },
            new ObservationCountingEngine(world),
            judge: judge);

        Assert.Null(result.Error);
        var observation = judge.Requests[0].Messages[0].Content!;
        Assert.Contains("button \"Increment\"", observation, StringComparison.Ordinal);
        Assert.Contains("- button \"Increment\" [ref=e1]", observation, StringComparison.Ordinal);
        Assert.Contains("- textbox \"Password\" [ref=e2] secure purpose=password", observation, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2-secret", observation, StringComparison.Ordinal);
    }

    private static DocumentWorld World() => CounterPage.World().Map("/about", page => page.Heading("About"));

    private static async Task<CoreTests.Attempt> RunAsync(
        Func<TestContext, Task> body,
        IEngine? engine = null,
        ScriptedModel? actor = null,
        ScriptedModel? judge = null,
        string? context = null,
        IReadOnlyDictionary<string, JsonElement>? providerOptions = null)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = engine ?? new DocumentEngine(World()),
            Model = actor,
            Judge = judge,
            AgentContext = context,
            ProviderOptions = providerOptions,
            BaseUrl = "https://app.test",
            TestTitle = "agent judgment tier",
            AssertionTimeout = TimeSpan.FromSeconds(2),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        Exception? error = null;
        try
        {
            await body(session.Context);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return new CoreTests.Attempt(error, session.ModelCalls, session.Replayed, session.Missed);
    }

    private sealed class CounterReading
    {
        public required string Counter { get; init; }
    }
}
