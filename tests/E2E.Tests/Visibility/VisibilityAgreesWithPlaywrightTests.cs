// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class VisibilityAgreesWithPlaywrightTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    // Upstream's page, with a test id on each element a case reads, so every case is listed in the tree.
    private const string Html = """
        <!DOCTYPE html>
        <html><body>
        <svg id="svg-hidden" data-testid="svg-hidden" width="40" height="40" style="visibility:hidden"><rect width="40" height="40"></rect></svg>
        <svg id="svg-shown" data-testid="svg-shown" width="40" height="40" role="img" aria-label="Logo"><rect width="40" height="40"></rect></svg>
        <div id="zero" data-testid="zero" style="width:0;height:0;overflow:hidden"><button data-testid="zero-button">Clipped</button></div>
        <div id="sized" data-testid="sized" style="width:40px;height:40px;overflow:hidden"><button>Shown</button></div>
        <details id="closed" data-testid="closed"><summary data-testid="closed-summary">More</summary><button id="in-closed" data-testid="in-closed">Inside closed</button></details>
        <details id="open" open><summary>More</summary><button id="in-open" data-testid="in-open">Inside open</button></details>
        <details><summary>Outer</summary><details id="nested" data-testid="nested" open><summary data-testid="nested-summary">Inner</summary><button id="in-nested" data-testid="in-nested">Nested</button></details></details>
        <div id="contents-empty" data-testid="contents-empty" style="display:contents"></div>
        <div id="contents-hidden-child" data-testid="contents-hidden-child" style="display:contents"><span style="display:none">gone</span></div>
        <div id="contents-painted" data-testid="contents-painted" style="display:contents"><button>Painted</button></div>
        <div id="contents-text" data-testid="contents-text" style="display:contents">bare text</div>
        <div id="contents-hidden-element" data-testid="contents-hidden-element" style="display:contents;visibility:hidden"><span>hidden span</span></div>
        <div id="contents-shown-child" data-testid="contents-shown-child" style="display:contents;visibility:hidden"><span style="visibility:visible">shown span</span></div>
        <div id="spinner" data-testid="spinner" aria-hidden="true" style="width:40px;height:40px;background:#888">spinning</div>
        <div aria-hidden="true"><button id="under-aria-hidden" data-testid="under-aria-hidden">Decorative</button></div>
        <div id="skipping" data-testid="skipping" style="content-visibility:hidden;width:50px;height:50px"><button id="skipped" data-testid="skipped">Skipped</button></div>
        <skip-host id="skipping-host" data-testid="skipping-host" style="content-visibility:hidden;display:block;width:50px;height:50px"><button id="skipped-slotted" data-testid="skipped-slotted">Slotted</button></skip-host>
        <div id="vis-parent" data-testid="vis-parent" style="visibility:hidden"><button id="vis-child" data-testid="vis-child" style="visibility:visible">Shown child</button></div>
        <script>
          customElements.define("skip-host", class extends HTMLElement {
            connectedCallback() { this.attachShadow({ mode: "open" }).innerHTML = "<div><slot></slot></div>"; }
          });
        </script>
        </body></html>
        """;

    [Fact]
    public async Task Agrees_with_Playwright_on_each_case()
    {
        await chromium.Page.SetContentAsync(Html);
        var cases = new (string Case, string TestId, bool Hidden)[]
        {
            ("an SVG with visibility: hidden", "svg-hidden", true),
            ("a shown SVG", "svg-shown", false),
            // Not ported: "a zero-size box with clipped content" (#zero). The tree's one hidden state is also
            // what role queries skip, so it leaves out a box with no size (see the aria-hidden rows below).
            ("the button clipped inside a zero-size box, which keeps a box of its own", "zero-button", false),
            ("a sized box", "sized", false),
            ("a button inside closed details", "in-closed", true),
            ("the summary of closed details", "closed-summary", false),
            ("a button inside open details", "in-open", false),
            ("open details nested in the body of closed details", "nested", true),
            ("the summary of details nested in closed details", "nested-summary", true),
            ("a button inside open details nested in closed details", "in-nested", true),
            ("an empty display: contents element", "contents-empty", true),
            ("a display: contents element whose only child is hidden", "contents-hidden-child", true),
            ("a display: contents element with a painted child", "contents-painted", false),
            ("a display: contents element with bare text", "contents-text", false),
            ("a hidden display: contents element whose child inherits it", "contents-hidden-element", true),
            ("a hidden display: contents element whose child is visible again", "contents-shown-child", false),
            ("an aria-hidden spinner that paints", "spinner", false),
            ("a button under an aria-hidden container", "under-aria-hidden", false),
            ("a content-visibility: hidden element, which keeps its box", "skipping", false),
            ("a button content-visibility: hidden skips", "skipped", true),
            ("a button slotted under a content-visibility: hidden host", "skipped-slotted", true),
            ("a visibility: hidden parent", "vis-parent", true),
            ("a child that sets visibility: visible under a hidden parent", "vis-child", false),
        };
        foreach (var (name, testId, hidden) in cases)
        {
            var locator = chromium.Page.GetByTestId(testId);
            var reader = (await chromium.ReadAsync(locator))?.Hidden;
            var byPlaywright = await locator.IsHiddenAsync();
            Assert.True(reader == hidden && byPlaywright == hidden, $"{name}: reader {reader}, Playwright {byPlaywright}");
        }
    }
}
