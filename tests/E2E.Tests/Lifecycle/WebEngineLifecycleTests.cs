// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Lifecycle;

[Collection(BrowserCollection.Name)]
public sealed class WebEngineLifecycleTests
{
    [Fact]
    public async Task Excludes_hidden_twins_from_a_visible_query_for_every_query_kind_and_under_an_index()
    {
        using var site = await FixtureApp.StartAsync();
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/twins");
            var screen = session.Screen;
            var options = (bool visible) => new TextMatchOptions { Visible = visible };
            var twins = new (string Kind, Func<bool, Locator> Query)[]
            {
                ("text", visible => screen.GetByText("No memories yet", options(visible))),
                ("label", visible => screen.GetByLabel("Memory search", options(visible))),
                ("placeholder", visible => screen.GetByPlaceholder("Search memory...", options(visible))),
                ("displayValue", visible => screen.GetByDisplayValue("alpha", options(visible))),
                ("testId", visible => screen.GetByTestId("memory-empty", options(visible))),
            };
            foreach (var (kind, query) in twins)
            {
                var all = query(false);
                var hidden = new List<bool>();
                for (var index = 0; index < await all.CountAsync(); index++)
                {
                    hidden.Add(await all.Nth(index).IsHiddenAsync());
                }

                Assert.True(hidden.SequenceEqual([true, false]), kind + " without visible");
                var shown = query(true);
                Assert.Equal(1, await shown.CountAsync());
                Assert.False(await shown.IsHiddenAsync(), kind + " with visible");
                Assert.True((await shown.BoundingBoxAsync())?.Width > 0, kind + " with visible");
            }

            // A role query already skips display:none; visible leaves it alone: the twin's shown
            // button, the labeled button, and the insert button below, never the hidden twin.
            Assert.Equal(3, await screen.GetByRole("button", new RoleOptions { Visible = true }).CountAsync());

            // A required-field marker is aria-hidden: the label names the field without it.
            var required = screen.GetByLabel("Display name");
            Assert.Equal(1, await required.CountAsync());
            await Expect.That(required).ToHaveAccessibleNameAsync("Display name");
            // Hidden text between visible fragments is skipped too, and CSS-hidden text stays out.
            await Expect.That(screen.GetByLabel("Team name")).ToHaveAccessibleNameAsync("Team name");
            await Expect.That(screen.GetByLabel("Mixed")).ToHaveAccessibleNameAsync("Mixed");
            // Not ported: "Visible label", "Second label", and "First label" match upstream's getByLabel
            // through any one associated label; the port's getByLabel matches the accessible name, which
            // joins both labels (COMPATIBILITY.md).
            // Every labelable element carries its labels: a button and a meter too, not only text fields.
            Assert.Equal("button", (await screen.GetByLabel("Run the check").ResolveAsync(CancellationToken.None)).Single().Role);
            Assert.Equal(1, await screen.GetByLabel("Score").CountAsync());
            // Not ported: text "Display name*" matches the label element itself. The tree does not list
            // text inside a control's label; the control carries it as its name (COMPATIBILITY.md).

            // A locator here resolves again on each action, so the fill lands on the field the label names
            // after a field is inserted ahead of it.
            var displayName = screen.GetByLabel("Display name");
            await screen.GetByRole("button", "Insert a field").TapAsync();
            await displayName.FillAsync("pinned");
            Assert.Equal("pinned", await screen.GetByLabel("Display name").InputValueAsync());
            Assert.Equal("", await screen.GetByLabel("Inserted").InputValueAsync() ?? "");

            // A visibility: hidden twin is hidden.
            Assert.Equal(2, await screen.GetByText("Decorative twin").CountAsync());
            Assert.False(await screen.GetByText("Decorative twin", options(true)).IsHiddenAsync());
            // Not ported: the painted aria-hidden spinner, which upstream's visible query keeps. The tree's
            // one hidden state keeps aria-hidden in it, for role queries and the agent (COMPATIBILITY.md).

            // Under an index the predicate runs before nth: first() is the first shown node, not the first node.
            Assert.True(await screen.GetByText("No memories yet").First().IsHiddenAsync());
            var firstShown = screen.GetByText("No memories yet", options(true)).First();
            Assert.Equal(1, await firstShown.CountAsync());
            Assert.False(await firstShown.IsHiddenAsync());

            // The surviving match acts on the shown element.
            await screen.GetByPlaceholder("Search memory...", options(true)).FillAsync("launch");
            Assert.False(await screen.GetByDisplayValue("launch").IsHiddenAsync());
            Assert.Equal(1, await screen.GetByDisplayValue("launch").CountAsync());
        });
    }

    [Fact]
    public async Task Applies_visible_before_an_index_filter_or_scope_so_a_hidden_twin_is_never_selected()
    {
        using var site = await FixtureApp.StartAsync();
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/twins");
            var screen = session.Screen;
            Locator Text(string value, bool visible) => screen.GetByText(value, new TextMatchOptions { Visible = visible });

            // The hidden paragraph comes first in document order.
            Assert.True(await Text("Decorative twin", false).First().IsHiddenAsync());
            Assert.False(await Text("Decorative twin", true).First().IsHiddenAsync());
            Assert.False(await Text("Decorative twin", true).Nth(0).IsHiddenAsync());
            Assert.Equal(0, await Text("Decorative twin", true).Nth(1).CountAsync());
            Assert.False(await Text("Decorative twin", true).Last().IsHiddenAsync());

            // The reader's own state narrows, so a twin only Playwright calls visible is never first.
            Assert.Equal("contents-shown", await Text("Contents twin", true).First().GetAttributeAsync("id"));
            Assert.True(await Text("Contents twin", false).First().IsHiddenAsync());

            // A filter over a visible query never retains the hidden twin.
            Assert.Equal(2, await Text("Decorative twin", false).Filter("Decorative").CountAsync());
            Assert.False(await Text("Decorative twin", true).Filter("Decorative").IsHiddenAsync());

            // Not ported: the has-filters on `body` and `#live`; the port has no CSS selector locator.

            // As a scope, a visible test-id query drops the hidden panel before the child query runs.
            var inAnyPanel = screen.GetByTestId("memory-panel").GetByText("Open");
            Assert.Equal(2, await inAnyPanel.CountAsync());
            Assert.False(await inAnyPanel.Nth(0).IsHiddenAsync());
            Assert.False(await inAnyPanel.Nth(1).IsHiddenAsync());
            var scoped = screen.GetByTestId("memory-panel", new TextMatchOptions { Visible = true }).GetByText("Open");
            Assert.Equal(1, await scoped.CountAsync());
            // Both children are visible, one overriding its panel's visibility; the exclusion came from the scope.
            var panels = screen.GetByTestId("memory-panel", new TextMatchOptions { Visible = true });
            Assert.Equal(1, await panels.CountAsync());
            Assert.False(await panels.IsHiddenAsync());
            await scoped.TapAsync();
        });
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/124: hasText keeps inputs, and a display-value scope is not refused")]
    public async Task Selects_positionally_among_display_value_matches_and_keeps_composition_honest()
    {
        using var site = await FixtureApp.StartAsync();
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/values");
            var screen = session.Screen;
            var shared = screen.GetByDisplayValue("shared");
            async Task<List<string?>> Names(Locator locator) => (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.Name).ToList();

            // Positions are relative to the value-filtered matches, not to every
            // form control on the page: "Other" sits between none of them.
            Assert.Equal(["First", "Second", "Third"], await Names(shared));
            Assert.Equal(["First"], await Names(shared.First()));
            Assert.Equal(["Second"], await Names(shared.Nth(1)));
            Assert.Equal(["Third"], await Names(shared.Last()));
            Assert.Empty(await Names(shared.Nth(3)));

            // Positions chain innermost first: first() of last() is still the last match.
            Assert.Equal(["Third"], await Names(shared.Last().First()));

            // filter({ hasText }) runs on each match. Inputs have no text content; the textarea's is its
            // initial content, exactly as for any other query.
            Assert.Equal(["Third"], await Names(shared.Filter("shared")));
            Assert.Empty(await Names(shared.Filter("nowhere")));

            // A filter after a position is checked on the selected element alone: the last match is the
            // textarea, whose text content is "shared"; the first is an input with no text content at all.
            Assert.Equal(["Third"], await Names(shared.Last().Filter("shared")));
            Assert.Empty(await Names(shared.First().Filter("shared")));
            // ... and `has` on a positional match too; a form control has no descendant nodes, so nothing survives.
            Assert.Empty(await Names(shared.Last().Filter(screen.GetByRole("textbox"))));

            // The match a position hands back acts on that element alone.
            await shared.Last().FillAsync("edited");
            Assert.Equal(["First", "Second"], await Names(shared));
            Assert.Equal(["Third"], await Names(screen.GetByDisplayValue("edited")));

            // What still needs the value predicate inside the chain stays unsupported, and says which
            // compositions those are.
            var child = await Assert.ThrowsAnyAsync<E2EException>(() => Names(shared.GetByRole("textbox")));
            Assert.Equal("UNSUPPORTED_CAPABILITY", child.Code);
            Assert.Equal("displayValue queries cannot scope child queries or serve as a has-filter in this engine", child.Message);
            var has = await Assert.ThrowsAnyAsync<E2EException>(() => Names(screen.GetByRole("main").Filter(shared)));
            Assert.Equal("UNSUPPORTED_CAPABILITY", has.Code);
        });
    }

    [Fact]
    public async Task Takes_a_display_value_position_among_shown_matches_when_the_query_is_visible()
    {
        using var site = await FixtureApp.StartAsync();
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/twins");
            var screen = session.Screen;
            Locator Alpha(bool visible) => screen.GetByDisplayValue("alpha", new TextMatchOptions { Visible = visible });
            async Task<List<bool>> Hidden(Locator locator) => (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.States.Hidden).ToList();

            // The display:none control comes first in document order, so without
            // visible the first position is the hidden twin.
            Assert.Equal([true], await Hidden(Alpha(false).First()));
            Assert.Equal([false], await Hidden(Alpha(false).Nth(1)));

            // With visible, hidden candidates leave before the value predicate and
            // its positional steps run, so every position is among shown controls.
            Assert.Equal([false], await Hidden(Alpha(true).First()));
            Assert.Equal([false], await Hidden(Alpha(true).Nth(0)));
            Assert.Equal([false], await Hidden(Alpha(true).Last()));
            Assert.Empty(await Hidden(Alpha(true).Nth(1)));

            // A filter after the position runs on the shown element alone.
            Assert.Empty(await Hidden(Alpha(true).First().Filter("nothing here")));

            // The positional match acts on the shown control, and the hidden twin keeps its value.
            await Alpha(true).First().FillAsync("launch");
            Assert.Equal([true], await Hidden(Alpha(false)));
            Assert.Equal([false], await Hidden(screen.GetByDisplayValue("launch")));
        });
    }

    [Fact]
    public async Task Keeps_a_node_id_across_observations_while_its_element_lives_and_reports_it_stale_once_it_is_gone()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await WebSemanticsTests.OpenAsync(site.Url + "form", new WebEngineOptions { Headless = true });
        var first = await session.ObserveAsync(CancellationToken.None);
        var textbox = WebSemanticsTests.Flatten(first.Roots).First(node => node.Role == "textbox");
        await session.PerformAsync(textbox, new LocatorAction.Fill("fresh"), CancellationToken.None);

        // The id is stamped on the element: a second look names the same
        // textbox by the same id, and the earlier ref still acts on it.
        var second = await session.ObserveAsync(CancellationToken.None);
        var again = WebSemanticsTests.Flatten(second.Roots).First(node => node.Role == "textbox");
        Assert.Equal(textbox.Ref, again.Ref);
        await session.PerformAsync(textbox, new LocatorAction.Fill("late"), CancellationToken.None);

        // A new document has none of the old elements: the id is gone with it.
        await session.OpenAsync(site.Url, CancellationToken.None);
        var third = await session.ObserveAsync(CancellationToken.None);
        Assert.DoesNotContain(WebSemanticsTests.Flatten(third.Roots), node => node.Ref == textbox.Ref);
        var stale = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(textbox, new LocatorAction.Fill("gone"), CancellationToken.None));
        Assert.Equal("NODE_STALE", stale.Code);
        Assert.True(stale.Retryable);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/121: a ref acts on its element when a second one matches")]
    public async Task Reports_a_located_ref_stale_once_a_second_element_matches_it_and_acts_on_neither()
    {
        // Upstream inserts the second button from the test; the port's engine has no page script call, so
        // the page inserts it a moment after load, once the test has located the first.
        using var site = await TinySite.StartAsync("""
            <!DOCTYPE html>
            <html><body>
            <script>window.taps = 0; const count = () => { taps++; document.getElementById("taps").textContent = String(taps); };</script>
            <button onclick="count()">Go</button>
            <output id="taps">0</output>
            <script>setTimeout(() => document.body.insertAdjacentHTML("beforeend", '<button onclick="count()">Go</button>'), 500);</script>
            </body></html>
            """);
        await using var session = await WebSemanticsTests.OpenAsync(site.Url, new WebEngineOptions { Headless = true });
        var go = WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).Single(node => node.Role == "button");
        while (WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).Count(node => node.Role == "button") < 2)
        {
            await Task.Delay(50);
        }

        var stale = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(go, new LocatorAction.Tap(), CancellationToken.None));
        Assert.Equal("NODE_STALE", stale.Code);
        Assert.True(stale.Retryable);
        var status = WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).Single(node => node.Role == "status");
        Assert.Equal("0", status.Name);
    }

    [Fact]
    public async Task Walks_through_a_display_contents_element_to_the_fields_it_lays_out()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await WebSemanticsTests.OpenAsync(site.Url + "contents", new WebEngineOptions { Headless = true });
        var fields = WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).Where(node => node.Role == "textbox").Select(node => node.Name);
        Assert.Equal(["Email", "First name"], fields);
    }

    [Fact]
    public async Task Reports_an_unopened_page_as_INVALID_STATE_never_as_a_missing_node()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await new WebEngine(new WebEngineOptions { Headless = true }).StartAsync(
            new EngineStartOptions { BaseUrl = site.Url, ActionTimeout = TimeSpan.FromSeconds(10) },
            CancellationToken.None);
        var error = await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(CancellationToken.None));
        Assert.Equal("INVALID_STATE", error.Code);
    }

    [Fact]
    public async Task Cancels_an_in_flight_operation_when_its_signal_aborts_instead_of_waiting_out_Playwright()
    {
        using var site = await FixtureApp.StartAsync();
        await using var session = await new WebEngine(new WebEngineOptions { Headless = true }).StartAsync(
            new EngineStartOptions { BaseUrl = site.Url, ActionTimeout = TimeSpan.FromSeconds(10) },
            CancellationToken.None);
        using var controller = new CancellationTokenSource();
        var started = System.Diagnostics.Stopwatch.StartNew();
        var pending = session.OpenAsync(site.Url + "slow", controller.Token);
        controller.CancelAfter(TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAnyAsync<E2EException>(() => pending);
        Assert.Equal("CANCELLED", error.Code);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3));
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/120: the test-id attribute stays in a node's attributes")]
    public async Task Reports_the_configured_test_id_attribute_as_testId_on_observed_and_located_nodes_alike()
    {
        using var site = await TinySite.StartAsync("""
            <!DOCTYPE html>
            <html><body><button data-qa="go">Go</button><button data-testid="stop">Stop</button></body></html>
            """);
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(new WebEngineOptions { Headless = true, TestIdAttribute = "data-qa" }),
            BaseUrl = site.Url,
            TestTitle = "web engine lifecycle > test id",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        Exception? failure = null;
        try
        {
            await session.App.OpenAsync("/");
            var observed = WebSemanticsTests.Flatten((await session.Screen.ObserveAsync(CancellationToken.None)).Roots).ToList();
            var go = observed.Single(node => node.Name == "Go");
            var stop = observed.Single(node => node.Name == "Stop");
            Assert.Equal("go", go.TestId);
            Assert.Null(stop.TestId);
            // The tree carries the id as a field, not as an attribute.
            Assert.False(go.Attributes.ContainsKey("data-qa"));

            Assert.Equal(["go"], (await session.Screen.GetByTestId("go").ResolveAsync(CancellationToken.None)).Select(node => node.TestId));
            Assert.Empty(await session.Screen.GetByTestId("stop").ResolveAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            session.Complete(failure);
        }
    }
}
