// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests.Visibility;

[Collection(BrowserCollection.Name)]
public sealed class AFramesDocumentTests
{
    [Fact]
    public async Task Places_its_boxes_past_the_frames_border_and_padding_where_Playwright_measures_them()
    {
        var (node, expected) = await VisibilitySupport.WithSessionAsync(
            """<iframe style="margin:13px;border:7px solid;padding:5px;width:200px;height:100px" srcdoc="<button>Framed</button>"></iframe>""",
            async session =>
            {
                var observed = VisibilitySupport.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).First(candidate => candidate.Name == "Framed");
                var box = await WebEngine.SurfaceOf(session)!.Page().FrameLocator("iframe").GetByRole(AriaRole.Button).BoundingBoxAsync();
                return (observed, box!);
            });
        Assert.Equal(expected.X, node.Rect!.X, 3);
        Assert.Equal(expected.Y, node.Rect.Y, 3);
        Assert.Equal(expected.Width, node.Rect.Width, 3);
        Assert.Equal(expected.Height, node.Rect.Height, 3);
    }
}
