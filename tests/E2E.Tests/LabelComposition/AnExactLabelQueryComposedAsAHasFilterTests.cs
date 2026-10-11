// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.LabelComposition;

/// <summary>
/// An exact label query keeps its exactness when it composes: as a <c>has</c> filter it picks the row whose
/// control that label names and no row whose label merely contains the text, and it still matches what the
/// standalone query matches. The fixture is <c>/rows</c>: a list of rows, each one labelled control beside a
/// Remove button. The second row's label carries an aria-hidden marker, a third row has a label with the
/// characters a selector body has to carry unharmed, and a fourth row's <c>Name</c> input is hidden, for the
/// <c>visible</c> flag on a composed query.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class AnExactLabelQueryComposedAsAHasFilterTests
{
    private const string Rows = """
        <!DOCTYPE html>
        <html>
        <head><title>Fixture Rows</title></head>
        <body>
        <h1>Rows</h1>
        <ul>
          <li data-testid="row-last-name"><label>Last Name <input name="last"></label><button type="button" onclick="this.parentElement.remove()">Remove</button></li>
          <li data-testid="row-name"><label for="name">Name<span aria-hidden="true">*</span></label><input id="name"><button type="button" onclick="this.parentElement.remove()">Remove</button></li>
          <li data-testid="row-quoted"><label>Say "hi" >> now <input name="quoted"></label><button type="button" onclick="this.parentElement.remove()">Remove</button></li>
          <li data-testid="row-name-hidden"><label for="name-hidden">Name</label><input id="name-hidden" hidden><button type="button" onclick="this.parentElement.remove()">Remove</button></li>
        </ul>
        </body>
        </html>
        """;

    [Fact]
    public async Task Keeps_the_exact_predicate_one_row_for_the_exact_label_two_for_the_substring_as_standalone()
    {
        await WithRowsAsync(async screen =>
        {
            Assert.Equal(["Name", "Name"], await NamesAsync(screen.GetByLabel("Name")));
            Assert.Equal(["row-name", "row-name-hidden"], await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Name"))));
            Assert.Equal(["row-last-name", "row-name", "row-name-hidden"], await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Name", exact: false))));
            Assert.Equal(["row-last-name"], await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Last Name"))));
            Assert.Empty(await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Nowhere"))));
        });
    }

    [Fact]
    public async Task Reads_the_label_as_the_standalone_query_does_an_aria_hidden_marker_never_hides_the_row()
    {
        await WithRowsAsync(async screen =>
        {
            Assert.Empty(await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Name*"))));
            Assert.Equal(["row-quoted"], await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Say \"hi\" >> now"))));
        });
    }

    [Fact]
    public async Task Keeps_the_visible_flag_when_composed_a_row_whose_matching_input_is_hidden_drops_out()
    {
        await WithRowsAsync(async screen =>
        {
            var visible = new TextMatchOptions { Visible = true };
            var exactVisible = screen.GetByLabel("Name", visible);
            Assert.Equal(1, await exactVisible.CountAsync());
            Assert.False(await exactVisible.IsHiddenAsync());
            Assert.Equal(["row-name"], await TestIdsAsync(RowsWith(screen, exactVisible)));
            Assert.Equal(["row-last-name", "row-name"], await TestIdsAsync(RowsWith(screen, screen.GetByLabel("Name", new TextMatchOptions { Exact = false, Visible = true }))));
        });
    }

    [Fact]
    public async Task Composes_onto_a_position_and_a_child_query_and_acts_on_the_row_it_picked()
    {
        await WithRowsAsync(async screen =>
        {
            var remove = RowsWith(screen, screen.GetByLabel("Name")).First().GetByRole("button", "Remove");
            Assert.Equal(1, await remove.CountAsync());
            await remove.TapAsync();
            Assert.Equal(["row-last-name", "row-quoted", "row-name-hidden"], await TestIdsAsync(screen.GetByRole("listitem")));
            Assert.Equal(1, await screen.GetByLabel("Last Name").CountAsync());
        });
    }

    private static Locator RowsWith(Screen screen, Locator has) => screen.GetByRole("listitem").Filter(has);

    private static async Task WithRowsAsync(Func<Screen, Task> body)
    {
        using var site = await TinySite.StartAsync(Rows);
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await body(session.Screen);
        });
    }

    private static async Task<List<string?>> NamesAsync(Locator locator) =>
        (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.Name).ToList();

    private static async Task<List<string?>> TestIdsAsync(Locator locator) =>
        (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.TestId).ToList();
}
