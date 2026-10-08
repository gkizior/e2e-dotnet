// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.TraceReplay;

/// <summary>
/// <c>replayTrace</c>: how far each look of a replay settles. Upstream drives <c>replayTrace</c> with a fake host; the
/// port replays inside an act, so each test records a flow on a document page, replays it, and reads the act's looks.
/// They open with the act's held-still start capture, which serves the first action, and close with the end check's
/// raw look. Only the settle <c>it</c>s are ported here: the port's replay has no gaps, drags, bare points, or lost-list
/// fallback, and always looks before a free action until it reaches the recorded end route.
/// </summary>
public sealed class ReplayTraceTests
{
    [Fact]
    public async Task Reads_the_start_capture_for_the_first_look_instead_of_observing_the_same_screen_again()
    {
        var looks = await RecordThenReplayAsync(TapWorld, ModelResponses.Tap("button", "Upgrade"));
        Assert.Equal([SettleMode.HeldStill, SettleMode.Raw], looks);
    }

    [Fact]
    public async Task Settles_each_look_as_far_as_the_previous_actions_policy_asks_held_still_after_a_tap_after_the_change_after_a_fill()
    {
        var tap = ModelResponses.Tap("button", "Upgrade");
        var looks = await RecordThenReplayAsync(
            TapWorld,
            tap,
            ModelResponses.Fill("textbox", "Email", "a@b.c"),
            tap,
            ModelResponses.Call("fill_secret", new { role = "textbox", name = "Password", secret = "password" }),
            tap,
            ModelResponses.Call("select", new { role = "combobox", name = "Plan", value = "Pro" }),
            tap);

        // The start holds still and serves the first tap; so does the look after each tap or select. The look
        // after a typed fill waits for its value only. A secret fill is masked out of the tree, so the look after
        // it holds still: there is no change to wait for.
        Assert.Equal(
            [
                SettleMode.HeldStill,
                SettleMode.HeldStill,
                SettleMode.AfterChange,
                SettleMode.HeldStill,
                SettleMode.HeldStill,
                SettleMode.HeldStill,
                SettleMode.HeldStill,
                SettleMode.Raw,
            ],
            looks);
    }

    [Fact]
    public async Task Takes_one_look_per_repeat_of_a_folded_scroll_on_a_list()
    {
        var looks = await RecordThenReplayAsync(ScrollWorld, ModelResponses.Call("scroll", new { role = "group", name = "Rows 1 to 12", direction = "down", times = 3 }));
        Assert.Equal([SettleMode.HeldStill, SettleMode.HeldStill, SettleMode.HeldStill, SettleMode.Raw], looks);
    }

    [Fact]
    public async Task Repeats_a_folded_viewport_scroll_as_many_times_as_recorded_with_a_settled_look_between_repeats()
    {
        var looks = await RecordThenReplayAsync(ScrollWorld, ModelResponses.Call("scroll", new { direction = "down", times = 4 }));

        // Nothing to relocate, so the first repeat takes no look; each later one holds still first.
        Assert.Equal([SettleMode.HeldStill, SettleMode.HeldStill, SettleMode.HeldStill, SettleMode.HeldStill, SettleMode.Raw], looks);
    }

    // Every action changes what the page shows, so no look waits out a change window.
    private static DocumentWorld TapWorld() => new DocumentWorld().Map("/plan", page =>
    {
        var taps = 0;
        var status = page.Status("Taps 0");
        page.Button("Upgrade", () => status.Name = status.Text = "Taps " + ++taps);
        page.Textbox("Email");
        page.Textbox("Password", secure: true);
        page.Roots.Add(new DocumentElement { Role = "combobox", Name = "Plan", Value = "Free" });
    });

    private static DocumentWorld ScrollWorld() => new DocumentWorld().Map("/plan", page =>
    {
        var scrolls = 0;
        var status = page.Status("Scrolled 0");
        page.Roots.Add(new DocumentElement { Role = "group", Name = "Rows 1 to 12", OnScroll = _ => status.Name = status.Text = "Scrolled " + ++scrolls });
        page.OnScroll = _ => status.Name = status.Text = "Scrolled " + ++scrolls;
    });

    /// <summary>Records the steps live on a fresh cache, then replays them, and returns the replaying act's looks.</summary>
    private static async Task<IReadOnlyList<SettleMode>> RecordThenReplayAsync(Func<DocumentWorld> world, params ModelResponse[] steps)
    {
        var directory = CoreTests.TempCache();
        var next = 0;
        var recorded = await ActAsync(world(), directory, new ScriptedModel(_ => next < steps.Length ? steps[next++] : ModelResponses.Done("passed", "Done.")));
        Assert.Equal("missed", recorded.Mode);

        var replayed = await ActAsync(world(), directory, new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")));
        Assert.Equal("self-finalized", replayed.Mode);
        return replayed.Looks;
    }

    private static async Task<(string? Mode, IReadOnlyList<SettleMode> Looks)> ActAsync(DocumentWorld world, string directory, IAgentModel model)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            Model = model,
            BaseUrl = "https://billing.test",
            Cache = new FileStepCache(directory),
            CacheMode = CacheMode.ReadWrite,
            TestTitle = "replay > looks",
            AssertionTimeout = TimeSpan.FromSeconds(2),
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            ReplayTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(30),
            StepTimeout = TimeSpan.FromSeconds(20),
        });
        Exception? error = null;
        string? mode = null;
        try
        {
            await session.Context.App.OpenAsync("/plan");
            var parameters = new Dictionary<string, object?> { ["password"] = Secret.Create("password", "hunter2") };
            mode = (await session.Context.Agent.ActAsync("work the plan page", new ActOptions { Params = parameters })).Cache?.Mode;

            // A passing check verifies the act, so its recording is written.
            await Expect.That(session.Context.Screen.GetByRole("heading", "Missing")).ToBeHiddenAsync();
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        Assert.Null(error);
        return (mode, [.. session.Context.Agent.Feed.Looks]);
    }
}
