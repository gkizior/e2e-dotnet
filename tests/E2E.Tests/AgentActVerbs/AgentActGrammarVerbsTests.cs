// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Tests.AgentAct;
using static E2E.Tests.AgentActVerbs.GesturesSession;

namespace E2E.Tests.AgentActVerbs;

/// <summary>
/// <c>agent.act</c> grammar verbs on the gestures page, driven by a scripted model. Upstream also runs hover,
/// the right-click and long-press variants, drag, upload, a repeated key press, and a hover at a bare point.
/// This port has no such tool. Upstream also checks each step's engine events, their detail line, the policy
/// events, and the changes a tool result lists. The port has no events, and its tool result lists the whole screen.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class AgentActGrammarVerbsTests
{
    [Fact]
    public async Task Dispatches_the_double_tap_variant_as_its_own_action()
    {
        var (result, model) = await RunOneAsync(DoubleTapFlow);

        Assert.Equal(1, result.Actions);
        var toolResult = CounterPage.ToolResults(model)[0];
        Assert.StartsWith("double-tapped button \"Tap me twice\"", toolResult, StringComparison.Ordinal);
        Assert.Contains("status \"Gesture state\" text=\"double-tapped\"", toolResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checks_a_box_twice_and_the_second_check_flips_nothing()
    {
        var (result, model) = await RunOneAsync(ReplayFlows.Single(entry => entry.Title == "checks the box and leaves a checked box alone"));

        // Two check actions: the second finds the box already checked and flips nothing.
        Assert.Equal(2, result.Actions);
        var toolResults = CounterPage.ToolResults(model);
        Assert.StartsWith("checked checkbox \"Agree to terms\"", toolResults[0], StringComparison.Ordinal);
        Assert.Contains("status \"Gesture state\" text=\"agreed: true\"", toolResults[0], StringComparison.Ordinal);
        Assert.Contains("status \"Gesture state\" text=\"agreed: true\"", toolResults[1], StringComparison.Ordinal);
        Assert.DoesNotContain("failed", toolResults[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checks_a_radio_the_pick_replaced_in_one_action()
    {
        var (result, model) = await RunOneAsync(ReplayFlows.Single(entry => entry.Title == "checks a radio the pick replaces with its summary"));

        Assert.Equal(1, result.Actions);
        Assert.Contains("status \"Gesture state\" text=\"delivery: Express\"", CounterPage.ToolResults(model)[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scrolls_a_listed_node_into_view_in_one_action()
    {
        var (result, model) = await RunOneAsync(ReplayFlows.Single(entry => entry.Title == "scrolls a listed node into view"));

        Assert.Equal(1, result.Actions);
        Assert.StartsWith("scrolled paragraph into view", CounterPage.ToolResults(model)[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pages_a_windowed_list_to_a_row_by_its_text_in_one_action()
    {
        var (result, model) = await RunOneAsync(ReplayFlows.Single(entry => entry.Title == "pages a windowed list to a row it has not rendered"));

        // Several pages of the list, one action of the budget.
        Assert.Equal(1, result.Actions);
        var toolResult = CounterPage.ToolResults(model)[0];
        Assert.StartsWith("scrolled down until \"Row 24\" was in view", toolResult, StringComparison.Ordinal);
        Assert.Contains("Row 24 · Golden", toolResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Goes_back_and_shows_the_screen_it_returned_to()
    {
        var (result, model) = await RunOneAsync(ReplayFlows.Single(entry => entry.Title == "opens a page and comes back"));

        // The tap, then the back.
        Assert.Equal(2, result.Actions);
        var toolResults = CounterPage.ToolResults(model);
        Assert.Contains("heading \"About\"", toolResults[0], StringComparison.Ordinal);
        Assert.StartsWith("navigated back", toolResults[1], StringComparison.Ordinal);
        Assert.Contains("heading \"Gestures\"", toolResults[1], StringComparison.Ordinal);
    }

    private static async Task<(ActResult Result, ScriptedModel Model)> RunOneAsync(Flow flow)
    {
        using var site = await StartSiteAsync();
        var directory = Path.Combine(Path.GetTempPath(), "e2e-verbs", Guid.NewGuid().ToString("n"));
        var model = ModelFor(flow);
        var result = await RunFlowAsync(site, flow, model, directory);
        return (result, model);
    }
}
