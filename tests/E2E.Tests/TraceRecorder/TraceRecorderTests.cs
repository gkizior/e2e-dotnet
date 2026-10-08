// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceRecorder;

/// <summary>
/// The port records inside the agent, with a plain action list: no summaries, CSS selectors, positions, scroll spans,
/// truncation flag, action cap, tool gaps, secret names, or entry validation, and no hover, long-press, secondary-tap,
/// drag, upload, or point-hover verbs. So these upstream its are not ported: "poisons a trace whose target is an unnamed
/// twin no named row tells apart, and keeps one that a row does", "records durable descriptors and readable summaries",
/// "always finalizes into a trace the reader accepts", "records a secret fill by name only, with page strings redacted",
/// "masks a secret resolved after the node was captured", "masks a secret resolved after capture in the container the
/// node sat within", "poisons the trace instead of bending a replay input", "keeps the smallest coverage when folding
/// scrolls, and none when a repeat lacks one", "marks a gap that ends replay for a mutating project tool", "truncates
/// past the action cap instead of dropping the flag", "records every node verb, a state, a drag with its destination,
/// files, and a back step, and reads them back", and "rejects a stored check, upload, or drag that lost a field".
/// </summary>
public sealed class TraceRecorderTests
{
    /// <summary>
    /// Upstream drives the recorder directly. The port records inside the agent, so
    /// this records five scrolls down and one up through an act and reads the entry back.
    /// </summary>
    [Fact]
    public async Task Folds_consecutive_identical_scrolls_into_one_action_with_a_repeat_count()
    {
        var directory = ScrollTests.TempCache();
        var model = ScrollTests.Sequence(
            ModelResponses.Call("scroll", new { direction = "down" }),
            ModelResponses.Call("scroll", new { direction = "down" }),
            ModelResponses.Call("scroll", new { direction = "down" }),
            ModelResponses.Call("scroll", new { direction = "down" }),
            ModelResponses.Call("scroll", new { direction = "down" }),
            ModelResponses.Call("scroll", new { direction = "up" }),
            ModelResponses.Done("passed", "scrolled"));
        var result = await ScrollTests.RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/rows");
                await ctx.Agent.ActAsync("scroll down five screens, then up one");
                await Expect.That(ctx.Screen.GetByRole("button", "Row 30")).ToBeVisibleAsync();
            },
            ScrollTests.Rows(),
            model,
            directory);

        Assert.Null(result.Error);
        var entry = ScrollTests.ReadEntry(directory);
        Assert.Equal([("down", (int?)5), ("up", null)], entry.Actions.Select(action => (action.Direction, action.Times)));
    }

    [Fact]
    public async Task Returns_no_trace_for_a_step_that_committed_nothing()
    {
        var directory = ScrollTests.TempCache();
        var result = await ScrollTests.RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/rows");
                await ctx.Agent.ActAsync("look at the rows");
                await Expect.That(ctx.Screen.GetByRole("button", "Row 1")).ToBeVisibleAsync();
            },
            ScrollTests.Rows(),
            ScrollTests.Sequence(ModelResponses.Done("passed", "nothing to do")),
            directory);

        Assert.Null(result.Error);
        Assert.False(Directory.Exists(directory) && Directory.GetFiles(directory, "*.json").Length > 0);
    }
}
