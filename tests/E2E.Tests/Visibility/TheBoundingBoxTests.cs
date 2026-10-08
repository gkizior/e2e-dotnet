// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class TheBoundingBoxTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Is_null_for_a_node_with_no_layout_box()
    {
        var cases = new (string Case, string Html, (double Width, double Height)? Size)[]
        {
            ("display: none", """<div id="box" style="display:none">gone</div>""", null),
            ("under a display: none ancestor", """<div style="display:none"><span id="box">gone</span></div>""", null),
            ("an empty display: contents element", """<div id="box" style="display:contents"></div>""", null),
            ("display: contents around text", """<div id="box" style="display:contents">text</div>""", null),
            ("visibility: hidden, which keeps its box", """<div id="box" style="visibility:hidden;width:30px;height:20px"></div>""", (30, 20)),
            ("a zero-size box", """<div id="box" style="width:0;height:0"></div>""", (0, 0)),
        };
        foreach (var (name, html, size) in cases)
        {
            // The id is the reader's test id too, so the box is listed whatever its role.
            await chromium.Page.SetContentAsync($"""<body style="margin:0">{html.Replace("id=\"box\"", "id=\"box\" data-testid=\"box\"", StringComparison.Ordinal)}</body>""");
            var locator = chromium.Page.Locator("#box");
            var node = await chromium.ReadAsync(locator);
            Assert.True(node is not null, name);
            var reader = node.Rect;
            var box = await locator.BoundingBoxAsync();
            var byPlaywright = box is null ? null : new BoundingBox(box.X, box.Y, box.Width, box.Height);
            Assert.True(reader == byPlaywright, $"{name}: reader {reader}, Playwright {byPlaywright}");
            Assert.True(size == (reader is null ? null : ((double Width, double Height)?)(reader.Width, reader.Height)), name);
        }
    }
}
