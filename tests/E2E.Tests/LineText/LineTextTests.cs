// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.LineText;

/// <summary>
/// The observed tree reads an element's text as a person reads its line: words inside inline
/// descendants (a link, emphasis, code) stay in place in the sentence, the link stays a node of
/// its own, and text-only inline wrappers are not listed a second time. The port maps a
/// <c>&lt;p&gt;</c> to the <c>paragraph</c> role, so each paragraph line starts with it.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class LineTextTests
{
    [Fact]
    public async Task Reads_a_sentence_with_an_inline_link_whole_and_keeps_the_link_as_its_own_node_under_it()
    {
        var nodes = await ObserveAsync("""<p>Read our <a href="/privacy">privacy policy</a> for details.</p>""");
        Assert.Equal(["paragraph Read our privacy policy for details.", """link "privacy policy" privacy policy"""], Lines(nodes));
        Assert.Equal(["link"], nodes[0].Children.Select(child => child.Role));
    }

    [Fact]
    public async Task Reads_emphasis_code_and_spans_in_place_and_lists_none_of_them_again()
    {
        var nodes = await ObserveAsync("<p>Use <strong>bold</strong>, <em>italic <b>nested</b></em>, <code>npx e2e</code>, and a <span>span</span>.</p>");
        Assert.Equal(["paragraph Use bold, italic nested, npx e2e, and a span."], Lines(nodes));
    }

    [Fact]
    public async Task Keeps_an_inline_node_named_by_a_test_id_while_its_words_stay_in_the_sentence()
    {
        var nodes = await ObserveAsync("""<p>Total <span data-testid="total">$10</span> due today</p>""");
        Assert.Equal(["paragraph Total $10 due today", "$10"], Lines(nodes));
        Assert.Equal("total", nodes[1].TestId);
    }

    [Fact]
    public async Task Keeps_an_inline_element_that_offers_an_action_listed_its_words_still_in_the_sentence()
    {
        var nodes = await ObserveAsync("""
            <p>Text <span onclick="" tabindex="0">change</span> here</p>
            <p>Or <span id="wired">resend</span> the code</p>
            <p>See <a>the note</a> below</p>
            <p>Pick <span tabindex="-1"><b>this</b></span> now</p>
            <div tabindex="-1"><span>Outside</span> <span tabindex="0"><b>any</b></span></div>
            <script>document.getElementById('wired').onclick = () => undefined;</script>
            """);
        Assert.Equal(
            [
                "paragraph Text change here",
                "change",
                "paragraph Or resend the code",
                "resend",
                "paragraph See the note below",
                "the note",
                "paragraph Pick this now",
                "this",
                "Outside",
                "any",
            ],
            Lines(nodes));
    }

    [Fact]
    public async Task Lists_the_children_of_a_wrapper_with_no_text_of_its_own_as_before()
    {
        var nodes = await ObserveAsync("<div><span>One</span><span>Two</span></div><button><span>Save</span></button>");
        Assert.Equal(["One", "Two", """button "Save" Save"""], Lines(nodes));
    }

    [Fact]
    public async Task Separates_words_at_block_children_line_breaks_and_form_controls_and_reads_no_hidden_inline_text()
    {
        // The label of a control is the control's name, not a node, so the form-control line is a div.
        var nodes = await ObserveAsync("""
            <div>Intro<p>Block</p>tail</div>
            <p>one<br>two</p>
            <div>Show <select aria-label="Rows"><option>10</option></select> per page</div>
            <p>Shown <span style="display:none">gone</span><span aria-hidden="true">decor</span><span style="visibility:hidden">ghost</span> end</p>
            <p>Price <span style="display:inline-block;width:0;height:0;overflow:hidden">$99</span><span style="font-size:0">$98</span> today</p>
            <p>extra<wbr>ordinary</p>
            """);
        var texts = nodes.Where(node => node.Role is null or "paragraph").Select(node => node.OwnText ?? node.Text);
        Assert.Equal(["Intro tail", "Block", "one two", "Show per page", "Shown decor end", "Price today", "extraordinary"], texts);
    }

    [Fact]
    public async Task Reads_the_shadow_tree_an_inline_custom_element_renders_open_or_closed_and_the_light_text_it_slots()
    {
        var nodes = await ObserveAsync("""
            <p>Signed in as <open-name></open-name>, welcome back.</p>
            <p>Your plan: <closed-plan></closed-plan> until May.</p>
            <p>Hello <slot-greet>Ada <unused-light style="display:none">x</unused-light></slot-greet>!</p>
            <script>
              customElements.define('open-name', class extends HTMLElement {
                connectedCallback() { this.attachShadow({ mode: 'open' }).innerHTML = '<b>Ada</b> <a href="/me">Lovelace</a>'; }
              });
              customElements.define('closed-plan', class extends HTMLElement {
                connectedCallback() { this.attachShadow({ mode: 'closed' }).innerHTML = '<strong>Pro</strong>'; }
              });
              customElements.define('slot-greet', class extends HTMLElement {
                connectedCallback() { this.attachShadow({ mode: 'open' }).innerHTML = 'dear <em><slot></slot></em>'; }
              });
            </script>
            """);
        Assert.Equal(
            [
                "paragraph Signed in as Ada Lovelace, welcome back.",
                """link "Lovelace" Lovelace""",
                "paragraph Your plan: Pro until May.",
                "paragraph Hello dear Ada !",
            ],
            Lines(nodes));
    }

    [Fact]
    public async Task Reads_no_slotted_text_a_hidden_slot_hides_and_lists_a_slotted_child_that_shows_itself_again()
    {
        // The page records what the browser itself reads for the paragraph, as upstream's innerText check does.
        var nodes = await ObserveAsync("""
            <p>Status <hidden-slot>secret <span style="visibility:visible">shown</span></hidden-slot> end</p>
            <script>
              customElements.define('hidden-slot', class extends HTMLElement {
                connectedCallback() { this.attachShadow({ mode: 'open' }).innerHTML = '<slot style="visibility:hidden"></slot>'; }
              });
              const paragraph = document.querySelector('p');
              paragraph.setAttribute('data-inner-text', paragraph.innerText);
            </script>
            """);
        Assert.Equal(["paragraph Status end", "shown"], Lines(nodes));
        Assert.Equal("Status shown end", nodes[0].Attributes["data-inner-text"]);
    }

    [Fact]
    public async Task Keeps_listing_the_inline_text_of_a_line_too_long_for_the_text_bound_so_nothing_past_the_cut_is_lost()
    {
        var nodes = await ObserveAsync($"<p>{string.Concat(Enumerable.Repeat("word ", 120))}<strong>Total: $42</strong></p>");
        Assert.Equal(2, nodes.Count);
        Assert.StartsWith("word word", (nodes[0].OwnText ?? nodes[0].Text));
        Assert.Equal("Total: $42", (nodes[1].OwnText ?? nodes[1].Text));
    }

    /// <summary>
    /// Every node of one observation of <paramref name="body"/> that paints, in document order. The port also lists
    /// hidden nodes and boxes with no size, flagged as such; upstream's tree leaves them out.
    /// </summary>
    private static async Task<List<SemanticNode>> ObserveAsync(string body)
    {
        using var site = await TinySite.StartAsync($"<!DOCTYPE html><html><body>{body}</body></html>");
        await using var engine = await WebSemanticsTests.OpenAsync(site.Url, new WebEngineOptions { Headless = true });
        return WebSemanticsTests.Flatten((await engine.ObserveAsync(CancellationToken.None)).Roots)
            .Where(node => !node.States.Hidden && node.Rect is { Width: > 0, Height: > 0 })
            .ToList();
    }

    /// <summary>The nodes as <c>role "name" text</c>, the parts a model reads.</summary>
    private static List<string> Lines(IEnumerable<SemanticNode> nodes) =>
        nodes.Select(node => string.Join(' ', new[] { node.Role, node.Name is null ? null : $"\"{node.Name}\"", node.OwnText ?? node.Text }.Where(part => !string.IsNullOrEmpty(part)))).ToList();
}
