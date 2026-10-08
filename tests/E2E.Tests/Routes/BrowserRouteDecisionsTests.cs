// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Routes;

[Collection(BrowserCollection.Name)]
public sealed class BrowserRouteDecisionsTests
{
    [Fact]
    public async Task Sets_a_cookie_with_a_relative_url_on_the_base_URL()
    {
        using var site = await TinySite.StartAsync("<!DOCTYPE html><html><body></body></html>");
        var session = await WebEngineTests.StartAsync(site, new ScriptedModel(_ => ModelResponses.Done("passed", "unused")), cache: null);
        await WebEngineTests.RunAsync(session, async () =>
        {
            var browser = session.Browser;
            await browser.SetCookiesAsync([new BrowserCookie { Name = "flavor", Value = "oatmeal", Url = "/" }]);
            var cookie = Assert.Single(await browser.CookiesAsync());
            Assert.Equal(("flavor", "oatmeal", "127.0.0.1", "/"), (cookie.Name, cookie.Value, cookie.Domain, cookie.Path));
        });
    }
}
