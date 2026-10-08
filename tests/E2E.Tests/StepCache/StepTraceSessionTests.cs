// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;

namespace E2E.Tests.StepCache;

/// <summary><c>StepTraceSession</c>: what a step records and when its replay finishes on its own.</summary>
public sealed class StepTraceSessionTests
{
    private static readonly JsonSerializerOptions EntryJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Hands_off_on_an_end_route_with_any_literal_that_differs_the_recorded_anchors_on_screen_or_not()
    {
        // A slug the runner cannot recognize, or a link that now lands on a lookalike page: another screen either way.
        foreach (var (start, recorded, replayed) in new[] { ("/products", "/products/summer-sneaker", "/products/winter-boot"), ("/nav", "/settings-page", "/profile") })
        {
            var directory = CoreTests.TempCache();
            DocumentWorld World(string target) => new DocumentWorld()
                .Map(start, page => page.Link("Open", target))
                .Map(recorded, page => page.Status("Marker saved"))
                .Map(replayed, page => page.Status("Marker saved"));

            Assert.Null((await RunAsync(World(recorded), directory, start, Saved, ModelResponses.Tap("link", "Open"))).Error);

            var replay = await RunAsync(World(replayed), directory, start, null, ModelResponses.Tap("link", "Open"));
            Assert.Equal(0, replay.Replayed);
            Assert.Equal(("agent-concluded", "end-mismatch"), (replay.Cache?.Mode, replay.Cache?.Reason));
        }
    }

    [Fact]
    public async Task Hands_off_when_the_recorded_delta_did_not_happen_during_the_replay()
    {
        // The submit gives the draft way to the saved marker, or does nothing.
        static DocumentWorld World(string initial, bool submits, bool removes = false) => new DocumentWorld().Map("/form", page =>
        {
            var status = page.Status(initial);
            page.Button("Submit", () =>
            {
                if (submits)
                {
                    status.Name = status.Text = "Marker saved";
                }

                if (removes)
                {
                    status.Hidden = true;
                }
            });
        });
        var tap = ModelResponses.Tap("button", "Submit");

        // The effect happened: the draft gave way to the saved marker.
        var directory = CoreTests.TempCache();
        Assert.Null((await RunAsync(World("Marker draft", submits: true), directory, "/form", Saved, tap)).Error);
        var good = await RunAsync(World("Marker draft", submits: true), directory, "/form", Saved, tap);
        Assert.Equal(1, good.Replayed);
        Assert.Equal("self-finalized", good.Cache?.Mode);

        // The saved marker already showed before the submit, which did nothing: no proof.
        var already = await RunAsync(World("Marker saved", submits: false), directory, "/form", null, tap);
        Assert.Equal(("agent-concluded", "end-mismatch"), (already.Cache?.Mode, already.Cache?.Reason));

        // A removal that did not happen: the vanished node is still there.
        directory = CoreTests.TempCache();
        Assert.Null((await RunAsync(World("Marker draft", submits: false, removes: true), directory, "/form", ctx => Expect.That(ctx.Screen.GetByRole("status")).ToBeHiddenAsync(), tap)).Error);
        var kept = await RunAsync(World("Marker draft", submits: false), directory, "/form", null, tap);
        Assert.Equal("end-mismatch", kept.Cache?.Reason);

        // A recording with no delta at all and no move proves nothing.
        directory = CoreTests.TempCache();
        Assert.Null((await RunAsync(World("Marker draft", submits: true), directory, "/form", Saved, tap)).Error);
        var path = Assert.Single(Directory.GetFiles(directory, "*.json"));
        var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path), EntryJson)!;
        entry.Appeared = [];
        entry.Gone = [];
        File.WriteAllText(path, JsonSerializer.Serialize(entry, EntryJson));
        var blind = await RunAsync(World("Marker draft", submits: true), directory, "/form", null, tap);
        Assert.Equal("end-mismatch", blind.Cache?.Reason);
    }

    [Fact]
    public async Task Measures_the_evidence_from_the_page_a_recorded_navigate_opened_not_from_the_screen_the_replay_began_on()
    {
        static DocumentWorld World(bool alreadyOn) => new DocumentWorld()
            .Map("/home", page => page.Heading("Home"))
            .Map("/settings", page =>
            {
                DocumentElement? toggle = null;
                toggle = new DocumentElement { Role = "switch", Name = "Email alerts", Checked = alreadyOn, OnTap = () => toggle!.Checked = true };
                page.Roots.Add(toggle);
            });
        var steps = new[] { ModelResponses.Call("navigate", new { url = "/settings" }), ModelResponses.Call("tap", new { role = "switch", name = "Email alerts" }) };
        static Task On(TestContext ctx) => Expect.That(ctx.Screen.GetByRole("switch", "Email alerts")).ToBeCheckedAsync();

        var directory = CoreTests.TempCache();
        Assert.Null((await RunAsync(World(alreadyOn: false), directory, "/home", On, steps)).Error);

        // The switch was off on the settings page and on after the tap: the replay turned it on.
        var good = await RunAsync(World(alreadyOn: false), directory, "/home", On, steps);
        Assert.Equal(1, good.Replayed);

        // It was already on when the page opened, from an earlier run, and the tap did nothing.
        var already = await RunAsync(World(alreadyOn: true), directory, "/home", null, steps);
        Assert.Equal(("agent-concluded", "end-mismatch"), (already.Cache?.Mode, already.Cache?.Reason));
    }

    [Fact]
    public async Task Evicts_an_entry_that_did_not_serve_a_pass_with_nothing_to_record_in_its_place()
    {
        var directory = CoreTests.TempCache();
        var submitted = false;
        DocumentWorld World() => new DocumentWorld().Map("/form", page =>
        {
            var status = page.Status(submitted ? "Submitted" : "Draft");
            page.Button("Submit", () => status.Name = status.Text = "Submitted");
        });
        var modelCalls = 0;
        var model = new ScriptedModel(request =>
        {
            modelCalls++;
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal) || text.Contains("end-mismatch", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Submitted.")
                : ModelResponses.Tap("button", "Submit");
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/form");
            await ctx.Agent.ActAsync("submit the form");
            await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("Submitted");
        }

        var first = await CoreTests.RunAsync(Body, World(), model, directory);
        Assert.Null(first.Error);
        Assert.Single(Directory.GetFiles(directory, "*.json"));

        // The status already reads Submitted, so the replay hands off; the agent finds the step done without changing anything.
        submitted = true;
        modelCalls = 0;
        var second = await CoreTests.RunAsync(Body, World(), model, directory);
        Assert.Null(second.Error);
        Assert.Equal(0, second.Replayed);
        Assert.True(modelCalls > 0);
        Assert.Empty(Directory.GetFiles(directory, "*.json"));
    }

    [Fact]
    public async Task Never_stages_a_step_that_changed_nothing_a_replay_could_check()
    {
        var directory = CoreTests.TempCache();
        var world = new DocumentWorld().Map("/share", page =>
        {
            page.Heading("Share");
            page.Button("Copy link");
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Copied.")
                : ModelResponses.Tap("button", "Copy link");
        });

        var result = await CoreTests.RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/share");
            await ctx.Agent.ActAsync("copy the share link");
            await Expect.That(ctx.Screen.GetByRole("heading", "Share")).ToBeVisibleAsync();
        }, world, model, directory);

        Assert.Null(result.Error);
        Assert.False(Directory.Exists(directory) && Directory.GetFiles(directory, "*.json").Length > 0);
    }

    [Fact]
    public async Task Stages_what_a_removal_only_step_made_vanish()
    {
        var directory = CoreTests.TempCache();
        static DocumentWorld World() => new DocumentWorld().Map("/items", page =>
        {
            var item = new DocumentElement { Role = "listitem", Name = "Item A" };
            page.Roots.Add(item);
            page.Button("Delete", () => item.Hidden = true);
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Deleted.")
                : ModelResponses.Tap("button", "Delete");
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/items");
            await ctx.Agent.ActAsync("delete Item A");
            await Expect.That(ctx.Screen.GetByRole("listitem", "Item A")).ToBeHiddenAsync();
        }

        var first = await CoreTests.RunAsync(Body, World(), model, directory);
        Assert.Null(first.Error);
        var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json"))), EntryJson)!;
        Assert.Empty(entry.Appeared);
        var gone = Assert.Single(entry.Gone);
        Assert.Equal(("listitem", "Item A"), (gone.Role, gone.Name));

        var second = await CoreTests.RunAsync(Body, World(), new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task Turns_a_rejecting_store_read_into_a_miss_instead_of_failing_the_step()
    {
        var model = Script(ModelResponses.Tap("button", "Save marker"));

        var step = await StepAsync(StorageWorld(), new ThrowingCache(), "/storage", null, model);

        Assert.Null(step.Error);
        Assert.Equal(("missed", "invalid-entry", 0, 0), Info(step));
        Assert.DoesNotContain("Replay stopped", Text(model.Requests[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Names_a_retry_attempt_as_the_miss_reason_without_reading_the_store()
    {
        var cache = new CountingCache(CoreTests.TempCache());
        var model = Script(ModelResponses.Tap("button", "Save marker"));

        var step = await StepAsync(StorageWorld(), cache, "/storage", null, model, attempt: 2);

        Assert.Equal(0, cache.Reads);
        Assert.Equal(("missed", "retry", 0, 0), Info(step));
        Assert.DoesNotContain("Replay stopped", Text(model.Requests[0]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Degrades_a_hit_that_is_not_a_trace_1_entry_to_a_miss_whichever_store_returned_it()
    {
        var step = await StepAsync(StorageWorld(), new MalformedCache(), "/storage", null, Script(ModelResponses.Tap("button", "Save marker")));

        Assert.Equal(("missed", "invalid-entry", 0, 0), Info(step));
    }

    [Fact]
    public async Task Takes_no_unchanged_node_for_the_delta_when_a_secret_registered_after_the_starting_screen_masks_it()
    {
        var directory = CoreTests.TempCache();
        var world = new DocumentWorld().Map("/storage", page =>
        {
            page.Status("Code token-2718-value");
            var saved = new DocumentElement { Role = "status", Name = "Marker saved", Hidden = true };
            page.Roots.Add(saved);
            page.Button("Save marker", () => saved.Hidden = false);
        });
        var parameters = new Dictionary<string, object?> { ["token"] = Secret.Create("token", "token-2718-value") };

        var step = await StepAsync(world, new FileStepCache(directory), "/storage", Saved, Script(ModelResponses.Tap("button", "Save marker")), parameters);

        Assert.Null(step.Error);
        Assert.Equal([("status", "Marker saved")], Entry(directory).Appeared.Select(anchor => (anchor.Role, anchor.Name)));
    }

    [Fact]
    public async Task Stages_the_delta_between_the_starting_and_passing_screens_as_end_anchors()
    {
        var directory = CoreTests.TempCache();

        var step = await StepAsync(StorageWorld(), new FileStepCache(directory), "/storage", Saved, Script(ModelResponses.Tap("button", "Save marker")));

        Assert.Null(step.Error);
        var entry = Entry(directory);
        Assert.Equal([("status", "Marker saved")], entry.Appeared.Select(anchor => (anchor.Role, anchor.Name)));
        Assert.Equal("/storage", entry.EndRoute);
    }

    [Fact]
    public async Task Records_nothing_when_a_unique_value_is_spelled_by_another_param_and_says_so_in_the_step_detail()
    {
        static DocumentWorld World() => new DocumentWorld()
            .Map("/reminders", page => page.Heading("Reminders"))
            .Map("/reminders/new", page => page.Status("Reminder created"));
        static Task Created(TestContext ctx) => Expect.That(ctx.Screen.GetByRole("status", "Reminder created")).ToBeVisibleAsync();

        var directory = CoreTests.TempCache();
        var collides = await StepAsync(
            World(),
            new FileStepCache(directory),
            "/reminders",
            Created,
            Script(ModelResponses.Call("navigate", new { url = "/reminders/new?title=Daily&frequency=Daily" })),
            new Dictionary<string, object?> { ["title"] = Values.Unique("Daily"), ["frequency"] = "Daily" });
        Assert.False(Directory.Exists(directory) && Directory.GetFiles(directory, "*.json").Length > 0);
        Assert.Equal(("missed", "param-collision"), (collides.Cache?.Mode, collides.Cache?.NotRecorded));

        // The same call with a title of its own records, and the detail carries no such note.
        directory = CoreTests.TempCache();
        var clean = await StepAsync(
            World(),
            new FileStepCache(directory),
            "/reminders",
            Created,
            Script(ModelResponses.Call("navigate", new { url = "/reminders/new?title=Water+plants&frequency=Daily" })),
            new Dictionary<string, object?> { ["title"] = Values.Unique("Water plants"), ["frequency"] = "Daily" });
        var url = Entry(directory).Actions[0].Url!;
        Assert.DoesNotContain("Water+plants", url, StringComparison.Ordinal);
        Assert.EndsWith("&frequency=Daily", url, StringComparison.Ordinal);
        Assert.Null(clean.Cache?.NotRecorded);
    }

    [Fact]
    public async Task Records_the_new_screen_as_anchors_for_a_step_that_moved_to_another_pathname()
    {
        var directory = CoreTests.TempCache();

        var step = await StepAsync(CustomersWorld(), new FileStepCache(directory), "/pricing", Saved, Script(ModelResponses.Call("navigate", new { url = "/customers?ref=nav" })));

        Assert.Null(step.Error);
        var entry = Entry(directory);
        Assert.Equal([("status", "Marker saved")], entry.Appeared.Select(anchor => (anchor.Role, anchor.Name)));
        Assert.Equal("/customers?ref=nav", entry.EndRoute);
    }

    [Fact]
    public async Task Self_finalizes_on_a_created_records_page_whose_minted_id_differs_from_the_recording()
    {
        var directory = CoreTests.TempCache();
        await StepAsync(ProjectWorld("/projects/95488a620f65"), new FileStepCache(directory), "/pricing", Saved, Script(ModelResponses.Tap("link", "Create project")));

        var step = await StepAsync(ProjectWorld("/projects/0c1d2e3f4a5b"), new FileStepCache(directory), "/pricing", null, Script());

        Assert.Equal(1, step.Replayed);
        Assert.Equal("self-finalized", step.Cache?.Mode);
    }

    [Fact]
    public async Task Still_refuses_a_different_page_even_when_the_anchors_happen_to_be_on_it()
    {
        var directory = CoreTests.TempCache();
        await StepAsync(ProjectWorld("/projects/95488a620f65"), new FileStepCache(directory), "/pricing", Saved, Script(ModelResponses.Tap("link", "Create project")));

        var step = await StepAsync(ProjectWorld("/customers"), new FileStepCache(directory), "/pricing", null, Script());

        Assert.Equal(0, step.Replayed);
        Assert.Equal("end-mismatch", step.Cache?.Reason);
    }

    [Fact]
    public async Task Refuses_to_self_finalize_when_a_recorded_end_anchor_is_not_on_screen_again()
    {
        var directory = CoreTests.TempCache();
        await RecordCustomersAsync(directory);
        var model = Script();

        var step = await StepAsync(CustomersWorld(saved: false), new FileStepCache(directory), "/pricing", null, model);

        Assert.Contains("Replay stopped (end-mismatch) after these actions:\n- navigate /customers\n", Text(model.Requests[0]), StringComparison.Ordinal);
        Assert.Equal(("agent-concluded", "end-mismatch", 1, 1), Info(step));
    }

    [Fact]
    public async Task Heals_stale_anchors_when_the_executor_settled_an_end_mismatch_without_acting()
    {
        var directory = CoreTests.TempCache();
        await RecordCustomersAsync(directory);

        // The executor looked, agreed the step was done, and recorded nothing more.
        var step = await StepAsync(CustomersWorld(marker: "Marker stored"), new FileStepCache(directory), "/pricing", Stored, Script());

        Assert.Equal("end-mismatch", step.Cache?.Reason);
        Assert.Equal(["navigate"], Entry(directory).Actions.Select(action => action.Kind));
    }

    [Fact]
    public async Task Evicts_instead_of_re_staging_when_the_executor_had_to_repair_after_an_end_mismatch()
    {
        var directory = CoreTests.TempCache();
        await RecordCustomersAsync(directory);
        var world = new DocumentWorld()
            .Map("/pricing", page => page.Heading("Pricing"))
            .Map("/customers", page =>
            {
                var saved = new DocumentElement { Role = "status", Name = "Marker saved", Hidden = true };
                page.Roots.Add(saved);
                page.Button("Save", () => saved.Hidden = false);
            });

        // The replayed flow did not produce its effect; the executor acted further.
        var step = await StepAsync(world, new FileStepCache(directory), "/pricing", Saved, Script(ModelResponses.Tap("button", "Save")));

        Assert.Equal("end-mismatch", step.Cache?.Reason);
        Assert.Empty(Directory.GetFiles(directory, "*.json"));
    }

    [Fact]
    public async Task Evicts_a_consumed_entry_when_the_step_then_fails_and_never_when_nothing_judged_the_app()
    {
        foreach (var (model, expected) in new[] { (Script(ModelResponses.Done("failed", "The customers are missing.")), 1), ((ScriptedModel?)null, 0) })
        {
            var directory = CoreTests.TempCache();
            await RecordCustomersAsync(directory);
            var cache = new CountingCache(directory);

            var step = await StepAsync(CustomersWorld(saved: false), cache, "/pricing", null, model);

            Assert.NotNull(step.Error);
            Assert.Equal(expected, cache.Deletes);
            Assert.Equal(0, cache.Writes);
        }
    }

    [Fact]
    public async Task Evicts_nothing_on_failure_when_no_replay_was_consumed()
    {
        var cache = new CountingCache(CoreTests.TempCache());

        var step = await StepAsync(StorageWorld(), cache, "/storage", null, Script(ModelResponses.Done("failed", "No marker.")));

        Assert.NotNull(step.Error);
        Assert.Equal(0, cache.Deletes);
    }

    [Fact]
    public async Task Neither_stages_nor_evicts_in_read_only_mode()
    {
        var directory = CoreTests.TempCache();
        await RecordCustomersAsync(directory);
        var cache = new CountingCache(directory);

        var step = await StepAsync(CustomersWorld(saved: false), cache, "/pricing", null, Script(ModelResponses.Done("failed", "No marker.")), mode: CacheMode.ReadOnly);

        Assert.NotNull(step.Error);
        Assert.Equal(0, cache.Deletes);
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task Self_finalizes_when_the_recorded_end_anchors_are_present_again()
    {
        var directory = CoreTests.TempCache();
        await RecordCustomersAsync(directory);

        var step = await StepAsync(CustomersWorld(), new FileStepCache(directory), "/pricing", null, Script());

        Assert.Equal(1, step.Replayed);
        Assert.Equal("self-finalized", step.Cache?.Mode);
    }

    [Fact]
    public async Task Refuses_to_self_finalize_when_the_recorded_end_path_no_longer_matches()
    {
        var directory = CoreTests.TempCache();
        await StepAsync(ProjectWorld("/customers"), new FileStepCache(directory), "/pricing", Saved, Script(ModelResponses.Tap("link", "Create project")));

        var step = await StepAsync(ProjectWorld("/moved-away"), new FileStepCache(directory), "/pricing", null, Script());

        Assert.Equal("end-mismatch", step.Cache?.Reason);
        Assert.Equal("agent-concluded", step.Cache?.Mode);
    }

    [Fact]
    public async Task Reads_the_start_capture_for_the_first_relocation_instead_of_capturing_the_same_screen_again()
    {
        var directory = CoreTests.TempCache();
        static DocumentWorld World() => new DocumentWorld().Map("/pricing", page =>
        {
            var status = page.Status("Marker draft");
            page.Button("Upgrade", () => status.Name = status.Text = "Marker saved");
        });
        var tap = ModelResponses.Tap("button", "Upgrade");
        Assert.Null((await RunAsync(World(), directory, "/pricing", Saved, tap)).Error);

        var step = await RunAsync(World(), directory, "/pricing", null, tap);
        Assert.Equal("self-finalized", step.Cache?.Mode);
        // One settled start capture serves the relocation; the end state is one raw look.
        Assert.Equal([SettleMode.HeldStill, SettleMode.Raw], step.Looks);
    }

    [Fact]
    public async Task Leaves_the_entry_file_untouched_across_replays_and_rewrites_it_after_a_hand_off_the_executor_healed()
    {
        var directory = CoreTests.TempCache();
        string File() => Assert.Single(Directory.GetFiles(directory, "*.json"));
        (string Bytes, DateTime Written) Snapshot() => (System.IO.File.ReadAllText(File()), System.IO.File.GetLastWriteTimeUtc(File()));

        // The recording run: a miss, then the flow and its effect are written.
        var recorded = await RecordCustomersAsync(directory);
        Assert.Equal("missed", recorded.Cache?.Mode);
        var written = Snapshot();
        Assert.Equal([("status", "Marker saved")], Entry(directory).Appeared.Select(anchor => (anchor.Role, anchor.Name)));

        // Two replays in a row: the file's bytes and write time never move.
        await Task.Delay(20);
        Assert.Equal("self-finalized", (await StepAsync(CustomersWorld(), new FileStepCache(directory), "/pricing", Saved, Script())).Cache?.Mode);
        Assert.Equal(written, Snapshot());
        Assert.Equal("self-finalized", (await StepAsync(CustomersWorld(), new FileStepCache(directory), "/pricing", Saved, Script())).Cache?.Mode);
        Assert.Equal(written, Snapshot());

        // A hand-off the executor settled without acting re-records with the live anchors.
        var healed = await StepAsync(CustomersWorld(marker: "Marker stored"), new FileStepCache(directory), "/pricing", Stored, Script());
        Assert.Equal("end-mismatch", healed.Cache?.Reason);
        Assert.NotEqual(written.Bytes, Snapshot().Bytes);
        Assert.Equal([("status", "Marker stored")], Entry(directory).Appeared.Select(anchor => (anchor.Role, anchor.Name)));

        // A replay nothing verified afterwards is implicated like a recording would be: evicted.
        var unconfirmed = await StepAsync(CustomersWorld(marker: "Marker stored"), new FileStepCache(directory), "/pricing", null, Script());
        Assert.Equal("self-finalized", unconfirmed.Cache?.Mode);
        Assert.Empty(Directory.GetFiles(directory, "*.json"));
    }

    // A passing check verifies the act, so it records.
    private static Task Saved(TestContext ctx) => Expect.That(ctx.Screen.GetByRole("status", "Marker saved")).ToBeVisibleAsync();

    private static Task Stored(TestContext ctx) => Expect.That(ctx.Screen.GetByRole("status", "Marker stored")).ToBeVisibleAsync();

    // A storage page whose save shows the saved marker.
    private static DocumentWorld StorageWorld() => new DocumentWorld().Map("/storage", page =>
    {
        page.Heading("Storage");
        var saved = new DocumentElement { Role = "status", Name = "Marker saved", Hidden = true };
        page.Roots.Add(saved);
        page.Button("Save marker", () => saved.Hidden = false);
    });

    // A pricing page, and a customers page that shows the marker.
    private static DocumentWorld CustomersWorld(bool saved = true, string marker = "Marker saved") => new DocumentWorld()
        .Map("/pricing", page => page.Heading("Pricing"))
        .Map("/customers", page =>
        {
            if (saved)
            {
                page.Status(marker);
            }
        });

    // A pricing page whose link opens the project page at `target`, which shows the saved marker.
    private static DocumentWorld ProjectWorld(string target) => new DocumentWorld()
        .Map("/pricing", page => page.Link("Create project", target))
        .Map(target, page => page.Status("Marker saved"));

    // Records a navigate from the pricing page to the customers page.
    private static Task<Step> RecordCustomersAsync(string directory)
    {
        return StepAsync(CustomersWorld(), new FileStepCache(directory), "/pricing", Saved, Script(ModelResponses.Call("navigate", new { url = "/customers" })));
    }

    // A model that takes the steps in order, then concludes that the step passed.
    private static ScriptedModel Script(params ModelResponse[] steps)
    {
        var next = 0;
        return new ScriptedModel(_ => next < steps.Length ? steps[next++] : ModelResponses.Done("passed", "Done."));
    }

    private static string Text(ModelRequest request) => string.Join('\n', request.Messages.Select(message => message.Content));

    private static (string?, string?, int?, int?) Info(Step step) => (step.Cache?.Mode, step.Cache?.Reason, step.Cache?.ReplayedActions, step.Cache?.TotalActions);

    private static CacheEntry Entry(string directory)
    {
        return JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json"))), EntryJson)!;
    }

    /// <summary>
    /// One test run with the old shape: open <paramref name="route"/>, then act with a model that takes
    /// <paramref name="steps"/> in order and concludes at once after a hand-off.
    /// </summary>
    private static Task<Step> RunAsync(DocumentWorld world, string directory, string route, Func<TestContext, Task>? check, params ModelResponse[] steps)
    {
        var next = 0;
        var model = new ScriptedModel(request => next < steps.Length && !Text(request).Contains("end-mismatch", StringComparison.Ordinal)
            ? steps[next++]
            : ModelResponses.Done("passed", "Done."));
        return StepAsync(world, new FileStepCache(directory), route, check, model);
    }

    private sealed record Step(Exception? Error, CacheInfo? Cache, int Replayed, IReadOnlyList<SettleMode> Looks);

    /// <summary>
    /// One attempt: open <paramref name="route"/>, act once, then run <paramref name="check"/>, which
    /// verifies the act when it passes. A null model is a model outage.
    /// </summary>
    private static async Task<Step> StepAsync(
        DocumentWorld world,
        IStepCache cache,
        string route,
        Func<TestContext, Task>? check,
        IAgentModel? model,
        IReadOnlyDictionary<string, object?>? parameters = null,
        int attempt = 1,
        CacheMode mode = CacheMode.ReadWrite)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            Model = model,
            BaseUrl = "https://billing.test",
            Cache = cache,
            CacheMode = mode,
            Attempt = attempt,
            TestTitle = "billing > case",
            AssertionTimeout = TimeSpan.FromSeconds(2),
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            ReplayTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        CacheInfo? info = null;
        Exception? error = null;
        try
        {
            await session.Context.App.OpenAsync(route);
            info = (await session.Context.Agent.ActAsync("open billing", new ActOptions { Params = parameters })).Cache;
            if (check is not null)
            {
                await check(session.Context);
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return new Step(error, info, session.Replayed, [.. session.Context.Agent.Feed.Looks]);
    }

    /// <summary>A store whose every read rejects, as a store behind a network can.</summary>
    private sealed class ThrowingCache : IStepCache
    {
        public CacheLookup Read(string key) => throw new InvalidOperationException("redis connection refused");

        public void Write(string key, CacheEntry entry)
        {
        }

        public void Delete(string key)
        {
        }
    }

    /// <summary>A store that hits with an entry whose actions are not a list.</summary>
    private sealed class MalformedCache : IStepCache
    {
        public CacheLookup Read(string key) => new() { Entry = JsonSerializer.Deserialize<CacheEntry>("""{ "schema": 1, "actions": null }""", EntryJson) };

        public void Write(string key, CacheEntry entry)
        {
        }

        public void Delete(string key)
        {
        }
    }

    /// <summary>A file store that counts its reads, writes, and deletes.</summary>
    private sealed class CountingCache(string directory) : IStepCache
    {
        private readonly FileStepCache _inner = new(directory);

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public int Deletes { get; private set; }

        public CacheLookup Read(string key)
        {
            Reads++;
            return _inner.Read(key);
        }

        public void Write(string key, CacheEntry entry)
        {
            Writes++;
            _inner.Write(key, entry);
        }

        public void Delete(string key)
        {
            Deletes++;
            _inner.Delete(key);
        }
    }
}
