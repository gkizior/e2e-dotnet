// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ReadNodeNames;

[Collection(BrowserCollection.Name)]
public sealed class TruncatedFlagTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Reports_a_walk_the_node_budget_stopped_and_a_whole_read_as_not_truncated()
    {
        var buttons = string.Concat(Enumerable.Range(0, 20).Select(index => $"<button>Button {index}</button>"));
        await chromium.Page.SetContentAsync($"<main>{buttons}</main>");

        await chromium.CaptureAsync(100);
        Assert.False(chromium.Truncated);
        Assert.Equal(21, chromium.NodeCount);

        await chromium.CaptureAsync(5);
        Assert.True(chromium.Truncated);
        Assert.Equal(5, chromium.NodeCount);
    }

    [Fact]
    public async Task Reports_a_child_frame_the_budget_could_not_enter()
    {
        // The page lists exactly the budget in nodes: the button, the frame, and the buttons that fill the rest.
        var filler = string.Concat(Enumerable.Repeat("<button>Fill</button>", ObservationLimits.Nodes - 2));
        const string Frame = "<button>One</button><iframe title=\"Frame\" srcdoc=\"<button>Inside</button>\"></iframe>";

        using var whole = await TinySite.StartAsync($"<!DOCTYPE html><html><body>{Frame}</body></html>");
        await using (var session = await WebSemanticsTests.OpenAsync(whole.Url, new WebEngineOptions { Headless = true }))
        {
            var observation = await session.ObserveAsync(CancellationToken.None);
            Assert.False(observation.Truncated);
            Assert.Contains(WebSemanticsTests.Flatten(observation.Roots), node => node.Name == "Inside");
        }

        using var cut = await TinySite.StartAsync($"<!DOCTYPE html><html><body>{Frame}{filler}</body></html>");
        await using (var session = await WebSemanticsTests.OpenAsync(cut.Url, new WebEngineOptions { Headless = true }))
        {
            var observation = await session.ObserveAsync(CancellationToken.None);
            Assert.True(observation.Truncated);
            Assert.DoesNotContain(WebSemanticsTests.Flatten(observation.Roots), node => node.Name == "Inside");
        }
    }
}
