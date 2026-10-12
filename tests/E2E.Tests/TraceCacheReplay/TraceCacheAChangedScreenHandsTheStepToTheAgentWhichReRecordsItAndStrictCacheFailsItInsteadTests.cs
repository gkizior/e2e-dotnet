// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Tests.AgentTraceCache;

namespace E2E.Tests.TraceCacheReplay;

/// <summary>
/// The second run changes the screen under the recording. Upstream runs seven flows twice through the runner, in
/// strict and lenient mode, over a fixture app with a <c>renamed</c> variant. The port records and replays the three
/// flows that diverge, one session each.
/// </summary>
public sealed class TraceCacheAChangedScreenHandsTheStepToTheAgentWhichReRecordsItAndStrictCacheFailsItInsteadTests
{
    private static readonly Flow Company = new(
        "creates a company",
        "create the company Acme",
        "/companies/new",
        "/companies/new",
        renamed => new DocumentWorld().Map("/companies/new", page =>
        {
            var status = page.Status("Company", hidden: true);
            page.Textbox("Name");
            page.Button(renamed ? "Save company" : "Save", () => status.Hidden = false);
        }),
        renamed => ["fill Name", renamed ? "Save company" : "Save"]);

    private static readonly Flow Archives = new(
        "archives a record",
        "archive the record",
        "/records/1a2b3c4d5e6f",
        "/drafts/1a2b3c4d5e6f",
        _ => new DocumentWorld()
            .Map("/records/1a2b3c4d5e6f", page => Archivable(page))
            .Map("/drafts/1a2b3c4d5e6f", page => Archivable(page)),
        _ => ["Archive"]);

    private static readonly Flow Retires = new(
        "retires a record",
        "retire the record",
        "/records/7a8b9c0d1e2f",
        "/records/7a8b9c0d1e2f",
        renamed => new DocumentWorld().Map("/records/7a8b9c0d1e2f", page =>
        {
            var status = page.Status("Retired", hidden: true);
            page.Button(renamed ? "Remove" : "Retire", () => status.Hidden = false);
        }),
        renamed => [renamed ? "Remove" : "Retire"]);

    [Fact]
    public async Task Fails_every_diverged_step_with_REPLAY_STALE_naming_its_reason_without_a_model_call()
    {
        foreach (var (flow, reason) in new[] { (Company, "target-not-found"), (Archives, "wrong-context"), (Retires, "target-not-found") })
        {
            var directory = TempCache();
            Assert.Null((await RunAsync(flow, directory, replay: false, strict: false)).Error);
            var recorded = Directory.GetFiles(directory).Single();
            var bytes = await File.ReadAllBytesAsync(recorded);

            var stale = await RunAsync(flow, directory, replay: true, strict: true);

            var error = Assert.IsType<AgentException>(stale.Error);
            Assert.Equal("REPLAY_STALE", error.Code);
            Assert.Contains(reason, error.Message, StringComparison.Ordinal);
            Assert.Contains("re-record it without cache.strict", error.Message, StringComparison.Ordinal);
            Assert.Equal(0, stale.ModelCalls);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(recorded));
        }
    }

    [Fact]
    public async Task Passes_every_flow_on_the_lenient_run()
    {
        foreach (var flow in new[] { Company, Archives, Retires })
        {
            var directory = TempCache();
            Assert.Null((await RunAsync(flow, directory, replay: false, strict: false)).Error);
            Assert.Null((await RunAsync(flow, directory, replay: true, strict: false)).Error);
        }
    }

    // Upstream also reads the entry the lenient run leaves, which keeps the renamed button's old name. The port rewrites the entry after a hand-off.
    [Fact]
    public async Task Replays_the_typing_hands_off_at_the_renamed_button_and_tells_the_model_what_already_ran()
    {
        var directory = TempCache();
        await RunAsync(Company, directory, replay: false, strict: false);

        var replayed = await RunAsync(Company, directory, replay: true, strict: false);

        Assert.Null(replayed.Error);
        Assert.Equal(("agent-concluded", "target-not-found", 1, 2), (replayed.Result?.Cache?.Mode, replayed.Result?.Cache?.Reason, replayed.Result?.Cache?.ReplayedActions, replayed.Result?.Cache?.TotalActions));
        Assert.True(replayed.ModelCalls > 0);
        var notice = replayed.Model.Requests[0].Messages[0].Content!;
        Assert.Contains("Replay stopped (target-not-found) after these actions:\n- fill textbox \"Name\"", notice, StringComparison.Ordinal);
        Assert.Equal(["fill", "tap"], Entry(directory).Actions.Select(action => action.Kind));
        Assert.Equal("Save company", Entry(directory).Actions[1].Name);
    }

    [Fact]
    public async Task Misses_with_wrong_context_on_another_route_and_records_the_new_screen()
    {
        var directory = TempCache();
        await RunAsync(Archives, directory, replay: false, strict: false);

        var replayed = await RunAsync(Archives, directory, replay: true, strict: false);

        Assert.Null(replayed.Error);
        Assert.Equal(("missed", "wrong-context"), (replayed.Result?.Cache?.Mode, replayed.Result?.Cache?.Reason));
        Assert.True(replayed.ModelCalls > 0);
        Assert.Equal("/drafts/1a2b3c4d5e6f", Entry(directory).Route);
    }

    [Fact]
    public async Task Misses_with_target_not_found_when_the_first_control_vanished_and_heals_the_entry()
    {
        var directory = TempCache();
        await RunAsync(Retires, directory, replay: false, strict: false);

        var replayed = await RunAsync(Retires, directory, replay: true, strict: false);

        Assert.Null(replayed.Error);
        Assert.Equal(("missed", "target-not-found"), (replayed.Result?.Cache?.Mode, replayed.Result?.Cache?.Reason));
        Assert.True(replayed.ModelCalls > 0);
        var action = Assert.Single(Entry(directory).Actions);
        Assert.Equal(("tap", "button", "Remove"), (action.Kind, action.Role, action.Name));
    }

    private static void Archivable(DocumentPage page)
    {
        var status = page.Status("Archived", hidden: true);
        page.Button("Archive", () => status.Hidden = false);
    }

    private static string TempCache() => Path.Combine(Path.GetTempPath(), "e2e-diverged", Guid.NewGuid().ToString("n"));

    private static CacheEntry Entry(string directory)
    {
        var key = Path.GetFileNameWithoutExtension(Directory.GetFiles(directory).Single());
        return new FileStepCache(directory).Read(key).Entry!;
    }

    // Recording runs on the original screen. The replay runs on the changed one.
    private static async Task<Run> RunAsync(Flow flow, string directory, bool replay, bool strict)
    {
        var model = new ScriptedModel(request =>
        {
            // A fill of the Name field comes first when the flow has one, then one tap per control, in order.
            var steps = flow.Steps(replay);
            var done = request.Messages.Count(message => message.Role == "tool");
            var next = done + TwoTapCounter.Replayed(request.Messages[0].Content!);
            if (next >= steps.Length)
            {
                return ModelResponses.Done("passed", flow.Title + " done");
            }

            return steps[next].StartsWith("fill ", StringComparison.Ordinal)
                ? ModelResponses.Fill("textbox", steps[next]["fill ".Length..], "Acme")
                : ModelResponses.Tap("button", steps[next]);
        });
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(flow.World(replay)),
            Model = model,
            BaseUrl = "https://app.test",
            Cache = new FileStepCache(directory),
            CacheEnabled = true,
            CacheMode = CacheMode.ReadWrite,
            CacheStrict = strict,
            TestTitle = flow.Title,
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            ReplayTimeout = TimeSpan.FromMilliseconds(300),
        });
        await session.App.OpenAsync(replay ? flow.ReplayRoute : flow.RecordRoute);
        ActResult? result = null;
        Exception? error = null;
        try
        {
            result = await session.Agent.ActAsync(flow.Instruction);
            await Expect.That(session.Screen.GetByRole("status")).ToBeVisibleAsync();
        }
        catch (AgentException ex)
        {
            error = ex;
        }

        session.Complete(error);
        return new Run(result, error, session.ModelCalls, model);
    }

    private sealed record Flow(string Title, string Instruction, string RecordRoute, string ReplayRoute, Func<bool, DocumentWorld> World, Func<bool, string[]> Steps);

    private sealed record Run(ActResult? Result, Exception? Error, int ModelCalls, ScriptedModel Model);
}
