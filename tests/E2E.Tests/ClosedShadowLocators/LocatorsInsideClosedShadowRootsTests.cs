// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.ClosedShadowLocators;

/// <summary>
/// Deterministic locators inside closed shadow roots: every query kind resolves the nodes the reader already
/// observes there, actions and reads work on them, and the rules that hold outside (strictness by count, the
/// <c>visible</c> flag, positions, scopes, filters) hold across the boundary, with each node found once. The
/// fixture is <c>/closed-form</c>; see <see cref="FixtureApp"/> for the layout.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class LocatorsInsideClosedShadowRootsTests
{
    [Fact]
    public async Task Resolves_every_query_kind_to_the_node_the_observed_tree_names()
    {
        using var site = await FixtureApp.StartAsync();
        await using (var engine = await WebSemanticsTests.OpenAsync(site.Url + "closed-form", new WebEngineOptions { Headless = true }))
        {
            var observed = WebSemanticsTests.Flatten((await engine.ObserveAsync(CancellationToken.None)).Roots);
            Assert.Equal("submit", observed.First(node => node.Role == "button" && node.Name == "Submit").TestId);
        }

        await WithScreenAsync(site, async screen =>
        {
            Assert.Equal(["submit"], await TestIdsAsync(screen.GetByRole("button", "Submit")));
            Assert.Equal(["code"], await TestIdsAsync(screen.GetByPlaceholder("Access code")));

            Assert.Equal(["ada"], await ValuesAsync(screen.GetByLabel("Nickname")));
            Assert.Equal(["ada"], await ValuesAsync(screen.GetByLabel("nick", exact: false)));

            Assert.Equal(["hint"], await TestIdsAsync(screen.GetByText("Access code hint: SHADOW-42", new TextMatchOptions { Visible = true })));
            Assert.Equal(["hint"], await TestIdsAsync(screen.GetByText("code hint", new TextMatchOptions { Exact = false, Visible = true })));

            var valued = await screen.GetByDisplayValue("ada").ResolveAsync(CancellationToken.None);
            Assert.Equal(["nickname"], valued.Select(node => node.Attributes["name"]));

            var byTestId = await screen.GetByTestId("code").ResolveAsync(CancellationToken.None);
            Assert.Equal(["Access code"], byTestId.Select(node => node.Attributes["placeholder"]));
        });
    }

    [Fact]
    public async Task Reaches_roots_nested_either_way_round_open_inside_closed_closed_inside_open()
    {
        await WithScreenAsync(async screen =>
        {
            var buttons = await screen.GetByRole("button").ResolveAsync(CancellationToken.None);
            Assert.Equal(
                ["Twin", "Submit", "Twin", "Twin", "Open inside closed", "Closed inside open", "Sidecar"],
                buttons.Select(node => node.Name));
        });
    }

    [Fact]
    public async Task Finds_a_node_once_whether_Playwright_or_the_closed_root_path_reaches_it_so_counts_stay_strict()
    {
        await WithScreenAsync(async screen =>
        {
            Assert.Equal(["twin-light", "twin-a", "twin-b"], await TestIdsAsync(screen.GetByRole("button", "Twin")));
            // A position counts the matches in tree order, the light-DOM button first here.
            Assert.Equal(["twin-a"], await TestIdsAsync(screen.GetByRole("button", "Twin").Nth(1)));
            Assert.Equal(["twin-b"], await TestIdsAsync(screen.GetByRole("button", "Twin").Last()));
        });
    }

    [Fact]
    public async Task Applies_the_visible_flag_and_the_hidden_exclusion_of_a_role_query_inside_the_root()
    {
        await WithScreenAsync(async screen =>
        {
            const string Hint = "Access code hint: SHADOW-42";
            Assert.Equal(["hint", "hint-ghost"], (await TestIdsAsync(screen.GetByText(Hint))).Order(StringComparer.Ordinal));
            Assert.Equal(["hint"], await TestIdsAsync(screen.GetByText(Hint, new TextMatchOptions { Visible = true })));
            // A role query never matches a hidden node, as outside a closed root.
            Assert.Equal(["submit"], await TestIdsAsync(screen.GetByRole("button", "Submit")));
        });
    }

    [Fact]
    public async Task Scopes_a_child_query_to_a_root_inside_the_closed_root_and_filters_a_region_by_a_button_inside_it()
    {
        await WithScreenAsync(async screen =>
        {
            var nested = await screen.GetByRole("region", "Advanced").GetByRole("button").ResolveAsync(CancellationToken.None);
            Assert.Equal(["Open inside closed", "Closed inside open"], nested.Select(node => node.Name));

            // Not ported: `has` on the `x-access` host. The port has no CSS selector locator, and the host is
            // no node of the tree, so there is nothing to filter. The region that holds the root stands in for it.
            var advanced = screen.GetByRole("region");
            Assert.Equal(1, await advanced.Filter(screen.GetByRole("button", "Open inside closed")).CountAsync());
            Assert.Equal(0, await advanced.Filter(screen.GetByRole("button", "Sidecar")).CountAsync());
        });
    }

    [Fact]
    public async Task Filters_a_node_inside_a_closed_root_by_its_text()
    {
        await WithScreenAsync(async screen =>
        {
            // Not ported: the `x-access` host half. The port has no CSS selector locator, and the host is
            // no node of the tree. Inside the root the text is ordinary text again.
            Assert.Equal(1, await screen.GetByTestId("hint").Filter("SHADOW-42").CountAsync());
        });
    }

    [Fact]
    public async Task Matches_a_label_an_aria_labelledby_id_names_in_the_controls_own_tree_in_a_closed_root_and_an_open_one_below_it()
    {
        using var site = await FixtureApp.StartAsync();
        await using (var engine = await WebSemanticsTests.OpenAsync(site.Url + "closed-form", new WebEngineOptions { Headless = true }))
        {
            var observed = WebSemanticsTests.Flatten((await engine.ObserveAsync(CancellationToken.None)).Roots).ToList();
            var pin = observed.First(node => node.TestId == "pin");
            Assert.Equal(("textbox", "PIN"), (pin.Role, pin.Name));
            var tone = observed.First(node => node.TestId == "tone");
            Assert.Equal(("textbox", "Tone"), (tone.Role, tone.Name));
        }

        await WithScreenAsync(site, async screen =>
        {
            Assert.Equal(["pin"], await TestIdsAsync(screen.GetByLabel("PIN")));
            Assert.Equal(["pin"], await TestIdsAsync(screen.GetByLabel("pin", exact: false)));
            Assert.Equal(["tone"], await TestIdsAsync(screen.GetByLabel("Tone")));
            Assert.Equal(["tone"], await TestIdsAsync(screen.GetByLabel("tone", exact: false)));
        });
    }

    [Fact]
    public async Task Reports_focus_on_the_node_that_holds_it_inside_a_closed_root_and_inside_an_open_root_below_it()
    {
        await WithScreenAsync(async screen =>
        {
            async Task<bool> FocusedAsync(string testId) => (await screen.GetByTestId(testId).ResolveAsync(CancellationToken.None))[0].States.Focused;
            async Task<List<string?>> FocusedTestIdsAsync() =>
                (await screen.GetByTestId(new Regex(".*")).ResolveAsync(CancellationToken.None)).Where(node => node.States.Focused).Select(node => node.TestId).ToList();

            await screen.GetByTestId("pin").TapAsync();
            Assert.True(await FocusedAsync("pin"));
            Assert.False(await FocusedAsync("code"));

            await screen.GetByTestId("tone").TapAsync();
            Assert.True(await FocusedAsync("tone"));
            Assert.False(await FocusedAsync("pin"));
            Assert.Equal(["tone"], await FocusedTestIdsAsync());
        });
    }

    [Fact]
    public async Task Fills_presses_taps_and_reads_the_nodes_it_located()
    {
        await WithScreenAsync(async screen =>
        {
            var code = screen.GetByPlaceholder("Access code");
            await code.FillAsync("WRONG-1");
            Assert.Equal("WRONG-1", (await screen.GetByTestId("code").ResolveAsync(CancellationToken.None))[0].Value);
            await code.PressAsync("Enter");
            Assert.Equal(1, await screen.GetByText("denied").CountAsync());

            await code.FillAsync("SHADOW-42");
            await screen.GetByRole("button", "Submit").TapAsync();
            Assert.Equal("granted", (await screen.GetByText("granted").ResolveAsync(CancellationToken.None)).Single().Text);
            Assert.Equal(["code"], await TestIdsAsync(screen.GetByDisplayValue("SHADOW-42")));
        });
    }

    private static async Task WithScreenAsync(Func<Screen, Task> body)
    {
        using var site = await FixtureApp.StartAsync();
        await WithScreenAsync(site, body);
    }

    private static async Task WithScreenAsync(TinySite site, Func<Screen, Task> body)
    {
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/closed-form");
            await body(session.Screen);
        });
    }

    private static async Task<List<string?>> TestIdsAsync(Locator locator) =>
        (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.TestId).ToList();

    private static async Task<List<string?>> ValuesAsync(Locator locator) =>
        (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.Value).ToList();
}
