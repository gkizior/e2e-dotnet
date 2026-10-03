// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests;

public sealed class ScreenQueryTests
{
    private int _next;

    [Fact]
    public async Task Regex_matches_in_every_query_kind()
    {
        var screen = ScreenOf(
            Node("button", "Save draft"),
            Node(null, text: "Total: 42"),
            Node("textbox", "Email address", value: "ada@example.test", placeholder: "you@example.test"),
            Node(null, testId: "row-17"));

        Assert.Single(await Resolve(screen.GetByRole("button", new Regex("^save", RegexOptions.IgnoreCase))));
        Assert.Single(await Resolve(screen.GetByText(new Regex(@"Total: \d+"))));
        Assert.Single(await Resolve(screen.GetByLabel(new Regex("address$"))));
        Assert.Empty(await Resolve(screen.GetByLabel(new Regex("^email"))));
        Assert.Single(await Resolve(screen.GetByPlaceholder(new Regex("@example"))));
        Assert.Single(await Resolve(screen.GetByDisplayValue(new Regex("^ada@"))));
        Assert.Single(await Resolve(screen.GetByTestId(new Regex(@"^row-\d+$"))));
    }

    [Fact]
    public async Task Display_value_matches_the_current_value()
    {
        var screen = ScreenOf(Node("textbox", "First", value: "Ada"), Node("textbox", "Last", value: "Lovelace"));

        var match = Assert.Single(await Resolve(screen.GetByDisplayValue("Lovelace")));
        Assert.Equal("Last", match.Name);
        Assert.Single(await Resolve(screen.GetByDisplayValue("love", exact: false)));
        Assert.Empty(await Resolve(screen.GetByDisplayValue("love")));
    }

    [Fact]
    public async Task Role_options_require_states_and_level()
    {
        var screen = ScreenOf(
            Node("checkbox", "A", states: new NodeStates { Checked = true }),
            Node("checkbox", "B"),
            Node("tab", "One", states: new NodeStates { Selected = true }),
            Node("tab", "Two"),
            Node("button", "Menu", states: new NodeStates { Expanded = true }),
            Node("button", "Bold", states: new NodeStates { Pressed = true }),
            Node("button", "Off", states: new NodeStates { Disabled = true }),
            Node("heading", "Title", level: 1),
            Node("heading", "Section", level: 2));

        Assert.Equal("A", Assert.Single(await Resolve(screen.GetByRole("checkbox", new RoleOptions { Checked = true }))).Name);
        Assert.Equal("B", Assert.Single(await Resolve(screen.GetByRole("checkbox", new RoleOptions { Checked = false }))).Name);
        Assert.Equal("One", Assert.Single(await Resolve(screen.GetByRole("tab", new RoleOptions { Selected = true }))).Name);
        Assert.Equal("Menu", Assert.Single(await Resolve(screen.GetByRole("button", new RoleOptions { Expanded = true }))).Name);
        Assert.Equal("Bold", Assert.Single(await Resolve(screen.GetByRole("button", new RoleOptions { Pressed = true }))).Name);
        Assert.Equal("Off", Assert.Single(await Resolve(screen.GetByRole("button", new RoleOptions { Disabled = true }))).Name);
        Assert.Equal("Section", Assert.Single(await Resolve(screen.GetByRole("heading", new RoleOptions { Level = 2 }))).Name);
        Assert.Single(await Resolve(screen.GetByRole("heading", "Title", new RoleOptions { Level = 1 })));
        Assert.Empty(await Resolve(screen.GetByRole("heading", "Title", new RoleOptions { Level = 2 })));
        Assert.Throws<ArgumentOutOfRangeException>(() => screen.GetByRole("heading", new RoleOptions { Level = 7 }));
    }

    [Fact]
    public async Task Img_is_an_alias_of_image()
    {
        var screen = ScreenOf(Node("image", "Logo"));

        var locator = screen.GetByRole("img", "Logo");
        Assert.Single(await Resolve(locator));
        Assert.Equal("getByRole(\"image\", \"Logo\")", locator.Query.Describe());
    }

    [Fact]
    public async Task Hidden_nodes_are_dropped_only_by_role_queries_unless_visible_is_set()
    {
        var hidden = new NodeStates { Hidden = true };
        var screen = ScreenOf(
            Node("button", "Save", states: hidden),
            Node("button", "Save"),
            Node(null, text: "Saved", states: hidden),
            Node(null, text: "Saved"));

        Assert.Single(await Resolve(screen.GetByRole("button", "Save")));
        Assert.Equal(2, (await Resolve(screen.GetByText("Saved"))).Count);
        Assert.Single(await Resolve(screen.GetByText("Saved", new TextMatchOptions { Visible = true })));
    }

    [Fact]
    public async Task Label_matches_any_named_node()
    {
        var screen = ScreenOf(Node("slider", "Volume"), Node("switch", "Wi-Fi"));

        Assert.Single(await Resolve(screen.GetByLabel("Volume")));
        Assert.Single(await Resolve(screen.GetByLabel("wi-fi", exact: false)));
    }

    [Fact]
    public async Task Chained_locators_support_every_query_kind()
    {
        var screen = ScreenOf(
            Node("form", "Billing", children:
            [
                Node("textbox", "Email", value: "a@b.test", placeholder: "Email"),
                Node(null, text: "Card"),
                Node("button", "Pay", testId: "submit"),
            ]),
            Node("textbox", "Email", value: "a@b.test", placeholder: "Email"),
            Node(null, text: "Card"),
            Node(null, testId: "submit"));
        var form = screen.GetByRole("form", "Billing");

        Assert.Single(await Resolve(form.GetByLabel("Email")));
        Assert.Single(await Resolve(form.GetByText("Card")));
        Assert.Single(await Resolve(form.GetByTestId("submit")));
        Assert.Single(await Resolve(form.GetByPlaceholder("Email")));
        Assert.Single(await Resolve(form.GetByDisplayValue("a@b.test")));
        Assert.Single(await Resolve(form.GetByRole("button", "Pay")));
        Assert.Equal("getByRole(\"form\", \"Billing\").getByTestId(\"submit\")", form.GetByTestId("submit").Query.Describe());
    }

    [Fact]
    public async Task Last_picks_the_last_match()
    {
        var screen = ScreenOf(Node("button", "A"), Node("button", "B"), Node("button", "C"));

        Assert.Equal("C", Assert.Single(await Resolve(screen.GetByRole("button").Last())).Name);
        Assert.Equal("getByRole(\"button\").last()", screen.GetByRole("button").Last().Query.Describe());
        Assert.Empty(await Resolve(screen.GetByRole("link").Last()));
    }

    [Fact]
    public async Task Filter_has_keeps_matches_with_a_matching_descendant()
    {
        var screen = ScreenOf(
            Node("listitem", "Alpha", children: [Node("button", "Delete")]),
            Node("listitem", "Beta", children: [Node("button", "Edit")]),
            Node("listitem", "Gamma", children: [Node("button", "Delete")]));

        var rows = screen.GetByRole("listitem").Filter(screen.GetByRole("button", "Delete"));
        var matches = await Resolve(rows);
        Assert.Equal(["Alpha", "Gamma"], matches.Select(node => node.Name));
        Assert.Equal("Gamma", Assert.Single(await Resolve(rows.Last())).Name);
        Assert.Equal(
            "getByRole(\"listitem\").filter(has: getByRole(\"button\", \"Delete\")).last()",
            rows.Last().Query.Describe());

        // The has locator resolves inside each match, so a listitem does not match itself.
        Assert.Empty(await Resolve(screen.GetByRole("listitem").Filter(screen.GetByRole("listitem"))));
    }

    [Fact]
    public async Task Filter_and_index_apply_in_order()
    {
        var screen = ScreenOf(Node("button", "Save"), Node("button", "Cancel"), Node("button", "Save as"));

        Assert.Empty(await Resolve(screen.GetByRole("button").First().Filter("cancel")));
        Assert.Equal("Cancel", Assert.Single(await Resolve(screen.GetByRole("button").Filter(new Regex("^Can")).First())).Name);
    }

    [Fact]
    public async Task Action_waits_for_a_hidden_text_match_and_reports_it()
    {
        var screen = ScreenOf(Node(null, text: "Continue", states: new NodeStates { Hidden = true }));

        var error = await Assert.ThrowsAsync<TestException>(() => screen.GetByText("Continue").TapAsync());
        Assert.Equal("NOT_ACTIONABLE", error.Code);
        Assert.Contains("hidden", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Visibility_expectations_read_the_hidden_state()
    {
        var screen = ScreenOf(Node(null, text: "Saving", states: new NodeStates { Hidden = true }));

        await Expect.That(screen.GetByText("Saving")).ToBeHiddenAsync();
        var error = await Assert.ThrowsAsync<TestException>(() => Expect.That(screen.GetByText("Saving")).ToBeVisibleAsync());
        Assert.Equal("ASSERTION_FAILED", error.Code);
    }

    private static Task<IReadOnlyList<SemanticNode>> Resolve(Locator locator) => locator.ResolveAsync(CancellationToken.None);

    private static Screen ScreenOf(params SemanticNode[] roots)
    {
        var observation = new Observation { Route = "/", Roots = roots };
        return new Screen(
            _ => Task.FromResult(observation),
            (_, _, _) => Task.CompletedTask,
            () => CancellationToken.None,
            () => { },
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(100));
    }

    private SemanticNode Node(
        string? role,
        string? name = null,
        string? text = null,
        string? value = null,
        string? placeholder = null,
        string? testId = null,
        int? level = null,
        NodeStates? states = null,
        IReadOnlyList<SemanticNode>? children = null)
    {
        _next++;
        return new SemanticNode
        {
            Ref = "e" + _next.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Role = role,
            Name = name,
            Text = text,
            Value = value,
            Placeholder = placeholder,
            TestId = testId,
            Level = level,
            States = states ?? new NodeStates(),
            Children = children ?? [],
        };
    }
}
