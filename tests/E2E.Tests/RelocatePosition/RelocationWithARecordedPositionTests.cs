// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.StepCache.StepTraceSessionTests;

namespace E2E.Tests.RelocatePosition;

/// <summary>
/// The port records no position, so the other cases of this file are not ported: "records where a target stands among the
/// twins its description matches", "records nothing for a target its description already names alone", "counts twins inside
/// the recorded container only", "relocates to the same twin when the live screen shows the same number of them", "diverges
/// as ambiguous when a twin appeared or vanished", "names the position in the action summary and carries it on the target",
/// and "survives the store and rejects a malformed position".
/// </summary>
public sealed class RelocationWithARecordedPositionTests
{
    /// <summary>
    /// Partial port of "still diverges without a recorded position": the port names the reason, and lists no candidates.
    /// </summary>
    [Fact]
    public async Task Diverges_as_ambiguous_without_a_recorded_position_and_names_no_candidates()
    {
        var directory = CoreTests.TempCache();
        var single = new DocumentWorld().Map("/home", page =>
        {
            var status = page.Status("Idle");
            page.Button("Set up", () => status.Name = status.Text = "Configured");
        });
        var recorded = await StepAsync(single, new FileStepCache(directory), "/home", ctx => Expect.That(ctx.Screen.GetByRole("status")).ToBeVisibleAsync(), Script(ModelResponses.Tap("button", "Set up")));
        Assert.Null(recorded.Error);
        var twins = new DocumentWorld().Map("/home", page =>
        {
            page.Status("Idle");
            page.Button("Set up");
            page.Button("Set up");
        });

        var replay = await StepAsync(twins, new FileStepCache(directory), "/home", null, Script(ModelResponses.Done("passed", "Done.")));

        Assert.Equal(("missed", "target-ambiguous"), (replay.Cache?.Mode, replay.Cache?.Reason));
    }
}
