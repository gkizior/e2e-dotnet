// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.StepCache.StepTraceSessionTests;

namespace E2E.Tests.TraceReplay;

/// <summary>
/// <c>verifyEndState</c> is the end check of a replay. Not ported: "treats a surface that cannot be observed as a mismatch"
/// (the port's end check does not catch a failed look), and the three waits that size the end wait from the recording or
/// give it up when the surface turns to pixels (the port waits its replay timeout and has no pixels). The other cases are covered
/// by <c>StepTraceSessionTests</c>, the end check being part of a replayed act.
/// </summary>
public sealed class VerifyEndStateTests
{
    [Fact]
    public async Task Rethrows_a_runtime_hard_stop_raised_while_looking()
    {
        var directory = CoreTests.TempCache();
        var stopping = false;
        var replaying = false;
        DocumentWorld World() => new DocumentWorld().Map("/home", page =>
        {
            var status = page.Status("Idle");
            page.Button("Upgrade", () =>
            {
                status.Name = status.Text = "Upgraded";
                stopping = replaying;
            });
        });
        var recorded = await StepAsync(World(), new FileStepCache(directory), "/home", ctx => Expect.That(ctx.Screen.GetByRole("status")).ToBeVisibleAsync(), Script(ModelResponses.Tap("button", "Upgrade")));
        Assert.Null(recorded.Error);
        replaying = true;

        // The tap succeeds, and the look that checks its end state is the one that runs out of time.
        var replay = await StepAsync(
            World(),
            new FileStepCache(directory),
            "/home",
            null,
            Script(),
            wrap: engine => new LosingEngine(engine, () => stopping, () => stopping = false, () => new AgentException("STEP_TIMEOUT", "out of time")));

        Assert.Equal("STEP_TIMEOUT", Assert.IsType<AgentException>(replay.Error).Code);
    }
}
