// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentLedger;

/// <summary>
/// The port hands later steps a list of the summaries of the steps before them, with no api, status, byte limit, or
/// step record behind it. So these upstream its are not ported: "drops the oldest entries and reports how many were
/// omitted", "quotes the step explanation as a bounded handoff", "hands later steps the recorded verdict of a replayed
/// step, not the replay notice", and "strips control characters from untrusted labels and handoffs".
/// </summary>
public sealed class SerializeLedgerTests
{
    /// <summary>Partial: the port lists each step's summary under "Completed steps:", with no step number, api, or status.</summary>
    [Fact]
    public async Task Lists_the_summaries_of_the_completed_steps_in_the_order_they_ran()
    {
        var summaries = new[] { "opened the billing page", "chose the Pro plan" };
        var calls = 0;
        var model = new ScriptedModel(_ => ModelResponses.Done("passed", calls < summaries.Length ? summaries[calls++] : "paid"));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            TestTitle = "ledger order",
        });
        await session.App.OpenAsync("/settings/billing");

        await session.Agent.ActAsync("open billing");
        await session.Agent.ActAsync("choose the plan");
        await session.Agent.ActAsync("pay");

        var opening = model.Requests[2].Messages[0].Content;
        Assert.Contains("Completed steps:\n- opened the billing page\n- chose the Pro plan\n", opening, StringComparison.Ordinal);
        session.Complete();
    }
}
