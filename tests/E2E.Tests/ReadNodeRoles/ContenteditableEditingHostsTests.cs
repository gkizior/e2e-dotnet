// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ReadNodeRoles;

[Collection(BrowserCollection.Name)]
public sealed class ContenteditableEditingHostsTests
{
    private const string Pixel = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAAAAAAALAAAAAABAAEAAAIBRAA7";

    [Fact]
    public async Task Reports_the_host_as_a_textbox_named_by_aria_label_aria_placeholder_or_the_editor_placeholder_with_its_document_as_the_value()
    {
        using var site = await TinySite.StartAsync(Page("""
            <div contenteditable aria-label="Notes" data-testid="labelled"><p data-testid="block">Hello</p><p>World</p></div>
            <div contenteditable aria-placeholder="Write here" data-testid="aria-placeholder"><p><br></p></div>
            <div contenteditable class="tiptap ProseMirror" tabindex="0" data-testid="tiptap">
              <p class="is-empty is-editor-empty" data-placeholder="Write something"><br></p>
            </div>
            <div contenteditable class="ql-editor ql-blank" data-placeholder="Compose a message" data-testid="quill"><p><br></p></div>
            <div contenteditable role="textbox" aria-label="Message" data-testid="explicit"><p>Hi</p></div>
            <div contenteditable data-testid="bare"><p>Bare</p></div>
            <div contenteditable aria-label="Outer" data-testid="outer">
              <p>Text</p>
              <span contenteditable="false" data-testid="island">Chip<div contenteditable aria-label="Nested" data-testid="nested"><p>Inner</p></div></span>
            </div>
            """));
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var nodes = await NodesAsync(session);
            AssertTextbox(nodes["labelled"], "Notes", "Hello\n\nWorld");
            // Only the host is the control: a block inside it is content, not a second textbox.
            Assert.Null(nodes["block"].Role);
            AssertTextbox(nodes["aria-placeholder"], "Write here", "");
            AssertTextbox(nodes["tiptap"], "Write something");
            AssertTextbox(nodes["quill"], "Compose a message");
            AssertTextbox(nodes["explicit"], "Message", "Hi");
            AssertTextbox(nodes["bare"], null, "Bare");
            AssertTextbox(nodes["outer"], "Outer");
            Assert.Null(nodes["island"].Role);
            AssertTextbox(nodes["nested"], "Nested", "Inner");
            // The port finds a bare host by role too, since it reads the tree; Playwright's role selector needs the explicit role.
            Assert.Equal(["explicit"], await TestIdsAsync(session.Screen.GetByRole("textbox", "Message")));
        });
    }

    [Fact]
    public async Task Keeps_the_whitespace_an_editor_renders_as_its_value_and_reads_an_empty_editor_as_an_empty_value()
    {
        using var site = await TinySite.StartAsync(Page("""
            <div contenteditable style="white-space: pre-wrap" aria-label="Code" data-testid="pre">  keep spaces  </div>
            <div contenteditable style="white-space: pre-wrap" aria-label="Lines" data-testid="lines"><p>line1</p><p>  line2</p></div>
            <div contenteditable aria-label="Prose" data-testid="prose">  collapsed   text  </div>
            <div contenteditable style="white-space: pre-wrap" aria-label="Spaces" data-testid="spaces">   </div>
            <div contenteditable aria-label="Empty" data-testid="empty"><p><br></p></div>
            <div contenteditable style="white-space: pre-wrap" aria-label="Empty pre" data-testid="empty-pre"><p><br></p></div>
            <div contenteditable aria-label="Blank" data-testid="blank"></div>
            <div contenteditable style="white-space: pre-wrap" aria-label="Slate" data-testid="slate"><div><span>&#xFEFF;<br></span></div></div>
            <div contenteditable style="white-space: pre-wrap" aria-label="Slate lines" data-testid="slate-lines"><div><span>hi</span></div><div><span>&#xFEFF;<br></span></div></div>
            <input aria-label="Field" value="  keep spaces  " data-testid="field">
            """));
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var nodes = await NodesAsync(session);
            Assert.Equal("  keep spaces  ", nodes["pre"].Value);
            Assert.Equal("line1\n\n  line2", nodes["lines"].Value);
            // Under white-space: normal the browser renders no leading or trailing space, so none is read.
            Assert.Equal("collapsed text", nodes["prose"].Value);
            Assert.Equal("   ", nodes["spaces"].Value);
            Assert.Equal("", nodes["empty"].Value);
            Assert.Equal("", nodes["empty-pre"].Value);
            Assert.Equal("", nodes["blank"].Value);
            Assert.Equal("", nodes["slate"].Value);
            Assert.Equal("hi\n\n", nodes["slate-lines"].Value);
            Assert.Equal("  keep spaces  ", nodes["field"].Value);
        });
    }

    [Fact]
    public async Task Makes_an_editing_host_a_textbox_whatever_role_its_tag_carries_and_leaves_a_native_control_its_own()
    {
        using var site = await TinySite.StartAsync(Page($"""
            <article contenteditable data-testid="article"><p>Post</p></article>
            <h2 contenteditable data-testid="heading">Title</h2>
            <ul><li contenteditable data-testid="item">Item</li></ul>
            <section contenteditable aria-label="Notes" data-testid="section"></section>
            <button contenteditable data-testid="button">Save</button>
            <a href="#" contenteditable data-testid="link">Home</a>
            <select contenteditable data-testid="select"><option>a</option></select>
            <img contenteditable alt="Pic" src="{Pixel}" data-testid="image">
            """));
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var nodes = await NodesAsync(session);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["article"] = "textbox",
                    ["heading"] = "textbox",
                    ["item"] = "textbox",
                    ["section"] = "textbox",
                    ["button"] = "button",
                    ["link"] = "link",
                    ["select"] = "combobox",
                    ["image"] = "image",
                }.OrderBy(entry => entry.Key, StringComparer.Ordinal),
                nodes.Select(entry => KeyValuePair.Create(entry.Key, entry.Value.Role!)).OrderBy(entry => entry.Key, StringComparer.Ordinal));
            Assert.Equal("Post", nodes["article"].Value);
            Assert.Equal(("Notes", ""), (nodes["section"].Name, nodes["section"].Value));
        });
    }

    [Fact]
    public async Task Never_makes_a_drawn_or_embedded_surface_a_textbox_whatever_its_contenteditable_says()
    {
        using var site = await TinySite.StartAsync(Page("""
            <canvas contenteditable tabindex="0" width="120" height="60" data-testid="canvas"></canvas>
            <svg contenteditable width="20" height="20" data-testid="svg"></svg>
            <iframe contenteditable srcdoc="<p>Framed</p>" data-testid="frame"></iframe>
            <div contenteditable data-testid="host"><canvas width="20" height="20"></canvas><p>Caption</p></div>
            """));
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var nodes = await NodesAsync(session);
            // A canvas has no role; an svg has the image role; an iframe is a frame; only the host is a textbox.
            Assert.Equal(
                new Dictionary<string, string?> { ["canvas"] = null, ["svg"] = "image", ["frame"] = "iframe", ["host"] = "textbox" }.OrderBy(entry => entry.Key, StringComparer.Ordinal),
                nodes.Select(entry => KeyValuePair.Create(entry.Key, entry.Value.Role)).OrderBy(entry => entry.Key, StringComparer.Ordinal));
        });
    }

    [Fact]
    public async Task Drops_a_name_taken_from_a_block_placeholder_once_the_editor_has_content_and_keeps_one_from_the_host()
    {
        using var site = await TinySite.StartAsync(Page("""
            <div contenteditable class="tiptap ProseMirror" data-testid="tiptap">
              <p class="is-empty is-editor-empty" data-placeholder="Write something"><br></p>
            </div>
            <div contenteditable class="ql-editor ql-blank" data-placeholder="Compose a message" data-testid="quill"><p><br></p></div>
            <div contenteditable aria-placeholder="Write here" data-testid="aria-placeholder"><p><br></p></div>
            <span id="notes-label">Notes</span>
            <div contenteditable aria-labelledby="notes-label" data-testid="labelled">
              <p class="is-empty is-editor-empty" data-placeholder="Write something"><br></p>
            </div>
            """));
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var before = await NodesAsync(session);
            AssertTextbox(before["tiptap"], "Write something", "");
            AssertTextbox(before["quill"], "Compose a message", "");
            AssertTextbox(before["aria-placeholder"], "Write here", "");
            AssertTextbox(before["labelled"], "Notes", "");

            foreach (var testId in new[] { "tiptap", "quill", "aria-placeholder", "labelled" })
            {
                await session.Screen.GetByTestId(testId).FillAsync("Hello");
            }

            // TipTap's Placeholder extension paints the hint as a decoration on the
            // empty block and removes it, class and attribute, once the block has text.
            await session.Browser.EvaluateAsync<bool>("""
                () => {
                  for (const block of document.querySelectorAll('.tiptap [data-placeholder]')) {
                    block.removeAttribute('data-placeholder');
                    block.classList.remove('is-empty', 'is-editor-empty');
                  }
                  return true;
                }
                """);
            var after = await NodesAsync(session);
            AssertTextbox(after["tiptap"], null, "Hello");
            AssertTextbox(after["quill"], "Compose a message", "Hello");
            AssertTextbox(after["aria-placeholder"], "Write here", "Hello");
            AssertTextbox(after["labelled"], "Notes", "Hello");
        });
    }

    [Fact]
    public async Task Follows_the_document_as_it_is_edited_and_focused_as_a_textarea_value_does()
    {
        using var site = await TinySite.StartAsync(Page("""
            <div contenteditable aria-label="Notes" data-testid="editor"><p><br></p></div>
            <textarea aria-label="Plain" data-testid="plain"></textarea>
            """));
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await session.Screen.GetByTestId("plain").FillAsync("Typed");
            await session.Screen.GetByTestId("editor").FillAsync("Typed");
            var nodes = await NodesAsync(session);
            AssertTextbox(nodes["editor"], "Notes", "Typed");
            Assert.True(nodes["editor"].States.Focused);
            AssertTextbox(nodes["plain"], "Plain", "Typed");
            Assert.False(nodes["plain"].States.Focused);
        });
    }

    private static string Page(string body) => $"<!DOCTYPE html><html><body>{body}</body></html>";

    // One observation of the page, by test id: the nodes the agent and the locator reads see.
    private static async Task<Dictionary<string, SemanticNode>> NodesAsync(E2ESession session) =>
        WebSemanticsTests.Flatten((await session.Engine.ObserveAsync(CancellationToken.None)).Roots)
            .Where(node => node.TestId is not null)
            .ToDictionary(node => node.TestId!);

    private static void AssertTextbox(SemanticNode node, string? name, string? value = null)
    {
        Assert.Equal("textbox", node.Role);
        Assert.Equal(name, node.Name);
        if (value is not null)
        {
            Assert.Equal(value, node.Value);
        }
    }

    private static async Task<List<string?>> TestIdsAsync(Locator locator)
    {
        var testIds = new List<string?>();
        foreach (var match in await locator.AllAsync())
        {
            testIds.Add(await match.GetAttributeAsync("data-testid"));
        }

        return testIds;
    }
}
