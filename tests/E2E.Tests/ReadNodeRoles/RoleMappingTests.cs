// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ReadNodeRoles;

[Collection(BrowserCollection.Name)]
public sealed class RoleMappingTests
{
    private const string Pixel = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAAAAAAALAAAAAABAAEAAAIBRAA7";

    // An empty block lays out to no height and is hidden; the role fixtures below are about roles, so their empty elements get a box.
    private const string EmptyBoxes = "[data-testid]:empty { min-width: 1px; min-height: 1px }";

    [Theory]
    [InlineData("open")]
    [InlineData("closed")]
    public async Task Scopes_nested_shadow_landmarks_to_their_outer_article(string mode)
    {
        using var site = await TinySite.StartAsync(Page($$"""
            <article><div id="card"></div></article>
            <div id="page"></div>
            <script>
              const card = document.querySelector("#card").attachShadow({ mode: "{{mode}}" });
              card.innerHTML = '<div id="nested"></div>';
              card.querySelector("#nested").attachShadow({ mode: "{{mode}}" }).innerHTML =
                '<header aria-label="Card" data-testid="card-header">Card</header><footer aria-label="Card footer" data-testid="card-footer">Foot</footer>';
              document.querySelector("#page").attachShadow({ mode: "{{mode}}" }).innerHTML =
                '<header aria-label="Page" data-testid="page-header">Page</header><footer aria-label="Page footer" data-testid="page-footer">Legal</footer>';
            </script>
            """));
        var nodes = ByTestId(await CaptureAsync(site.Url));
        Assert.Contains("card-header", nodes.Keys);
        Assert.Contains("card-footer", nodes.Keys);
        Assert.Null(nodes["card-header"].Role);
        Assert.Null(nodes["card-footer"].Role);
        Assert.Equal("banner", nodes["page-header"].Role);
        Assert.Equal("contentinfo", nodes["page-footer"].Role);

        if (mode == "open")
        {
            var session = await WebSemanticsTests.StartSessionAsync(site.Url);
            await WebSemanticsTests.RunAsync(session, async () =>
            {
                await session.App.OpenAsync("/");
                Assert.Equal(["page-header"], await TestIdsAsync(session.Screen.GetByRole("banner")));
                Assert.Equal(["page-footer"], await TestIdsAsync(session.Screen.GetByRole("contentinfo")));
            });
        }
    }

    [Fact]
    public async Task Reports_ARIA_img_as_image_alongside_the_img_element()
    {
        using var site = await TinySite.StartAsync(Page($"""
            <style>{EmptyBoxes}</style>
            <img src="{Pixel}" alt="Logo" data-testid="picture">
            <div role="img" aria-label="Chart" data-testid="drawn"></div>
            <svg role="img" aria-label="Icon" width="10" height="10" data-testid="vector"></svg>
            <img src="{Pixel}" alt="">
            """));
        var tree = await CaptureAsync(site.Url);
        var nodes = ByTestId(tree);
        Assert.Equal(("image", "Logo"), (nodes["picture"].Role, nodes["picture"].Name));
        Assert.Equal(("image", "Chart"), (nodes["drawn"].Role, nodes["drawn"].Name));
        Assert.Equal(("image", "Icon"), (nodes["vector"].Role, nodes["vector"].Name));

        // An empty alt marks decoration: the element is not an image and, with nothing else to say, not a node.
        Assert.Equal(["Logo", "Chart", "Icon"], tree.Where(node => node.Role == "image").Select(node => node.Name));
        Assert.DoesNotContain(tree, node => node.Role == "presentation");
    }

    [Fact]
    public async Task Infers_row_headers_from_data_cell_neighbors_and_respects_explicit_scopes()
    {
        using var site = await TinySite.StartAsync(Page("""
            <table><tr><th data-testid="row">Item</th><td>Value</td></tr></table>
            <table><tr><td>Value</td><th data-testid="last-row">Item</th></tr></table>
            <table><tr><th data-testid="column">Item</th><th>Value</th></tr></table>
            <table><tr><th scope="col" data-testid="scoped-column">Item</th><td>Value</td></tr></table>
            <table><tr><th scope="row" data-testid="scoped-row">Item</th><th>Value</th></tr></table>
            <table><tr><th data-testid="empty-data">Item</th><td></td></tr></table>
            <table><tr><th data-testid="child-data">Item</th><td><input aria-label="Value"></td></tr></table>
            <table><tr><th data-testid="single">Item</th></tr></table>
            <table><tr><th data-testid="single-column">Item</th></tr><tr><td>Value</td></tr></table>
            """));
        var nodes = ByTestId(await CaptureAsync(site.Url));
        AssertRoles(nodes, new()
        {
            ["row"] = "rowheader",
            ["last-row"] = "rowheader",
            ["column"] = "columnheader",
            ["scoped-column"] = "columnheader",
            ["scoped-row"] = "rowheader",
            ["empty-data"] = "columnheader",
            ["child-data"] = "rowheader",
            ["single"] = null,
            ["single-column"] = "columnheader",
        });

        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            Assert.Equal(["row", "last-row", "scoped-row", "child-data"], await TestIdsAsync(session.Screen.GetByRole("rowheader")));
        });
    }

    [Fact]
    public async Task Reports_an_inline_svg_as_an_image_named_by_its_title_child_as_Playwright_does()
    {
        using var site = await TinySite.StartAsync(Page("""
            <input aria-label="Message">
            <div class="send-btn"><svg viewBox="0 0 24 24" width="24" height="24" data-testid="bare"><path d="M2 3 L22 12 L2 21 Z"/></svg></div>
            <svg width="10" height="10" data-testid="titled"><title>Close</title></svg>
            <svg width="10" height="10" aria-label="Labelled" data-testid="labelled"><title>Ignored</title></svg>
            <svg width="10" height="10" aria-hidden="true" data-testid="decorative"></svg>
            <svg width="10" height="10" role="presentation"><title>Decor</title></svg>
            <a href="/home" data-testid="home"><svg width="10" height="10" role="none"><title>Home</title></svg></a>
            """));
        var tree = await CaptureAsync(site.Url);
        var nodes = ByTestId(tree);
        Assert.Equal("image", nodes["bare"].Role);
        Assert.Null(nodes["bare"].Name);
        Assert.Equal(("image", "Close"), (nodes["titled"].Role, nodes["titled"].Name));
        Assert.Equal(("image", "Labelled"), (nodes["labelled"].Role, nodes["labelled"].Name));
        // The port lists an aria-hidden node as hidden, where upstream leaves it out of the tree.
        Assert.True(nodes["decorative"].States.Hidden);
        Assert.Equal(("link", "Home"), (nodes["home"].Role, nodes["home"].Name));
        Assert.DoesNotContain(tree, node => node.Role is "presentation" or "none");

        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            Assert.Equal(["bare", "titled", "labelled"], await TestIdsAsync(session.Screen.GetByRole("img")));
            Assert.Equal("titled", await session.Screen.GetByRole("img", "Close", exact: true).GetAttributeAsync("data-testid"));
        });
    }

    [Fact]
    public async Task Passes_the_composite_widget_roles_through_from_role_attributes_and_names_their_items_from_content()
    {
        using var site = await TinySite.StartAsync(Page($"""
            <style>{EmptyBoxes}</style>
            <div role="tablist" aria-label="Filter" data-testid="tablist">
              <button role="tab" aria-selected="true" data-testid="tab">All</button>
            </div>
            <div role="tabpanel" aria-label="All items" data-testid="tabpanel"></div>
            <div role="toolbar" aria-label="Formatting" data-testid="toolbar">
              <button aria-pressed="true">Bold</button>
            </div>
            <div role="menubar" data-testid="menubar">
              <div role="menuitem" data-testid="menuitem">File</div>
            </div>
            <div role="menu" aria-label="View" data-testid="menu">
              <div role="menuitemcheckbox" aria-checked="true" data-testid="check">Show grid</div>
              <div role="menuitemradio" aria-checked="false" data-testid="radio">Compact</div>
            </div>
            <div role="tree" aria-label="Files" data-testid="tree">
              <div role="treeitem" aria-expanded="false" data-testid="treeitem">src</div>
            </div>
            <div role="grid" aria-label="Sheet" data-testid="grid">
              <div role="rowgroup" data-testid="rowgroup">
                <div role="row" data-testid="row">
                  <div role="rowheader" data-testid="rowheader">1</div>
                  <div role="gridcell" data-testid="gridcell">A1</div>
                </div>
              </div>
            </div>
            <div role="radiogroup" aria-label="Plan" data-testid="radiogroup"></div>
            <div role="tooltip" data-testid="tooltip">Saves the draft</div>
            <div role="separator" data-testid="separator"></div>
            <div role="progressbar" aria-label="Upload" aria-valuenow="40" data-testid="progressbar"></div>
            <div role="spinbutton" aria-label="Quantity" aria-valuenow="2" data-testid="spinbutton"></div>
            <div role="meter" aria-label="Disk" aria-valuenow="50" data-testid="meter"></div>
            """));
        var nodes = ByTestId(await CaptureAsync(site.Url));
        AssertRoles(nodes, new()
        {
            ["tablist"] = "tablist",
            ["tab"] = "tab",
            ["tabpanel"] = "tabpanel",
            ["toolbar"] = "toolbar",
            ["menubar"] = "menubar",
            ["menuitem"] = "menuitem",
            ["menu"] = "menu",
            ["check"] = "menuitemcheckbox",
            ["radio"] = "menuitemradio",
            ["tree"] = "tree",
            ["treeitem"] = "treeitem",
            ["grid"] = "grid",
            ["rowgroup"] = "rowgroup",
            ["row"] = "row",
            ["rowheader"] = "rowheader",
            ["gridcell"] = "gridcell",
            ["radiogroup"] = "radiogroup",
            ["tooltip"] = "tooltip",
            ["separator"] = "separator",
            ["progressbar"] = "progressbar",
            ["spinbutton"] = "spinbutton",
            ["meter"] = "meter",
        });
        Assert.Equal(("Show grid", true), (nodes["check"].Name, nodes["check"].States.Checked));
        Assert.Equal(("Compact", false), (nodes["radio"].Name, nodes["radio"].States.Checked));
        Assert.Equal(("src", false), (nodes["treeitem"].Name, nodes["treeitem"].States.Expanded));
        Assert.Equal("Saves the draft", nodes["tooltip"].Name);
    }

    [Fact]
    public async Task Derives_the_vocabulary_roles_from_HTML_semantics_the_way_HTML_AAM_does()
    {
        using var site = await TinySite.StartAsync(Page($"""
            <style>{EmptyBoxes}</style>
            <header data-testid="page-header">Site</header>
            <nav data-testid="nav"></nav>
            <main data-testid="main">
              <article data-testid="article"><header data-testid="article-header">Post</header></article>
              <section aria-label="Pricing" data-testid="named-section"></section>
              <section data-testid="plain-section">Unnamed</section>
              <form aria-label="Sign in" data-testid="named-form"><input aria-label="User"></form>
              <form data-testid="plain-form"><input aria-label="Query"></form>
              <fieldset data-testid="fieldset"><legend>Notifications</legend></fieldset>
              <details data-testid="details"><summary>More</summary>Body</details>
              <figure data-testid="figure"><figcaption>Figure one</figcaption></figure>
              <hr data-testid="rule">
              <menu data-testid="menu"><li data-testid="menu-item">Cut</li></menu>
              <progress value="3" max="10" aria-label="Upload" data-testid="progress"></progress>
              <meter value="0.5" aria-label="Disk" data-testid="meter"></meter>
              <input type="number" aria-label="Quantity" value="2" data-testid="number">
              <table data-testid="table">
                <caption>Scores</caption>
                <thead data-testid="thead"><tr><th data-testid="col">Name</th></tr></thead>
                <tbody data-testid="tbody"><tr><th scope="row" data-testid="rowhead">Ada</th><td data-testid="cell">1</td></tr></tbody>
              </table>
              <address data-testid="address">Somewhere</address>
            </main>
            <aside data-testid="aside">Related</aside>
            <footer data-testid="page-footer">Legal</footer>
            """));
        var nodes = ByTestId(await CaptureAsync(site.Url));
        AssertRoles(nodes, new()
        {
            ["page-header"] = "banner",
            ["nav"] = "navigation",
            ["main"] = "main",
            ["article"] = "article",
            ["article-header"] = null,
            ["named-section"] = "region",
            ["plain-section"] = null,
            ["named-form"] = "form",
            ["plain-form"] = null,
            ["fieldset"] = "group",
            ["details"] = "group",
            ["figure"] = "figure",
            ["rule"] = "separator",
            ["menu"] = "list",
            ["menu-item"] = "listitem",
            ["progress"] = "progressbar",
            ["meter"] = "meter",
            ["number"] = "spinbutton",
            ["table"] = "table",
            ["thead"] = "rowgroup",
            ["col"] = "columnheader",
            ["tbody"] = "rowgroup",
            ["rowhead"] = "rowheader",
            ["cell"] = "cell",
            ["address"] = "group",
            ["aside"] = "complementary",
            ["page-footer"] = "contentinfo",
        });
        Assert.Equal(("Quantity", "2"), (nodes["number"].Name, nodes["number"].Value));
        Assert.Equal("Upload", nodes["progress"].Name);

        // A legend, a figcaption, and a caption name their parent, so the node the reader
        // reports carries the name the role selector matched on.
        Assert.Equal("Notifications", nodes["fieldset"].Name);
        Assert.Equal("Figure one", nodes["figure"].Name);
        Assert.Equal("Scores", nodes["table"].Name);
        Assert.Null(nodes["details"].Name);
    }

    private static string Page(string body) => $"<!DOCTYPE html><html><body>{body}</body></html>";

    private static async Task<List<SemanticNode>> CaptureAsync(string url)
    {
        await using var engine = await WebSemanticsTests.OpenAsync(url, new WebEngineOptions { Headless = true });
        return WebSemanticsTests.Flatten((await engine.ObserveAsync(CancellationToken.None)).Roots).ToList();
    }

    private static Dictionary<string, SemanticNode> ByTestId(IEnumerable<SemanticNode> tree) =>
        tree.Where(node => node.TestId is not null).ToDictionary(node => node.TestId!);

    // As upstream's toEqual, an expected null role holds when the node is missing or has no role.
    private static void AssertRoles(Dictionary<string, SemanticNode> nodes, Dictionary<string, string?> expected)
    {
        Assert.Equal(
            expected.Where(entry => entry.Value is not null).OrderBy(entry => entry.Key, StringComparer.Ordinal),
            nodes.Where(entry => entry.Value.Role is not null).Select(entry => KeyValuePair.Create(entry.Key, entry.Value.Role)).OrderBy(entry => entry.Key, StringComparer.Ordinal));
    }

    private static async Task<List<string?>> TestIdsAsync(Locator locator)
    {
        var testIds = new List<string?>();
        foreach (var match in await locator.AllAsync())
        {
            testIds.Add(await match.GetAttributeAsync("data-testid"));
        }

        return testIds;
    }
}
