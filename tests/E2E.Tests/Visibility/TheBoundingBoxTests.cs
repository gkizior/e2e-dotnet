// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class TheBoundingBoxTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public Task Display_none() => BoxAsync("""<div id="box" style="display:none">gone</div>""", null);

    [Fact]
    public Task Under_a_display_none_ancestor() => BoxAsync("""<div style="display:none"><span id="box">gone</span></div>""", null);

    [Fact]
    public Task An_empty_display_contents_element() => BoxAsync("""<div id="box" style="display:contents"></div>""", null);

    [Fact]
    public Task Display_contents_around_text() => BoxAsync("""<div id="box" style="display:contents">text</div>""", null);

    [Fact]
    public Task Visibility_hidden_which_keeps_its_box() => BoxAsync("""<div id="box" style="visibility:hidden;width:30px;height:20px"></div>""", (30, 20));

    [Fact]
    public Task A_zero_size_box() => BoxAsync("""<div id="box" style="width:0;height:0"></div>""", (0, 0));

    /// <summary>The reader's rect for the element next to Playwright's own bounding box, and its size.</summary>
    private async Task BoxAsync(string html, (double Width, double Height)? size)
    {
        // The id is the reader's test id too, so the box is listed whatever its role.
        await chromium.Page.SetContentAsync($"""<body style="margin:0">{html.Replace("id=\"box\"", "id=\"box\" data-testid=\"box\"", StringComparison.Ordinal)}</body>""");
        var locator = chromium.Page.Locator("#box");
        var node = await chromium.ReadAsync(locator);
        Assert.NotNull(node);
        var reader = node.Rect;
        var box = await locator.BoundingBoxAsync();
        Assert.Equal(box is null ? null : new BoundingBox(box.X, box.Y, box.Width, box.Height), reader);
        Assert.Equal(size, reader is null ? null : ((double Width, double Height)?)(reader.Width, reader.Height));
    }
}
