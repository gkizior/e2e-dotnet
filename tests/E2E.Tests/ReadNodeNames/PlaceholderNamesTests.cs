// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ReadNodeNames;

[Collection(BrowserCollection.Name)]
public sealed class PlaceholderNamesTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Names_text_controls_by_placeholder_in_HTML_AAM_order_and_carries_the_attribute()
    {
        await chromium.Page.SetContentAsync("""
            <input type="search" placeholder="Search products" data-testid="search">
            <textarea placeholder="Leave a note" data-testid="note"></textarea>
            <label for="email">Email</label><input id="email" type="email" placeholder="you@example.test">
            <input type="text" title="Promo code" placeholder="Enter code" data-testid="promo">
            <input type="text" aria-placeholder="Amount" data-testid="amount">
            <input type="checkbox" placeholder="Not a text control" data-testid="check">
            """);
        var nodes = await chromium.CaptureAsync();
        var byTestId = nodes.Where(node => node.TestId is not null).ToDictionary(node => node.TestId!);
        var email = nodes.Single(node => node.Name == "Email");

        Assert.Equal("searchbox", byTestId["search"].Role);
        Assert.Equal("Search products", byTestId["search"].Name);
        Assert.Equal("Search products", byTestId["search"].Attributes["placeholder"]);
        Assert.Equal("textbox", byTestId["note"].Role);
        Assert.Equal("Leave a note", byTestId["note"].Name);
        // A label wins over the placeholder; the placeholder still rides along as an attribute.
        Assert.Equal("textbox", email.Role);
        Assert.Equal("you@example.test", email.Attributes["placeholder"]);
        // The title comes before the placeholder, the aria-placeholder after it.
        Assert.Equal("Promo code", byTestId["promo"].Name);
        Assert.Equal("Amount", byTestId["amount"].Name);
        // Only text-like controls are placeholder-named.
        Assert.Null(byTestId["check"].Name);
        Assert.False(chromium.Truncated);
    }
}
