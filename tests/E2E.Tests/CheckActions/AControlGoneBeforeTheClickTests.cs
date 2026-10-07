// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CheckActions.CheckPage;

namespace E2E.Tests.CheckActions;

/// <summary>A control the click never reached stays a stale node. Ported from upstream <c>check-actions.test.ts</c>.</summary>
[Collection(BrowserCollection.Name)]
public sealed class AControlGoneBeforeTheClickTests
{
    [Fact]
    public async Task Stays_a_retryable_stale_node_nothing_was_dispatched()
    {
        await RunAsync(
            """<label><input type="checkbox" id="box">Notify</label>""",
            async session =>
            {
                var box = await FindAsync(session, "checkbox", "Notify");
                await EvaluateAsync<bool>(session, "() => { document.getElementById('box').remove(); return true; }");
                var error = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(box, new LocatorAction.Check(), CancellationToken.None));
                Assert.Equal("NODE_STALE", error.Code);
                Assert.True(error.Retryable);
            });
    }
}
