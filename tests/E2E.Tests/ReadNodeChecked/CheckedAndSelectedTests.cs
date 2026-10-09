// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Playwright;

namespace E2E.Tests.ReadNodeChecked;

[Collection(BrowserCollection.Name)]
public sealed class CheckedAndSelectedTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Reads_selected_boolean_values_without_case_sensitivity_while_native_option_state_wins()
    {
        await chromium.Page.SetContentAsync("""
            <div role="tablist">
              <button role="tab" aria-selected="TRUE" data-testid="upper">All</button>
              <button role="tab" aria-selected="TrUe" data-testid="mixed">Recent</button>
              <button role="tab" aria-selected="FALSE" data-testid="off">Archived</button>
            </div>
            <select aria-label="Plan" size="3">
              <option data-testid="native-on" selected aria-selected="FALSE">Team</option>
              <option data-testid="native-off" aria-selected="TRUE">Solo</option>
            </select>
            <div role="button" data-testid="embedded">Pick <div role="listbox"><div role="option" aria-selected="true">Chosen</div><div role="option" aria-selected="false">Other</div></div></div>
            """);
        var nodes = await chromium.CaptureAsync();
        Assert.True(Node(nodes, "upper").Selected);
        Assert.True(Node(nodes, "mixed").Selected);
        Assert.False(Node(nodes, "off").Selected);
        Assert.True(Node(nodes, "native-on").Selected);
        Assert.False(Node(nodes, "native-off").Selected);
        Assert.Equal("Pick Chosen", Node(nodes, "embedded").Name);
        Assert.Equal(["All", "Recent"], await chromium.Page.GetByRole(AriaRole.Tab, new() { Selected = true }).AllTextContentsAsync());
        Assert.Equal(1, await chromium.Page.GetByRole(AriaRole.Button, new() { Name = "Pick Chosen", Exact = true }).CountAsync());
    }

    [Fact]
    public async Task Excludes_uppercase_hidden_subtrees_from_observations_and_names_but_reads_hidden_references_whole()
    {
        await chromium.Page.SetContentAsync("""
            <div aria-hidden="TRUE"><button data-testid="hidden" aria-hidden="false">Hidden action</button></div>
            <button data-testid="save">Save<span aria-hidden="TrUe"> decoration</span></button>
            <button data-testid="shown" aria-hidden="FALSE">Shown</button>
            <button data-testid="referenced" aria-labelledby="reference">Fallback</button>
            <div aria-hidden="TRUE"><span id="reference">Label <i style="display:none">whole</i></span></div>
            """);
        var nodes = await chromium.CaptureAsync();
        Assert.True(Node(nodes, "hidden").AriaHidden);
        Assert.Equal("Save", Node(nodes, "save").Name);
        Assert.Equal("Shown", Node(nodes, "shown").Name);
        Assert.Equal("Label whole", Node(nodes, "referenced").Name);
        Assert.Equal(1, await chromium.Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).CountAsync());
        Assert.Equal(0, await chromium.Page.GetByRole(AriaRole.Button, new() { Name = "Hidden action", Exact = true }).CountAsync());
        Assert.Equal(1, await chromium.Page.GetByRole(AriaRole.Button, new() { Name = "Label whole", Exact = true }).CountAsync());
    }

    private static ReadNode Node(IReadOnlyList<ReadNode> nodes, string testId)
    {
        return nodes.Single(node => node.TestId == testId);
    }
}
