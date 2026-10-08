// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.ResolveExpression;

/// <summary>
/// The reference locator semantics. The screen is an iOS Settings page as the
/// mobile engine projects one: an application node, a navigation bar with a
/// button, a cell echoing its static text, a switch, two fields, a hidden button.
/// Not ported: "refuses a role query whose value is not a string" (the port's role is a typed string), "drops hidden
/// nodes from any query kind when it says visible" (the port's role options cannot ask for the hidden state), "hands a
/// selector to the platform hook, and refuses one without it" (no selector queries), and "rejects frames, since a
/// semantic tree has no nested documents" (no frame queries).
/// </summary>
public sealed class ResolveExpressionTests
{
    private static readonly SemanticNode[] Settings =
    [
        new()
        {
            Ref = "app",
            Role = "application",
            Name = "Settings",
            Text = "Settings",
            Children =
            [
                new()
                {
                    Ref = "bar",
                    Role = "navigation",
                    Name = "General",
                    Text = "General",
                    Children = [new() { Ref = "back", Role = "button", Name = "Back", Text = "Back", TestId = "BackButton" }],
                },
                new()
                {
                    Ref = "about",
                    Role = "listitem",
                    Name = "About",
                    Text = "About",
                    TestId = "ABOUT",
                    Children = [new() { Ref = "about-text", Role = "text", Name = "About", Text = "About" }],
                },
                new() { Ref = "airplane", Role = "switch", Name = "Airplane Mode", Text = "Airplane Mode", States = new NodeStates { Checked = false } },
                new() { Ref = "search", Role = "textbox", Name = "Search", Text = "Search", Value = "wifi" },
                new() { Ref = "password", Role = "textbox", Name = "Password", Text = "Password", InputPurpose = "password", States = new NodeStates { Secure = true } },
                new() { Ref = "hidden", Role = "button", Name = "Hidden", Text = "Hidden", States = new NodeStates { Hidden = true, Disabled = true } },
                new() { Ref = "scroller", Role = "listitem", Name = "Scroller", Text = "Scroller" },
            ],
        },
    ];

    private static readonly Screen Screen = ScreenFixture.Create(Settings);

    [Fact]
    public async Task Answers_role_queries_with_name_filters_and_skips_hidden_nodes()
    {
        Assert.Equal(["back"], await Ids(Screen.GetByRole("button")));
        Assert.Equal(["back"], await Ids(Screen.GetByRole("button", "Back")));
        Assert.Equal(["back"], await Ids(Screen.GetByRole("button", new Regex("^ba", RegexOptions.IgnoreCase))));
        Assert.Empty(await Ids(Screen.GetByRole("switch", new RoleOptions { Checked = true })));
        Assert.Equal(["airplane"], await Ids(Screen.GetByRole("switch", new RoleOptions { Checked = false })));
        Assert.Equal(["about", "scroller"], await Ids(Screen.GetByRole("listitem")));
    }

    [Fact]
    public async Task Requires_a_heading_level_exactly_so_a_tree_without_levels_answers_nothing()
    {
        var headings = ScreenFixture.Create(
            new SemanticNode { Ref = "h1", Role = "heading", Level = 1 },
            new SemanticNode { Ref = "h2", Role = "heading", Level = 2 },
            new SemanticNode { Ref = "h", Role = "heading" });
        Assert.Equal(["h2"], await Ids(headings.GetByRole("heading", new RoleOptions { Level = 2 })));
        Assert.Equal(["h1", "h2", "h"], await Ids(headings.GetByRole("heading")));
        Assert.Empty(await Ids(Screen.GetByRole("listitem", new RoleOptions { Level = 1 })));
    }

    [Fact]
    public async Task Answers_label_text_display_value_placeholder_and_test_id_queries()
    {
        Assert.Equal(["search"], await Ids(Screen.GetByLabel("Search")));
        Assert.Equal(["about-text"], await Ids(Screen.GetByText("About")));
        Assert.Equal(["about-text"], await Ids(Screen.GetByText("abo", exact: false)));
        Assert.Equal(["search"], await Ids(Screen.GetByDisplayValue("wifi")));
        Assert.Empty(await Ids(Screen.GetByDisplayValue("hunter2")));
        Assert.Equal(["about"], await Ids(Screen.GetByTestId("ABOUT")));
        Assert.Empty(await Ids(Screen.GetByPlaceholder("anything")));
        // The port's node carries the placeholder as a field; upstream's carries it as an attribute.
        var field = ScreenFixture.Create(new SemanticNode { Ref = "email", Role = "textbox", Placeholder = "you@example.test" });
        Assert.Equal(["email"], await Ids(field.GetByPlaceholder("you@example.test")));
    }

    [Fact]
    public async Task Answers_text_and_label_queries_with_the_innermost_match_when_an_ancestor_echoes_the_text()
    {
        Assert.Equal(["text"], (await Screen.GetByText("About").ResolveAsync(CancellationToken.None)).Select(match => match.Role));
        Assert.Equal(["about-text"], await Ids(Screen.GetByLabel("About")));

        // The echoing cell still answers role queries, and filters by its subtree text.
        Assert.Equal(["about"], await Ids(Screen.GetByRole("listitem", "About")));
        Assert.Equal(["about"], await Ids(Screen.GetByRole("listitem").Filter("About")));
    }

    [Fact]
    public async Task Reads_text_from_the_name_or_the_visible_text()
    {
        var nodes = ScreenFixture.Create(
            new SemanticNode { Ref = "named", Role = "button", Name = "Save" },
            new SemanticNode { Ref = "texted", Text = "Save" },
            new SemanticNode { Ref = "other", Name = "Cancel" });
        Assert.Equal(["named", "texted"], await Ids(nodes.GetByText("Save")));
        Assert.Equal(["named"], await Ids(nodes.GetByLabel("Save")));
    }

    [Fact]
    public async Task Scopes_filters_and_indexes()
    {
        Assert.Equal(["back"], await Ids(Screen.GetByRole("navigation").GetByRole("button")));
        Assert.Equal(["about"], await Ids(Screen.GetByRole("listitem").Filter("About")));
        Assert.Equal(["about"], await Ids(Screen.GetByRole("listitem").Filter(Screen.GetByRole("text"))));
        Assert.Equal(["about"], await Ids(Screen.GetByRole("listitem").First()));
        Assert.Equal(["scroller"], await Ids(Screen.GetByRole("listitem").Last()));
        Assert.Empty(await Ids(Screen.GetByRole("listitem").Nth(5)));
    }

    [Fact]
    public async Task Searches_strict_descendants_of_a_scope_each_once_in_document_order()
    {
        Assert.Empty(await Ids(Screen.GetByRole("application").GetByRole("application")));
        var nested = ScreenFixture.Create(new SemanticNode
        {
            Ref = "outer",
            Role = "group",
            Children =
            [
                new() { Ref = "inner", Role = "group", Children = [new() { Ref = "leaf", Role = "button" }] },
                new() { Ref = "sibling", Role = "button" },
            ],
        });
        Assert.Equal(["leaf", "sibling"], await Ids(nested.GetByRole("group").GetByRole("button")));
        Assert.Empty(await Ids(nested.GetByRole("list").GetByRole("button")));
    }

    [Fact]
    public async Task Resolves_has_inside_each_candidate_never_against_the_whole_screen()
    {
        var list = ScreenFixture.Create(
            new SemanticNode { Ref = "alpha", Role = "listitem", Text = "Alpha", Children = [new() { Ref = "remove", Role = "button", Name = "Remove" }] },
            new SemanticNode { Ref = "beta", Role = "listitem", Text = "Beta", Children = [new() { Ref = "note", Role = "text", Text = "Read only" }] });
        var items = list.GetByRole("listitem");
        Assert.Equal(["alpha"], await Ids(items.Filter(list.GetByRole("button", "Remove"))));
        Assert.Equal(["beta"], await Ids(items.Filter(list.GetByText("Read only"))));
        Assert.Empty(await Ids(items.Filter(list.GetByRole("link"))));
        Assert.Equal(["alpha"], await Ids(items.Filter("Alpha").Filter(list.GetByRole("button"))));
        Assert.Empty(await Ids(items.Filter("Beta").Filter(list.GetByRole("button"))));
    }

    [Fact]
    public async Task Matches_hasText_against_the_name_text_or_value_of_the_node_or_a_descendant()
    {
        var rows = ScreenFixture.Create(
            new SemanticNode { Ref = "by-value", Role = "row", Children = [new() { Ref = "field", Role = "textbox", Value = "draft" }] },
            new SemanticNode { Ref = "by-name", Role = "row", Name = "draft" },
            new SemanticNode { Ref = "other", Role = "row", Text = "final" });
        Assert.Equal(["by-value", "by-name"], await Ids(rows.GetByRole("row").Filter("draft")));
        Assert.Equal(["other"], await Ids(rows.GetByRole("row").Filter(new Regex("FIN", RegexOptions.IgnoreCase))));
    }

    private static async Task<IEnumerable<string>> Ids(Locator locator) =>
        (await locator.ResolveAsync(CancellationToken.None)).Select(match => match.Ref);
}
