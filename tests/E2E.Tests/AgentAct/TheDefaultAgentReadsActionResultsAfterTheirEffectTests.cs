// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentAct;

public sealed class TheDefaultAgentReadsActionResultsAfterTheirEffectTests
{
    // The port's tool results carry the whole screen, so this checks the view that arrived late, not a diff of it.
    [Fact]
    public async Task Waits_for_a_change_that_lands_after_the_tap_and_shows_the_screen_it_produced()
    {
        var world = new DocumentWorld().Map("/delayed", page =>
        {
            var second = page.Heading("Second view");
            second.Hidden = true;
            DocumentElement? next = null;
            next = page.Button("Continue", () => _ = Task.Delay(700).ContinueWith(
                _ =>
                {
                    next!.Hidden = true;
                    second.Hidden = false;
                },
                TaskScheduler.Default));
        });
        var model = new ScriptedModel(request => request.Messages.Any(message => message.Role == "tool")
            ? ModelResponses.Done("passed", "the second view is showing")
            : ModelResponses.Tap("button", "Continue"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/delayed");
                await ctx.Agent.ActAsync("continue to the second view");
                await Expect.That(ctx.Screen.GetByRole("heading", "Second view")).ToBeVisibleAsync();
            },
            world,
            model);

        Assert.Null(result.Error);
        // Two turns: the tap and the verdict. The tap's own result already shows the view that arrived 700 ms after the click.
        Assert.Equal(2, model.CallCount);
        var toolResult = CounterPage.ToolResults(model)[0];
        Assert.Contains("heading \"Second view\"", toolResult, StringComparison.Ordinal);
        Assert.DoesNotContain("- button \"Continue\"", toolResult, StringComparison.Ordinal);
    }

    // The port's tool results carry the whole screen after each action, not the changes of that action alone,
    // and a redundant observe is not answered cheaply.
    [Fact]
    public async Task Runs_the_actions_of_one_turn_in_order()
    {
        ActResult? act = null;
        var model = new ScriptedModel(request => request.Messages.Count(message => message.Role == "tool") switch
        {
            0 => CounterPage.Batch(
                ModelResponses.Fill("textbox", "Email", "ada@example.test"),
                ModelResponses.Fill("textbox", "Focus target", "hello")),
            _ => ModelResponses.Done("passed", "both fields hold their values"),
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/");
                act = await ctx.Agent.ActAsync("fill the email and the focus target fields");
                await Expect.That(ctx.Screen.GetByLabel("Email")).ToHaveValueAsync("ada@example.test");
                await Expect.That(ctx.Screen.GetByLabel("Focus target")).ToHaveValueAsync("hello");
            },
            CounterPage.World(),
            model);

        Assert.Null(result.Error);
        var results = CounterPage.ToolResults(model);
        // The first result is read before the second action ran.
        Assert.Contains("textbox \"Email\" [ref=", results[0], StringComparison.Ordinal);
        Assert.Contains("value=\"ada@example.test\"", results[0], StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"hello\"", results[0], StringComparison.Ordinal);
        Assert.Contains("value=\"hello\"", results[1], StringComparison.Ordinal);
        Assert.Equal(2, act!.Actions);
        Assert.Equal(2, model.CallCount);
    }
}
