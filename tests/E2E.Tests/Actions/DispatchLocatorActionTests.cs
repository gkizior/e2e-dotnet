// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CheckActions.CheckPage;

namespace E2E.Tests.Actions;

/// <summary>
/// Partial ports of upstream "dispatches %j to locator.%s" (clear, selectOption label) and the node swipe: the upstream tests check call arguments and a wheel on stubs. The port checks the
/// effect on a live page, since the page calls are made inside the engine. <c>longPress</c>, the <c>index</c> form of
/// <c>selectOption</c>, <c>dragTo</c>, and the node swipe's missing-box case are not ported: the port has no long press,
/// no index select, no <c>dragTo</c>, and scrolls a node with <c>scrollBy</c>, which needs no box (COMPATIBILITY.md).
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class DispatchLocatorActionTests
{
    [Theory]
    [InlineData("clear")]
    [InlineData("selectOption label")]
    public async Task Changes_the_field_or_the_selected_option_as_the_action_says(string action)
    {
        await RunAsync(
            """
            <input aria-label="Note" value="draft">
            <select aria-label="Color"><option>Red</option><option>Blue</option></select>
            """,
            async session =>
            {
                if (action == "clear")
                {
                    await session.PerformAsync(await FindByNameAsync(session, "Note"), new LocatorAction.Clear(), CancellationToken.None);
                    Assert.Equal("", await EvaluateAsync<string>(session, "() => document.querySelector('input').value"));
                }
                else
                {
                    await session.PerformAsync(await FindByNameAsync(session, "Color"), new LocatorAction.Select("Blue"), CancellationToken.None);
                    Assert.Equal("Blue", await EvaluateAsync<string>(session, "() => document.querySelector('select').value"));
                }
            });
    }

    [Fact]
    public async Task Scrolls_a_node_by_three_quarters_of_its_own_box()
    {
        // Partial: upstream wheels over the node by half its box, with a momentum option; the port calls scrollBy by three quarters.
        await RunAsync(
            """
            <div role="region" aria-label="Box" tabindex="0" style="width:400px;height:300px;overflow:auto">
              <div style="width:2000px;height:2000px"></div>
            </div>
            """,
            async session =>
            {
                var box = await FindByNameAsync(session, "Box");
                async Task<string> OffsetAsync() => await EvaluateAsync<string>(session, "() => document.querySelector('div').scrollLeft + ',' + document.querySelector('div').scrollTop");

                await session.PerformAsync(box, new LocatorAction.Swipe(ScrollDirection.Down), CancellationToken.None);
                Assert.Equal("0,225", await OffsetAsync());

                await session.PerformAsync(box, new LocatorAction.Swipe(ScrollDirection.Right), CancellationToken.None);
                Assert.Equal("300,225", await OffsetAsync());

                await session.PerformAsync(box, new LocatorAction.Swipe(ScrollDirection.Left), CancellationToken.None);
                Assert.Equal("0,225", await OffsetAsync());
            });
    }

    private static async Task<SemanticNode> FindByNameAsync(IEngineSession session, string name)
    {
        var observation = await session.ObserveAsync(CancellationToken.None);
        return Flatten(observation.Roots).First(node => node.Name == name);
    }
}
