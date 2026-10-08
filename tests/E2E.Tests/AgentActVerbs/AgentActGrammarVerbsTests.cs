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
    // result lists no changes, so only the step's outcome and tool result are ported.
    [Fact]
    public async Task Records_a_check_whose_radio_the_pick_replaced_the_click_landed()
    {
        using var site = await StartSiteAsync();
        var directory = Path.Combine(Path.GetTempPath(), "e2e-verbs", Guid.NewGuid().ToString("n"));
        var flow = ReplayFlows.Single(entry => entry.Title == "checks a radio the pick replaces with its summary");
        await RunFlowAsync(site, flow, ModelFor(flow), directory);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/176: the screen the model reads drops a named node's text")]
    public async Task Pages_a_windowed_list_to_a_row_by_its_text_as_one_action_and_brings_it_into_view()
    {
        using var site = await StartSiteAsync();
        var directory = Path.Combine(Path.GetTempPath(), "e2e-verbs", Guid.NewGuid().ToString("n"));
        var flow = ReplayFlows.Single(entry => entry.Title == "pages a windowed list to a row it has not rendered");
        var model = ModelFor(flow);
        var result = await RunFlowAsync(site, flow, model, directory);

        // Several pages of the list, one action of the budget.
        Assert.Equal(1, result.Actions);
        var toolResult = model.Requests[1].Messages.First(message => message.Role == "tool").Content;
        Assert.StartsWith("scrolled down until \"Row 24\" was in view", toolResult, StringComparison.Ordinal);
        Assert.Contains("Row 24 · Golden", toolResult, StringComparison.Ordinal);
    }
}
