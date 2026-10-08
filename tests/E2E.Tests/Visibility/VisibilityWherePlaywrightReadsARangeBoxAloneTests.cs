// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class VisibilityWherePlaywrightReadsARangeBoxAloneTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Hides_text_under_a_display_contents_element_whose_visibility_is_hidden_which_Playwright_calls_visible()
    {
        await chromium.Page.SetContentAsync("""<div id="contents-hidden-text" style="display:contents;visibility:hidden">Invisible text</div>""");
        var locator = chromium.Page.Locator("#contents-hidden-text");
        var reader = (await chromium.ReadAsync(locator))?.Hidden;
        var byPlaywright = await locator.IsHiddenAsync();
        Assert.Equal((true, false), (reader, byPlaywright));
    }
}
