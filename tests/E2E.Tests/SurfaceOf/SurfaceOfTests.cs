// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CheckActions.CheckPage;

namespace E2E.Tests.SurfaceOf;

/// <summary>
/// <c>SurfaceOf</c> hands test code the live page and context behind a web session, and nothing for any other engine.
/// Not ported: "refuses the page and the context before an attempt is running". The port has no handle without a
/// session: a session opens its context at start, so only the page refuses, with <c>INVALID_STATE</c> until the first open.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class SurfaceOfTests
{
    [Fact]
    public async Task Returns_a_live_surface_for_a_playwright_handle_and_undefined_for_a_foreign_one()
    {
        await using var web = await StartAsync();
        Assert.NotNull(WebEngine.SurfaceOf(web));

        await using var other = await new DocumentEngine(new DocumentWorld()).StartAsync(new EngineStartOptions(), CancellationToken.None);
        Assert.Null(WebEngine.SurfaceOf(other));
    }
}
