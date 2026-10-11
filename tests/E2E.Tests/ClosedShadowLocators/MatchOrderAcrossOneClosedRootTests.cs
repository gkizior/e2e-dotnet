// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ClosedShadowLocators;

[Collection(BrowserCollection.Name)]
public sealed class MatchOrderAcrossOneClosedRootTests
{
    [Fact]
    public async Task Lists_a_closed_root_match_in_tree_order_where_its_host_stands()
    {
        using var site = await FixtureApp.StartAsync();
        await using (var engine = await WebSemanticsTests.OpenAsync(site.Url + "closed-order", new WebEngineOptions { Headless = true }))
        {
            // The host comes before the light-DOM button, so the reader reads Alpha first.
            var observed = WebSemanticsTests.Flatten((await engine.ObserveAsync(CancellationToken.None)).Roots).Where(node => node.Role == "button");
            Assert.Equal(["Alpha", "Beta"], observed.Select(node => node.Name));
        }

        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/closed-order");
            // Not ported: upstream's locator searches the light DOM as one root and the closed root as the next,
            // so it answers Beta before Alpha. The port resolves a locator against the observed tree, so it
            // answers in the tree's order, Alpha first.
            var buttons = session.Screen.GetByRole("button");
            Assert.Equal(["Alpha", "Beta"], (await buttons.ResolveAsync(CancellationToken.None)).Select(node => node.Name));
            Assert.Equal(["Alpha"], (await buttons.First().ResolveAsync(CancellationToken.None)).Select(node => node.Name));
        });
    }
}
