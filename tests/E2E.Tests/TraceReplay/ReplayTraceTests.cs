// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.StepCache.StepTraceSessionTests;

namespace E2E.Tests.TraceReplay;

/// <summary>
/// <c>replayTrace</c>: how far each look of a replay settles. Upstream drives <c>replayTrace</c> with a fake host; the
/// port replays inside an act, so each test records a flow on a document page, replays it, and reads the act's looks.
/// They open with the act's held-still start capture, which serves the first action, and close with the end check's
/// raw look. The port's replay has no gaps, drags, bare points, or lost-list
/// fallback, and always looks before a free action until it reaches the recorded end route, so those <c>it</c>s are not ported.
/// Not ported either: "relocates by semantic identity when a recorded test id churned" (the port finds a test id exactly),
/// "surfaces an unknown commit state as action-uncertain" (the port has no such reason), and "hands off with
/// action-failed when the look before the second action throws" (the port's look before an action is outside the replay's catch).
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

    private static readonly ModelResponse[] NavigateThenUpgrade = [ModelResponses.Call("navigate", new { url = "/plan" }), ModelResponses.Tap("button", "Upgrade")];

    private static Task<Step> ReplayStepAsync(DocumentWorld world, string directory, Func<TestContext, Task>? check, params ModelResponse[] steps)
    {
        var next = 0;
        var model = new ScriptedModel(_ => next < steps.Length ? steps[next++] : ModelResponses.Done("passed", "Done."));
        return StepAsync(world, new FileStepCache(directory), "/home", check, model);
    }

    private static async Task<string> RecordAsync(Func<DocumentWorld> world, params ModelResponse[] steps)
    {
        var directory = CoreTests.TempCache();
        var recorded = await ReplayStepAsync(world(), directory, ctx => Expect.That(ctx.Screen.GetByRole("status")).ToBeVisibleAsync(), steps);
        Assert.Null(recorded.Error);
        Assert.Equal("missed", recorded.Cache?.Mode);
        return directory;
    }

    [Fact]
    public async Task Still_diverges_at_once_on_ambiguity_for_a_target_with_no_recorded_position()
    {
        var tapped = 0;
        var directory = await RecordAsync(
            () => new DocumentWorld().Map("/home", page =>
            {
                var status = page.Status("Idle");
                page.Button("Upgrade", () => status.Name = status.Text = "Upgraded " + ++tapped);
            }),
            ModelResponses.Tap("button", "Upgrade"));
        tapped = 0;
        var twins = new DocumentWorld().Map("/home", page =>
        {
            page.Status("Idle");
            page.Button("Upgrade", () => tapped++);
            page.Button("Upgrade", () => tapped++);
        });

        var replay = await ReplayStepAsync(twins, directory, null, ModelResponses.Done("passed", "Done."));

        Assert.Equal(("missed", "target-ambiguous", 0), (replay.Cache?.Mode, replay.Cache?.Reason, replay.Cache?.ReplayedActions));
        Assert.Equal(0, tapped);
    }

    [Fact]
    public async Task Absorbs_an_action_failure_as_divergence_never_as_a_step_failure()
    {
        var failing = false;
        DocumentWorld World() => new DocumentWorld().Map("/home", page => page.Heading("Home")).Map("/plan", page =>
        {
            var status = page.Status("Idle");
            page.Button("Upgrade", () =>
            {
                if (failing)
                {
                    throw new EngineException("ACTION_FAILED", "element detached");
                }

                status.Name = status.Text = "Upgraded";
            });
        });
        var directory = await RecordAsync(World, NavigateThenUpgrade);
        failing = true;

        var replay = await ReplayStepAsync(World(), directory, null, ModelResponses.Done("passed", "Done."));

        Assert.Null(replay.Error);
        Assert.Equal(("agent-concluded", "action-failed", 1), (replay.Cache?.Mode, replay.Cache?.Reason, replay.Cache?.ReplayedActions));
    }

    [Fact]
    public async Task Rethrows_runtime_hard_stops_untouched()
    {
        var exhausted = false;
        DocumentWorld World() => new DocumentWorld().Map("/home", page => page.Heading("Home")).Map("/plan", page =>
        {
            var status = page.Status("Idle");
            page.Button("Upgrade", () =>
            {
                if (exhausted)
                {
                    throw new AgentException("STEP_BUDGET_EXHAUSTED", "out of actions");
                }

                status.Name = status.Text = "Upgraded";
            });
        });
        var directory = await RecordAsync(World, NavigateThenUpgrade);
        exhausted = true;

        var replay = await ReplayStepAsync(World(), directory, null, ModelResponses.Done("passed", "Done."));

        Assert.Equal("STEP_BUDGET_EXHAUSTED", Assert.IsType<AgentException>(replay.Error).Code);
    }

    [Fact]
    public async Task Hands_off_when_a_smaller_scrolled_region_cannot_be_re_found_never_scrolling_the_viewport_for_it()
    {
        var scrolled = 0;
        var directory = await RecordAsync(
            () => new DocumentWorld().Map("/home", page =>
            {
                var status = page.Status("Idle");
                page.Roots.Add(new DocumentElement { Role = "group", Name = "Recommended", OnScroll = _ => status.Name = status.Text = "Scrolled " + ++scrolled });
            }),
            ModelResponses.Call("scroll", new { role = "group", name = "Recommended", direction = "right" }));
        scrolled = 0;
        var viewportScrolls = 0;
        var without = new DocumentWorld().Map("/home", page =>
        {
            page.Button("Upgrade");
            page.OnScroll = _ => viewportScrolls++;
        });

        var replay = await ReplayStepAsync(without, directory, null, ModelResponses.Done("passed", "Done."));

        Assert.Equal(("missed", "target-not-found", 0), (replay.Cache?.Mode, replay.Cache?.Reason, replay.Cache?.ReplayedActions));
        Assert.Equal((0, 0), (scrolled, viewportScrolls));
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
