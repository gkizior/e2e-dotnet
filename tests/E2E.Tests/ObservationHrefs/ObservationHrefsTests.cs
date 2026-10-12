// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ObservationHrefs;

[Collection(BrowserCollection.Name)]
public sealed class ObservationHrefsTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Reports_link_targets_as_origin_and_whole_path_marking_a_dropped_query_fragment_or_opaque_payload()
    {
        const string project = "/dashboard/e2e-dc3cf837/projects/404b2532-9fab-44dc-b3d0-f0a1b2c3d4e5/runs/7d0c3b52-2f7e-4c55-9a51-6f1e0c9b8a77";
        var export = string.Concat(Enumerable.Repeat("id,name%0A1,Ada%0A", 200));
        await chromium.Page.SetContentAsync($"""
            <base href="http://app.test/dashboard/">
            <a href="{project}">Project</a>
            <a href="https://user:pass@docs.example.test/guides/setup">Docs</a>
            <a href="search?q=token&amp;page=2">Search</a>
            <a href="settings#billing">Billing</a>
            <a href="mailto:team@example.test?subject=hi">Mail</a>
            <a href="tel:+48123456789">Call</a>
            <a href="data:text/csv,{export}">Export</a>
            <a href="javascript:void(document.body.dataset.clicked = 'yes')">Script</a>
            <a href="blob:http://app.test/0b7c4c1e-8d9a-4f2e-9c1b-2a3d4e5f6a7b">Download</a>
            <a href="blob:https://user:pass@files.example.test/0b7c4c1e?token=abc">Signed</a>
            <a href="blob:null/0b7c4c1e">Opaque</a>
            """);

        var hrefs = (await chromium.CaptureAsync()).Where(node => node.Role == "link").ToDictionary(node => node.Name!, node => node.Attributes["href"]);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["Project"] = "http://app.test" + project,
                ["Docs"] = "https://docs.example.test/guides/setup",
                ["Search"] = "http://app.test/dashboard/search?…",
                ["Billing"] = "http://app.test/dashboard/settings#…",
                ["Mail"] = "mailto:team@example.test?…",
                ["Call"] = "tel:+48123456789",
                ["Export"] = "data:…",
                ["Script"] = "javascript:…",
                ["Download"] = "blob:http://app.test/0b7c4c1e-8d9a-4f2e-9c1b-2a3d4e5f6a7b",
                ["Signed"] = "blob:https://files.example.test/0b7c4c1e?…",
                ["Opaque"] = "blob:…",
            },
            hrefs);
    }
}
