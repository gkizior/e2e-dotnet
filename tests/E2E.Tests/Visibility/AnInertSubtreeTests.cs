// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class AnInertSubtreeTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Leaves_the_tree_as_Chrome_drops_it_while_Playwright_still_calls_it_visible()
    {
        const string Html = """
            <main inert><h2>Behind the drawer</h2><button id="inert-save">Save</button></main>
            <button>Close drawer</button>
            """;
        var names = (await VisibilitySupport.ObserveAsync(Html)).Select(node => node.Name).OfType<string>().ToList();
        Assert.Contains("Close drawer", names);
        Assert.DoesNotContain("Save", names);
        Assert.DoesNotContain("Behind the drawer", names);
        await chromium.Page.SetContentAsync(Html);
        Assert.False(await chromium.Page.Locator("#inert-save").IsHiddenAsync());
    }

    [Fact]
    public async Task Lists_nothing_under_an_inert_document_root_and_nothing_slotted_into_an_inert_slot_of_a_closed_root()
    {
        var whole = await VisibilitySupport.ObserveAsync("<!doctype html><html inert><body><button>Whole page</button></body></html>");
        Assert.DoesNotContain(whole, node => node.Role == "button");
        var slotted = await VisibilitySupport.ObserveAsync("""
            <inert-slot><button>Slotted</button></inert-slot>
            <button>Outside</button>
            <script>
              customElements.define('inert-slot', class extends HTMLElement {
                connectedCallback() { this.attachShadow({ mode: 'closed' }).innerHTML = '<div inert><slot></slot></div>'; }
              });
            </script>
            """);
        Assert.Equal(["Outside"], slotted.Where(node => node.Role == "button").Select(node => node.Name));
    }
}
