// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.ToolLoopOverflow;

/// <summary>
/// The port has no project tools, so upstream's read-only <c>peek</c> has no port: a tap on a button the page does not
/// have gives the first turn a short result to spend.
/// </summary>
public sealed class ToolLoopContextOverflowTests
{
    private const string Overflow = "prompt is too long: 300000 tokens > 200000 maximum";

    private static AgentException Refusal() => new("CONTEXT_OVERFLOW", "The model request exceeded the context window: " + Overflow, blocked: true);

    /// <summary>A screen whose text is far longer than the overflow clip, as a dense page is.</summary>
    private static DocumentWorld BigScreen() => new DocumentWorld().Map("/big", page =>
    {
        for (var i = 0; i < 600; i++)
        {
            page.Paragraph("row " + i + " " + new string('x', 40));
        }
    });

    private static DocumentWorld EmptyScreen() => new DocumentWorld().Map("/empty", _ => { });

    [Fact]
    public async Task Shrinks_the_history_and_retries_once_when_the_provider_refuses_the_request_as_too_large()
    {
        var requests = new List<List<string?>>();
        var model = new ScriptedModel(request =>
        {
            requests.Add(request.Messages.Select(message => message.Content).ToList());
            return requests.Count switch
            {
                1 => ModelResponses.Tap("button", "Peek"),
                2 => throw Refusal(),
                _ => ModelResponses.Done("passed", "read the rows"),
            };
        });
        ActResult? result = null;
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/big");
                result = await ctx.Agent.ActAsync("read the big output");
            },
            BigScreen(),
            model);

        Assert.Null(attempt.Error);
        Assert.Equal(3, model.CallCount);
        // The second request carried the whole opening screen; the retried request carries its head and a notice,
        // and the tool result it follows is unchanged.
        Assert.True(requests[1][0]!.Length > 16_384);
        Assert.True(requests[2][0]!.Length < 16_384 + 200);
        Assert.Contains("more characters cut: the request exceeded the model's context window", requests[2][0], StringComparison.Ordinal);
        Assert.Equal(requests[1][^1], requests[2][^1]);
        // The refused request never answered, so it is not a model call the step paid for.
        Assert.Equal(2, result!.ModelCalls);
    }

    /// <summary>
    /// Partial port of upstream's "shrinks a pixel-mode history, where every screen rides a result with its screenshot, and
    /// retries", so it carries another name. The port has no screenshots, so no result carries an image beside its screen. And it has no cache-friendly
    /// budget that elides superseded screens before a refusal, so every earlier screen is still whole in the refused request.
    /// </summary>
    [Fact]
    public async Task Elides_the_superseded_screens_and_clips_the_newest_one_at_a_refusal_and_retries()
    {
        var requests = new List<List<string?>>();
        var model = new ScriptedModel(request =>
        {
            requests.Add(request.Messages.Select(message => message.Content).ToList());
            return requests.Count switch
            {
                <= 2 => ModelResponses.Call("observe", new { }),
                3 => throw Refusal(),
                _ => ModelResponses.Done("passed", "read the rows"),
            };
        });
        ActResult? result = null;
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/big");
                result = await ctx.Agent.ActAsync("read the big output");
            },
            BigScreen(),
            model);

        Assert.Null(attempt.Error);
        Assert.Equal(4, model.CallCount);
        // The opening screen and the first result's are superseded: only their leads and a notice are left.
        Assert.Contains("[earlier screen elided", requests[3][0], StringComparison.Ordinal);
        Assert.Contains("[earlier screen elided", requests[3][2], StringComparison.Ordinal);
        // What was left to shrink is the newest screen, riding the second result.
        Assert.Contains("more characters cut: the request exceeded the model's context window", requests[3][^1], StringComparison.Ordinal);
        Assert.Equal(3, result!.ModelCalls);
    }

    [Fact]
    public async Task Reports_CONTEXT_OVERFLOW_when_the_shrunk_request_is_refused_again()
    {
        var model = new ScriptedModel(request => request.Messages.Count == 1 ? ModelResponses.Tap("button", "Peek") : throw Refusal());
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/big");
                await ctx.Agent.ActAsync("read the big output");
            },
            BigScreen(),
            model);

        var error = Assert.IsType<AgentException>(attempt.Error);
        Assert.Equal("CONTEXT_OVERFLOW", error.Code);
        Assert.Contains("again after the history was shrunk once", error.Message, StringComparison.Ordinal);
        Assert.Equal(3, model.CallCount);
    }

    [Fact]
    public async Task Does_not_resend_a_request_that_shrinking_would_not_change()
    {
        var model = new ScriptedModel(_ => throw Refusal());
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/empty");
                await ctx.Agent.ActAsync("do the thing");
            },
            EmptyScreen(),
            model);

        var error = Assert.IsType<AgentException>(attempt.Error);
        Assert.Equal("CONTEXT_OVERFLOW", error.Code);
        Assert.Contains("had nothing left to shrink", error.Message, StringComparison.Ordinal);
        // The opening request has no superseded screen and no long text: one refusal is the verdict, not two.
        Assert.Equal(1, model.CallCount);
    }

    [Fact]
    public async Task Leaves_other_provider_failures_to_the_ordinary_error_translation()
    {
        var model = new ScriptedModel(request => request.Messages.Count == 1
            ? ModelResponses.Tap("button", "Peek")
            : throw new AgentException("MODEL_PROVIDER_FAILED", "The model provider failed: Rate limit reached for requests"));
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/empty");
                await ctx.Agent.ActAsync("read the big output");
            },
            EmptyScreen(),
            model);

        Assert.Equal("MODEL_PROVIDER_FAILED", Assert.IsType<AgentException>(attempt.Error).Code);
        Assert.Equal(2, model.CallCount);
    }
}
