// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ClosedShadow;

/// <summary>
/// The closed-root record the init script installs. Not ported: the record's <c>count</c>, and "answers a semantic query
/// with the root alone, without walking, while no closed root was attached" and "lets the mask engine find nothing
/// without walking while no closed root was attached, and refuses an untracked document". The port's page script walks
/// the document itself, so it keeps no count and registers no <c>e2e-roots=</c> or mask selector engines.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ClosedShadowRootRecordTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Counts_the_closed_roots_the_wrapped_attachShadow_records_and_leaves_open_ones_out()
    {
        var recorded = await chromium.Page.EvaluateAsync<string>(
            """
            () => {
              const record = globalThis[Symbol.for("e2e.closedShadowRoots")];
              const openHost = document.createElement("div");
              const closedHost = document.createElement("div");
              const open = openHost.attachShadow({ mode: "open" });
              const closed = closedHost.attachShadow({ mode: "closed" });
              return [record instanceof WeakMap, open.mode, record.has(openHost), record.get(closedHost) === closed].join(",");
            }
            """);
        Assert.Equal("true,open,false,true", recorded);

        // Installed once per document: a second run leaves the record and the wrapper alone.
        var unchanged = await chromium.Page.EvaluateAsync<bool>(
            """
            (script) => {
              const key = Symbol.for("e2e.closedShadowRoots");
              const record = globalThis[key];
              const attachShadow = Element.prototype.attachShadow;
              (0, eval)(script);
              return globalThis[key] === record && Element.prototype.attachShadow === attachShadow;
            }
            """,
            PageScript.RecordClosedShadowRoots);
        Assert.True(unchanged);
    }
}
