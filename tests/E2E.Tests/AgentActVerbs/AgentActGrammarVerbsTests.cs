// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.AgentActVerbs.GesturesSession;

namespace E2E.Tests.AgentActVerbs;

/// <summary><c>agent.act</c> grammar verbs on the gestures page, driven by a scripted model.</summary>
[Collection(BrowserCollection.Name)]
public sealed class AgentActGrammarVerbsTests
{
    // Upstream also checks the step's engine events, their detail line, and the
    // changes the tool result lists. The port has no engine events and its tool
    // result lists no changes, so only the step's outcome is ported.
    [Fact]
    public async Task Records_a_check_whose_radio_the_pick_replaced_the_click_landed()
    {
        using var site = await StartSiteAsync();
        var directory = Path.Combine(Path.GetTempPath(), "e2e-verbs", Guid.NewGuid().ToString("n"));
        var flow = ReplayFlows.Single(entry => entry.Title == "checks a radio the pick replaces with its summary");
        await RunFlowAsync(site, flow, ModelFor(flow), directory);
    }
}
