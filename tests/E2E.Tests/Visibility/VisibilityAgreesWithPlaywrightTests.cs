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

    // Not ported: "a zero-size box with clipped content" (#zero). The tree's one hidden state is also
    // what role queries skip, so it leaves out a box with no size (see the aria-hidden rows below).

    [Fact]
    public Task An_SVG_with_visibility_hidden() => AgreesAsync("svg-hidden", true);

    [Fact]
    public Task A_shown_SVG() => AgreesAsync("svg-shown", false);

    [Fact]
    public Task The_button_clipped_inside_a_zero_size_box_which_keeps_a_box_of_its_own() => AgreesAsync("zero-button", false);

    [Fact]
    public Task A_sized_box() => AgreesAsync("sized", false);

    [Fact]
    public Task A_button_inside_closed_details() => AgreesAsync("in-closed", true);

    [Fact]
    public Task The_summary_of_closed_details() => AgreesAsync("closed-summary", false);

    [Fact]
    public Task A_button_inside_open_details() => AgreesAsync("in-open", false);

    [Fact]
    public Task Open_details_nested_in_the_body_of_closed_details() => AgreesAsync("nested", true);

    [Fact]
    public Task The_summary_of_details_nested_in_closed_details() => AgreesAsync("nested-summary", true);

    [Fact]
    public Task A_button_inside_open_details_nested_in_closed_details() => AgreesAsync("in-nested", true);

    [Fact]
    public Task An_empty_display_contents_element() => AgreesAsync("contents-empty", true);

    [Fact]
    public Task A_display_contents_element_whose_only_child_is_hidden() => AgreesAsync("contents-hidden-child", true);

    [Fact]
    public Task A_display_contents_element_with_a_painted_child() => AgreesAsync("contents-painted", false);

    [Fact]
    public Task A_display_contents_element_with_bare_text() => AgreesAsync("contents-text", false);

    [Fact]
    public Task A_hidden_display_contents_element_whose_child_inherits_it() => AgreesAsync("contents-hidden-element", true);

    [Fact]
    public Task A_hidden_display_contents_element_whose_child_is_visible_again() => AgreesAsync("contents-shown-child", false);

    [Fact]
    public Task An_aria_hidden_spinner_that_paints() => AgreesAsync("spinner", false);

    [Fact]
    public Task A_button_under_an_aria_hidden_container() => AgreesAsync("under-aria-hidden", false);

    [Fact]
    public Task A_content_visibility_hidden_element_which_keeps_its_box() => AgreesAsync("skipping", false);

    [Fact]
    public Task A_button_content_visibility_hidden_skips() => AgreesAsync("skipped", true);

    [Fact]
    public Task A_button_slotted_under_a_content_visibility_hidden_host() => AgreesAsync("skipped-slotted", true);

    [Fact]
    public Task A_visibility_hidden_parent() => AgreesAsync("vis-parent", true);

    [Fact]
    public Task A_child_that_sets_visibility_visible_under_a_hidden_parent() => AgreesAsync("vis-child", false);

    private async Task AgreesAsync(string testId, bool hidden)
    {
        await chromium.Page.SetContentAsync(Html);
        var locator = chromium.Page.GetByTestId(testId);
        var reader = (await chromium.ReadAsync(locator))?.Hidden;
        var byPlaywright = await locator.IsHiddenAsync();
        Assert.Equal((hidden, hidden), (reader, byPlaywright));
    }
}
