// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentAct;

public sealed class LoopGuardsAndTranscriptsTests
{
    // Upstream also persists the executor transcript as a log artifact under --debug and checks it.
    // The port has no debug transcript, so only the verdict and the call count are ported.
    [Fact]
    public async Task Forces_a_verdict_when_the_model_repeats_itself()
    {
        var model = new ScriptedModel(request =>
        {
            if (request.Tools.Count == 1 && request.Tools[0].Name == "done")
            {
                return ModelResponses.Done("failed", "stuck repeating the same tap");
            }

            return ModelResponses.Tap("button", "Upgrade to Pro");
        });

        // Each tap changes the screen, so the same tap keeps succeeding and settles at once.
        var world = new DocumentWorld().Map("/settings/billing", page =>
        {
            var count = 0;
            var total = page.Paragraph("Taps: 0");
            page.Button("Upgrade to Pro", () => total.Text = "Taps: " + ++count);
        });
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("keep poking the same thing forever");
            },
            world,
            model);

        Assert.Contains("stuck repeating the same tap", Assert.IsType<AgentException>(attempt.Error).Message, StringComparison.Ordinal);
        // Five identical calls trip the stop; the forced turn concludes. Without the guard this
        // model would burn the whole 25-call budget.
        Assert.True(model.CallCount <= 8, "model calls: " + model.CallCount);
    }
}
