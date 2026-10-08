// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class TheTreeWalkThroughHiddenContentTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Walks_into_a_visibility_hidden_subtree_for_a_child_that_shows_again_and_stops_at_aria_hidden_and_skipped_content()
    {
        await chromium.Page.SetContentAsync("""
            <div style="visibility:hidden"><button>Hidden parent</button><div><button style="visibility:visible">Shown child</button></div></div>
            <div aria-hidden="true"><button>Decorative</button></div>
            <div style="content-visibility:hidden;width:50px;height:50px" data-testid="skipping">Skipped text<button>Skipped</button></div>
            <div style="display:contents;visibility:hidden"><button>Contents hidden</button></div>
            """);
        var nodes = await chromium.CaptureAsync();
        // The tree lists a hidden node as hidden where upstream leaves it out, so only the shown ones count.
        Assert.Equal(["Shown child"], nodes.Where(node => node.Role == "button" && !node.Hidden).Select(node => node.Name));
        var skipping = nodes.Single(node => node.TestId == "skipping");
        Assert.False(skipping.Hidden);
        // The container keeps its box and paints none of its text, which innerText reads empty too.
        Assert.Equal("", skipping.Text);
        Assert.Equal("", await chromium.Page.GetByTestId("skipping").InnerTextAsync());
    }
}
