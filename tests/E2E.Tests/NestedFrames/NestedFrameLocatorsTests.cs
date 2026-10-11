// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests.NestedFrames;

/// <summary>
/// A located node's box is in the top-level viewport, as the contract asks. The port reads nested frames
/// through the observed tree, so it has no <c>frameLocator</c> chain to resolve.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class NestedFrameLocatorsTests
{
    private const string DeepDocument = "<button>Deep button</button>";

    private static readonly string InnerDocument =
        "<button onclick=\"this.textContent = 'Inner clicked'\">Inner button</button>" +
        $"<iframe id=\"deep\" title=\"deep\" srcdoc=\"{Attribute(DeepDocument)}\"></iframe>";

    private static readonly string OuterDocument =
        $"<h2>Outer</h2><iframe id=\"inner\" title=\"inner\" style=\"border:7px solid;padding:5px\" srcdoc=\"{Attribute(InnerDocument)}\"></iframe>";

    private static readonly string HostDocument =
        $"<h1>Host</h1><iframe id=\"outer\" title=\"outer\" srcdoc=\"{Attribute(OuterDocument)}\"></iframe>";

    [Fact]
    public async Task Reports_an_observed_nodes_box_in_the_top_level_viewport_past_each_frames_border_and_padding()
    {
        await using var session = await WebSemanticsTests.OpenAsync("about:blank", new WebEngineOptions { Headless = true });
        var page = WebEngine.SurfaceOf(session)!.Page();
        await page.SetContentAsync(HostDocument);
        var innerFrame = page.FrameLocator("#outer").FrameLocator("#inner");
        await innerFrame.FrameLocator("#deep").GetByRole(AriaRole.Button, new() { Name = "Deep button" }).WaitForAsync();

        var node = WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).First(candidate => candidate.Name == "Inner button");

        // Playwright's own box for the same element, measured through both frames' borders and padding.
        var expected = (await innerFrame.GetByRole(AriaRole.Button).BoundingBoxAsync())!;
        Assert.Equal(expected.X, node.Rect!.X, 3);
        Assert.Equal(expected.Y, node.Rect.Y, 3);
        Assert.Equal(expected.Width, node.Rect.Width, 3);
        Assert.Equal(expected.Height, node.Rect.Height, 3);
    }

    /// <summary>Escapes HTML for a double-quoted attribute value, so a document can carry a nested <c>srcdoc</c>.</summary>
    private static string Attribute(string html) => html.Replace("&", "&amp;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal);
}
