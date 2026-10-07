// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ProtectedApp;

[Collection(BrowserCollection.Name)]
public sealed class WebHeadersBasicAuthTests
{
    [Fact]
    public async Task Runs_the_page_in_the_configured_locale_and_time_zone_after_a_context_reset_too()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, "<!DOCTYPE html><html><body></body></html>"));
        await using var session = (IBrowserSession)await WebSemanticsTests.OpenAsync(site.Url, new WebEngineOptions
        {
            Headless = true,
            Locale = "de-DE",
            TimezoneId = "Asia/Tokyo",
        });
        const string Read = "() => [navigator.language, Intl.DateTimeFormat().resolvedOptions().timeZone, new Date('2026-01-01T00:00:00Z').getTimezoneOffset()].join(' ')";
        const string Expected = "de-DE Asia/Tokyo -540";

        Assert.Equal(Expected, (await session.EvaluateAsync(Read, null, false, CancellationToken.None))?.GetString());
        await session.ClearStateAsync(CancellationToken.None);
        await session.OpenAsync(site.Url, CancellationToken.None);
        Assert.Equal(Expected, (await session.EvaluateAsync(Read, null, false, CancellationToken.None))?.GetString());
    }
}
