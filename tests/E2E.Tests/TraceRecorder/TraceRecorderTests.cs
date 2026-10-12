// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.TraceRecorder.RecordedEntries;

namespace E2E.Tests.TraceRecorder;

/// <summary>
/// The port records inside the agent, with a plain action list: no summaries, CSS selectors, positions, scroll spans,
/// truncation flag, action cap, tool gaps, secret names, or entry validation, and no hover, long-press, secondary-tap,
/// drag, upload, or point-hover verbs. So these upstream its are not ported: "poisons a trace whose target is an unnamed
/// twin no named row tells apart, and keeps one that a row does",
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
    /// this records four scrolls down and one up through an act and reads the entry back.
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
            ModelResponses.Call("scroll", new { direction = "up" }),
            ModelResponses.Done("passed", "scrolled"));
        var result = await ScrollTests.RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/rows");
                await ctx.Agent.ActAsync("scroll down four screens, then up one");
                await Expect.That(ctx.Screen.GetByRole("button", "Row 25")).ToBeVisibleAsync();
            },
            ScrollTests.Rows(),
            model,
            directory);

        Assert.Null(result.Error);
        var entry = ScrollTests.ReadEntry(directory);
        Assert.Equal([("down", (int?)4), ("up", null)], entry.Actions.Select(action => (action.Direction, action.Times)));
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

    /// <summary>Partial port of "records durable descriptors and readable summaries": the port keeps no summary and no CSS selector.</summary>
    [Fact]
    public async Task Records_the_durable_descriptors_of_each_action()
    {
        var directory = await RecordAsync(
            SettingsWorld(),
            null,
            ModelResponses.Call("navigate", new { url = "/billing" }),
            ModelResponses.Tap("button", "Upgrade"),
            ModelResponses.Fill("textbox", "Plan", "yearly"));

        var entry = ReadEntry(directory);

        Assert.Equal("/settings", entry.Route);
        Assert.Equal(
            [("navigate", "/billing", null, null, null, null), ("tap", null, "button", "Upgrade", "upgrade-cta", null), ("fill", null, "textbox", "Plan", null, "yearly")],
            entry.Actions.Select(action => (action.Kind, action.Url, action.Role, action.Name, action.TestId, action.Value)));
    }

    /// <summary>Partial port of "always finalizes into a trace the reader accepts": the port has no tool gaps, so a plain recording is read back.</summary>
    [Fact]
    public async Task Finalizes_into_an_entry_the_store_reads_back()
    {
        var directory = await RecordAsync(SettingsWorld(), null, ModelResponses.Call("navigate", new { url = "/billing" }), ModelResponses.Tap("button", "Upgrade"));

        var read = new FileStepCache(directory).Read(Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.json"))));

        Assert.NotNull(read.Entry);
        Assert.Equal(2, read.Entry.Actions.Count);
    }

    /// <summary>Partial port of "records a secret fill by name only, with page strings redacted": the port has no truncation flag to leave unset.</summary>
    [Fact]
    public async Task Records_a_secret_fill_as_its_marker_with_page_strings_redacted()
    {
        var parameters = new Dictionary<string, object?> { ["password"] = Secret.Create("member-password", "hunter2", "password") };
        var directory = await RecordAsync(
            SettingsWorld("Greeting hunter2"),
            parameters,
            ModelResponses.Call("navigate", new { url = "/billing" }),
            ModelResponses.Call("fill_secret", new { role = "textbox", name = "Password", secret = "member-password" }),
            ModelResponses.Tap("button", "Greeting <secret:member-password>"));

        var raw = RawEntry(directory);
        var entry = ReadEntry(directory);

        Assert.DoesNotContain("hunter2", raw, StringComparison.Ordinal);
        Assert.Contains("<secret:member-password>", raw, StringComparison.Ordinal);
        Assert.Equal("<secret:member-password>", entry.Actions.Single(action => action.Kind == "fill").Value);
        Assert.Equal("Greeting <secret:member-password>", entry.Actions[^1].Name);
    }
}
