// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests.ReadNodeNames;

[Collection(BrowserCollection.Name)]
public sealed class NamesFromContentAndPrecedenceTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Reads_an_embedded_control_by_its_value_as_the_role_selector_does()
    {
        const string html = """
            <button data-testid="textbox">Flash the screen <input value="3"> times</button>
            <button data-testid="search">Find <input type="search" value="q"> now</button>
            <button data-testid="textarea">Note <textarea>hi</textarea> end</button>
            <button data-testid="aria-label-ignored">Name <input aria-label="ignored" value="v"> end</button>
            <button data-testid="select">Pick <select><option>A</option><option selected>B</option></select> now</button>
            <div role="button" data-testid="no-selection">Choose <select><option>A</option><option>B</option></select> now</div>
            <div role="button" data-testid="multiple">Pick <select multiple><option selected>A</option><option selected>B</option></select> now</div>
            <div role="button" data-testid="aria-listbox">Pick <div role="listbox"><div role="option" aria-selected="true">X</div><div role="option">Y</div></div> now</div>
            <div role="button" data-testid="datalist">Find <input list="choices" value="dv"><datalist id="choices"><option>o</option></datalist> now</div>
            <button data-testid="range">Vol <input type="range" min="0" max="10" value="4"> end</button>
            <button data-testid="valuetext">Bass <input type="range" aria-valuetext="four" min="0" max="10" value="4"> end</button>
            <div role="button" data-testid="no-value-attribute">Volume <input type="range" min="0" max="10"> end</div>
            <div role="button" data-testid="spinbutton">Count <input type="number" value="7"> end</div>
            <div role="button" data-testid="progress">Load <progress value="30" max="100"></progress> end</div>
            <div role="button" data-testid="checkbox">Agree <input type="checkbox"> end</div>
            <div role="button" data-testid="hidden-control">Gain <input value="zz" style="display:none"> end</div>
            <div role="button" data-testid="aria-hidden-control">Level <input value="zz" aria-hidden="true"> end</div>
            <span id="count">Count <input value="2"> rows</span><button aria-labelledby="count" data-testid="referenced">z</button>
            <label for="qty">Qty <input value="5"> items</label><input id="qty" data-testid="label-for">
            <label>Inside <input value="9" data-testid="own-label"></label>
            """;
        var cases = new (string TestId, string Role, string Name)[]
        {
            ("textbox", "button", "Flash the screen 3 times"),
            ("search", "button", "Find q now"),
            ("textarea", "button", "Note hi end"),
            // An embedded control's value comes before its aria-label.
            ("aria-label-ignored", "button", "Name v end"),
            ("select", "button", "Pick B now"),
            ("no-selection", "button", "Choose A now"),
            ("multiple", "button", "Pick A B now"),
            ("aria-listbox", "button", "Pick X now"),
            ("datalist", "button", "Find dv now"),
            ("range", "button", "Vol 4 end"),
            ("valuetext", "button", "Bass four end"),
            ("no-value-attribute", "button", "Volume end"),
            ("spinbutton", "button", "Count 7 end"),
            ("progress", "button", "Load 30 end"),
            ("checkbox", "button", "Agree end"),
            ("hidden-control", "button", "Gain end"),
            ("aria-hidden-control", "button", "Level end"),
            ("referenced", "button", "Count 2 rows"),
            ("label-for", "textbox", "Qty 5 items"),
            // A control inside its own label is the name's subject, never part of it.
            ("own-label", "textbox", "Inside"),
        };
        await AssertNamesAsync(html, cases);
    }

    [Fact]
    public async Task Keeps_an_embedded_controls_value_out_of_label_text_and_a_secure_fields_value_out_of_every_name()
    {
        await chromium.Page.SetContentAsync("""
            <label for="qty">Qty <input value="5"> items</label><input id="qty" data-testid="label-for">
            <button data-testid="secure">Unlock <input type="password" value="hunter2"> now</button>
            <label for="pin">PIN <input type="password" value="hunter2"> field</label><input id="pin" data-testid="secure-label">
            """);
        var nodes = await chromium.CaptureAsync();
        // Not ported: the label text upstream reads apart from the name (`labels` is ["Qty items"]); the
        // port has no label text, and its getByLabel matches the accessible name.
        Assert.Equal("label-for", await chromium.Page.GetByLabel("Qty items", new PageGetByLabelOptions { Exact = true }).GetAttributeAsync("data-testid"));
        Assert.Equal("Unlock now", nodes.Single(node => node.TestId == "secure").Name);
        Assert.Equal("PIN field", nodes.Single(node => node.TestId == "secure-label").Name);
        Assert.DoesNotContain("hunter2", JsonSerializer.Serialize(nodes), StringComparison.Ordinal);
    }

    private const string Pixel = "data:image/gif;base64,R0lGODlhAQABAAAAACw=";

    [Fact]
    public async Task Names_a_control_from_its_descendants_the_way_the_role_selector_does_aria_label_alt_content_then_title()
    {
        await AssertNamesAsync($$"""
            <button data-testid="img"><img src="{{Pixel}}" alt="Search"></button>
            <button data-testid="svg"><svg aria-label="Close" width="10" height="10"></svg></button>
            <button data-testid="nested"><span><img src="{{Pixel}}" alt="Deep"></span></button>
            <button data-testid="labelled-span"><span aria-label="Inner"><b>Bold</b></span></button>
            <button data-testid="mixed"><img src="{{Pixel}}" alt="Search">Go</button>
            <button data-testid="blocks"><div>A</div><div>B</div></button>
            <button data-testid="titled"><span title="Settings"></span></button>
            <button data-testid="text-over-title"><span title="Tip">Visible</span></button>
            <button data-testid="hidden"><img src="{{Pixel}}" alt="Hidden" style="display:none"><span aria-hidden="true">x</span><img src="{{Pixel}}" alt="Kept"></button>
            <a href="#" data-testid="link"><img src="{{Pixel}}" alt="Home"></a>
            <input type="image" src="{{Pixel}}" alt="Go" data-testid="image-input">
            """,
            [
                ("img", "button", "Search"),
                ("svg", "button", "Close"),
                ("nested", "button", "Deep"),
                ("labelled-span", "button", "Inner"),
                ("mixed", "button", "SearchGo"),
                ("blocks", "button", "A B"),
                ("titled", "button", "Settings"),
                ("text-over-title", "button", "Visible"),
                ("hidden", "button", "Kept"),
                ("link", "link", "Home"),
                ("image-input", "button", "Go"),
            ]);
    }

    [Fact]
    public async Task Reads_aria_labelledby_before_aria_label_and_either_before_a_label_element()
    {
        await AssertNamesAsync($$"""
            <span id="heading">Shipping address</span>
            <input aria-labelledby="heading" aria-label="Address" data-testid="referenced">
            <label for="named">Label text</label><input id="named" aria-label="Aria text" data-testid="labelled">
            <span id="first">First</span><span id="second"><img src="{{Pixel}}" alt="Pic"></span>
            <button aria-labelledby="first second" data-testid="two-references">x</button>
            """,
            [
                ("referenced", "textbox", "Shipping address"),
                ("labelled", "textbox", "Aria text"),
                ("two-references", "button", "First Pic"),
            ],
            async playwright => Assert.Equal(0, await playwright.Page.GetByRole(AriaRole.Textbox, new PageGetByRoleOptions { Name = "Address", Exact = true }).CountAsync()));
    }

    [Fact]
    public async Task Reads_a_referenced_target_whole_when_it_is_hidden_by_its_own_aria_label_and_drops_hidden_parts_of_a_shown_one()
    {
        await AssertNamesAsync("""
            <button aria-labelledby="l1" aria-label="Delete" data-testid="hidden-reference"><span id="l1" hidden>Delete account</span></button>
            <button aria-labelledby="l2" aria-label="Delete" data-testid="aria-hidden-reference"><span id="l2" aria-hidden="true">Remove account</span></button>
            <button aria-labelledby="l3" data-testid="nested-hidden-reference">x</button>
            <div hidden><span id="l3">Deep <i style="display:none">gone</i>label</span></div>
            <button aria-labelledby="l4" data-testid="shown-reference">x</button>
            <span id="l4">Shown <i style="display:none">gone</i></span>
            <button aria-labelledby="l5" data-testid="labelled-reference">x</button>
            <span id="l5" aria-label="Own label">Text</span>
            <button aria-labelledby="l6" data-testid="aria-hidden-ancestor-reference">x</button>
            <div aria-hidden="true"><span id="l6">Label <i style="display:none">gone</i></span></div>
            """,
            [
                ("hidden-reference", "button", "Delete account"),
                ("aria-hidden-reference", "button", "Remove account"),
                ("nested-hidden-reference", "button", "Deep gone label"),
                ("shown-reference", "button", "Shown"),
                ("labelled-reference", "button", "Own label"),
                // An aria-hidden ancestor hides the target as its own attribute would, so it is read whole.
                ("aria-hidden-ancestor-reference", "button", "Label gone"),
            ]);
    }

    [Fact]
    public async Task Resolves_an_aria_labelledby_id_in_the_elements_own_shadow_tree_not_the_document()
    {
        await AssertNamesAsync("""
            <span id="l">Outside</span>
            <x-host></x-host>
            <script>
              const root = document.querySelector('x-host').attachShadow({ mode: 'open' });
              root.innerHTML = '<span id="l">Inside</span><button aria-labelledby="l" data-testid="inside">x</button>';
            </script>
            """,
            [("inside", "button", "Inside")]);
    }

    [Fact]
    public async Task Follows_a_descendant_reference_in_a_name_from_content_once_per_element_and_never_from_inside_another()
    {
        await AssertNamesAsync("""
            <button data-testid="svg-reference"><svg aria-labelledby="t1" width="10" height="10"><title id="t1">Close</title></svg>Icon</button>
            <button data-testid="block-svg-reference"><svg aria-labelledby="t2" style="display:block" width="10" height="10"><title id="t2">Close</title></svg>Icon</button>
            <button data-testid="svg-title"><svg width="10" height="10"><title>Svg title</title></svg></button>
            <button data-testid="two-references"><span aria-labelledby="p1 p2"></span></button>
            <span id="p1">One</span><span id="p2">Two</span>
            <button data-testid="hidden-target"><span aria-labelledby="p3"></span></button>
            <span id="p3" hidden>Hidden target</span>
            <button data-testid="cycle"><span id="c1" aria-labelledby="c2">A</span><span id="c2" aria-labelledby="c1">B</span></button>
            <button id="ancestor" data-testid="ancestor-reference"><span aria-labelledby="ancestor">A</span>B</button>
            <button id="me" aria-labelledby="me" data-testid="self-reference">Self</button>
            <button aria-labelledby="r1" data-testid="nested-reference">x</button>
            <span id="r1"><span aria-labelledby="r2">Ref text</span></span><span id="r2">Nested</span>
            """,
            [
                ("svg-reference", "button", "CloseIcon"),
                ("block-svg-reference", "button", "Close Icon"),
                ("svg-title", "button", "Svg title"),
                ("two-references", "button", "One Two"),
                ("hidden-target", "button", "Hidden target"),
                ("cycle", "button", "B"),
                ("ancestor-reference", "button", "AB"),
                ("self-reference", "button", "Self"),
                ("nested-reference", "button", "Ref text"),
            ]);
    }

    [Fact]
    public async Task Names_every_role_accname_allows_from_content_a_shadow_tree_included_and_a_reset_input_by_its_value_or_default()
    {
        var html = """
            <div role="checkbox" aria-checked="false" data-testid="checkbox">Remember me</div>
            <span role="radio" aria-checked="true" data-testid="radio">Monthly</span>
            <div role="switch" aria-checked="false" data-testid="switch">Dark mode</div>
            <table>
              <tr><th data-testid="columnheader">Price</th><th scope="row" data-testid="rowheader">Ada</th><td data-testid="cell">42</td></tr>
            </table>
            <div role="grid"><div role="row" data-testid="row"><div role="gridcell" data-testid="gridcell">A1</div></div></div>
            <x-button role="button" tabindex="0" data-testid="custom-button"><span slot="icon">*</span></x-button>
            <x-label role="button" tabindex="0" data-testid="slotted-button">Slotted</x-label>
            <x-action role="button" tabindex="0" data-testid="closed-slotted-button">Save</x-action>
            <input type="reset" value="Clear" data-testid="reset-value">
            <input type="reset" data-testid="reset-default">
            <input type="submit" data-testid="submit-default">
            <button data-testid="plain">Plain</button>
            <input type="submit" value="Send" data-testid="submit-value">
            <script>
              document.querySelector('x-button').attachShadow({ mode: 'open' }).innerHTML = '<slot name="icon"></slot><span>Custom</span>';
              document.querySelector('x-label').attachShadow({ mode: 'open' }).innerHTML = '<b>[</b><slot></slot><b>]</b>';
              document.querySelector('x-action').attachShadow({ mode: 'closed' }).innerHTML = '<b>[</b><slot></slot><b>]</b>';
            </script>
            """;
        await AssertNamesAsync(html,
            [
                ("checkbox", "checkbox", "Remember me"),
                ("radio", "radio", "Monthly"),
                ("switch", "switch", "Dark mode"),
                ("columnheader", "columnheader", "Price"),
                ("rowheader", "rowheader", "Ada"),
                ("cell", "cell", "42"),
                ("row", "row", "A1"),
                ("gridcell", "gridcell", "A1"),
                ("custom-button", "button", "* Custom"),
                ("slotted-button", "button", "[ Slotted ]"),
                ("reset-value", "button", "Clear"),
                ("reset-default", "button", "Reset"),
                ("submit-default", "button", "Submit"),
                ("plain", "button", "Plain"),
                ("submit-value", "button", "Send"),
            ]);

        // A closed root's slot lists its assigned nodes while the light child's `assignedSlot` reads null
        // from outside, so the slotted text is read once, at the slot. Playwright's role selector cannot
        // see a closed root and names the host "Save", so there is no locator to compare against.
        var nodes = await CaptureByTestIdAsync();
        Assert.True(nodes["closed-slotted-button"].Role == "button" && nodes["closed-slotted-button"].Name == "[ Save ]", nodes["closed-slotted-button"].Name);
    }

    // Sets the page's content, then checks the tree's role and name for each case and the one element
    // Playwright's role selector resolves for that role and exact name (upstream's locatedTestId) on the same
    // page. `more` reads further from that page.
    private async Task AssertNamesAsync(string body, (string TestId, string Role, string Name)[] cases, Func<ChromiumPage, Task>? more = null)
    {
        await chromium.Page.SetContentAsync(body);
        var nodes = await CaptureByTestIdAsync();
        foreach (var (testId, role, name) in cases)
        {
            Assert.True(nodes.TryGetValue(testId, out var node) && node.Role == role && node.Name == name, $"{testId}: {node?.Role} \"{node?.Name}\"");
            Assert.Equal(testId, await LocatedTestIdAsync(role, name));
        }

        if (more is not null)
        {
            await more(chromium);
        }
    }

    // The test id of the one element Playwright's role selector resolves for this role and exact name.
    private Task<string?> LocatedTestIdAsync(string role, string name) =>
        chromium.Page.GetByRole(Enum.Parse<AriaRole>(role, ignoreCase: true), new PageGetByRoleOptions { Name = name, Exact = true }).GetAttributeAsync("data-testid");

    private async Task<Dictionary<string, ReadNode>> CaptureByTestIdAsync() =>
        (await chromium.CaptureAsync()).Where(node => node.TestId is not null).ToDictionary(node => node.TestId!);
}
