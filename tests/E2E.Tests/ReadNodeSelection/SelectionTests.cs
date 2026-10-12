// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ReadNodeSelection;

[Collection(BrowserCollection.Name)]
public sealed class SelectionTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Reads_the_selection_of_the_focused_field_and_editing_host()
    {
        await chromium.Page.SetContentAsync("""
            <input data-testid="field" value="release approved">
            <textarea data-testid="note">first line</textarea>
            <div data-testid="editor" contenteditable>release approved</div>
            <input type="password" data-testid="secret" value="hunter2">
            """);

        await chromium.Page.GetByTestId("field").FocusAsync();
        await chromium.Page.GetByTestId("field").EvaluateAsync("el => el.setSelectionRange(8, 16)");
        var nodes = await chromium.CaptureAsync();
        var field = Node(nodes, "field");
        Assert.Equal("release approved", field.Value);
        Assert.Equal("approved", field.Selection);
        Assert.True(field.Focused);
        Assert.Null(Node(nodes, "note").Selection);
        Assert.Null(Node(nodes, "editor").Selection);

        await chromium.Page.GetByTestId("note").FocusAsync();
        await chromium.Page.GetByTestId("note").EvaluateAsync("el => el.setSelectionRange(6, 10)");
        nodes = await chromium.CaptureAsync();
        Assert.Equal("line", Node(nodes, "note").Selection);
        // The input keeps its range while unfocused; only the focused field reports one.
        Assert.Null(Node(nodes, "field").Selection);

        await chromium.Page.GetByTestId("editor").ClickAsync();
        await chromium.Page.Keyboard.PressAsync("End");
        nodes = await chromium.CaptureAsync();
        var editor = Node(nodes, "editor");
        Assert.True(editor.Focused);
        Assert.Null(editor.Selection);

        for (var index = 0; index < "approved".Length; index++)
        {
            await chromium.Page.Keyboard.PressAsync("Shift+ArrowLeft");
        }

        nodes = await chromium.CaptureAsync();
        Assert.Equal("approved", Node(nodes, "editor").Selection);

        await chromium.Page.GetByTestId("secret").FocusAsync();
        await chromium.Page.GetByTestId("secret").EvaluateAsync("el => el.select()");
        nodes = await chromium.CaptureAsync();
        var secret = Node(nodes, "secret");
        Assert.True(secret.Secure);
        Assert.Null(secret.Value);
        Assert.Null(secret.Selection);
    }

    private static ReadNode Node(IReadOnlyList<ReadNode> nodes, string testId)
    {
        return nodes.Single(node => node.TestId == testId);
    }
}
