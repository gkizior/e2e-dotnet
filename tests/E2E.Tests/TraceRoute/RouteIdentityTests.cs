// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cache;

namespace E2E.Tests.TraceRoute;

/// <summary>
/// Routes: a location reduced to the screen it names, across the URL shapes apps use. The <c>appLocation</c> describe is
/// not ported: the engines report a location without its origin, so the port has no app origin to relate it to.
/// </summary>
public sealed class RouteIdentityTests
{
    [Fact]
    public void Abstracts_the_ids_apps_mint_uuids_hex_and_digit_runs_random_tokens_prefixed_record_ids()
    {
        Assert.True(Alike("/runs/3f2504e0-4f89-11d3-9a0c-0305e82c3301", "/runs/8374a7a7-a64a-422d-9183-4350756c7f07"));
        Assert.True(Alike("/projects/a77c12665e90/tests", "/projects/0c1d2e3f4a5b/tests"));
        Assert.True(Alike("/orders/42", "/orders/7"));
        Assert.True(Alike("/blog/2024/09/18/launch-day", "/blog/2025/01/02/launch-day"));
        Assert.True(Alike("/dashboard/e2e-45961c4c/projects/0c1d2e3f4a5b", "/dashboard/e2e-12345678/projects/a77c12665e90"));
        Assert.True(Alike("/keys/omk_7Qx9Lm2Pz4Rt8Wv1", "/keys/omk_1Ab2Cd3Ef4Gh5Ij"));
        Assert.True(Alike("/projects/PROJ-016", "/projects/PROJ-017"));
        Assert.True(Alike("/invoices/INV-2041", "/invoices/INV-87"));
    }

    [Theory]
    [InlineData("/projects/integrations", "/projects/settings")]
    [InlineData("/api/v2/companies-v2/new", "/api/v3/companies-v2/new")]
    [InlineData("/en-US/settings/general", "/de-DE/settings/general")]
    [InlineData("/Products/Summer-Sneaker", "/Products/Winter-Boot")]
    [InlineData("/users/john_doe", "/users/jane_doe")]
    [InlineData("/news/page-2", "/news/page-3")]
    [InlineData("/tags/c++", "/tags/c")]
    [InlineData("/shop/%E9%9E%8B", "/shop/x")]
    [InlineData("/projects/new", "/projects/a77c12665e90")]
    [InlineData("/projects", "/projects/a77c12665e90")]
    public void Keeps_the_words_a_router_owns_versions_locales_capitalized_and_dashed_slugs(string location, string other)
    {
        Assert.True(Alike(location, location));
        Assert.False(Alike(location, other));
        Assert.True(Alike("/shop/%E9%9E%8B", "/shop/鞋"));
    }

    [Fact]
    public void Ignores_the_fragment_and_a_trailing_slash_and_keeps_the_origin_a_location_names()
    {
        Assert.True(Alike("/companies", "/companies/", "/companies#top"));
        Assert.True(Alike("/settings", "/settings?"));
        Assert.True(Alike("https://app.example.test/companies?x=1", "https://app.example.test/companies?x=2"));
        Assert.False(Alike("https://app.example.test/companies", "/companies"));
        Assert.False(Alike("http://127.0.0.1:4400/shop", "http://localhost:4400/shop"));
    }

    [Fact]
    public void Keeps_the_query_as_part_of_the_screen_with_minted_values_as_ids_in_any_order()
    {
        Assert.False(Alike("/task?mode=unsafe", "/task?mode=safe"));
        Assert.True(Alike("/companies?search=E2E+abc&page=2", "/companies?page=3&search=other+term"));
        Assert.True(Alike("/companies?tab=notes&sort=name", "/companies?sort=name&tab=notes"));
        Assert.False(Alike("/companies?tab=notes", "/companies?tab=files"));
        Assert.False(Alike("/companies", "/companies?tab=notes"));

        // Ids, cache busters, signed tokens, timestamps, and dates are the run's, not the screen's.
        Assert.True(Alike(
            "/list?id=42&_=1727780000&token=eyJhbGciOiJIUzI1NiJ9.x1.y2&at=2026-10-01T10:00:00Z",
            "/list?id=7&_=1727780999&token=eyJhbGciOiJIUzI1NiJ9.a9.b8&at=2026-10-02T11:30:00Z"));
        Assert.True(Alike("/r?8f14e45fceea167a", "/r?c9f0f895fb98ab91"));
        Assert.True(Alike("/tickets?open=INV-2041", "/tickets?open=INV-87"));
        Assert.True(Alike("/reports?from=2026-10-01&view=week", "/reports?from=2026-10-02&view=week"));
        Assert.False(Alike("/reports?from=2026-10-01&view=week", "/reports?from=2026-10-01&view=month"));
    }

    [Fact]
    public void Routes_by_the_fragment_when_the_app_does_its_query_included()
    {
        Assert.True(Alike("/#/companies/8374a7a7-a64a-422d-9183-4350756c7f07", "/companies/1"));
        Assert.True(Alike("/app/#/settings?tab=2", "/settings?tab=3"));
        Assert.True(Alike("/app/?lang=en#/settings?tab=billing", "/settings?lang=en&tab=billing"));
        Assert.True(Alike("https://app.example.test/#/orders/42", "https://app.example.test/orders/7"));
    }

    [Fact]
    public void Treats_a_segment_with_whitespace_as_a_record_name_and_a_literal_plus_as_a_literal()
    {
        Assert.True(Alike("/companies/Acme%20Corp", "/companies/Acme Corp", "/companies/42"));
        Assert.False(Alike("/companies/Acme+Corp", "/companies/42"));
    }

    [Fact]
    public void Keeps_an_encoded_slash_inside_a_segment_from_becoming_a_segment()
    {
        Assert.False(Alike("/a%2Fb/c", "/a/b/c"));
    }

    [Theory]
    [InlineData("Settings")]
    [InlineData("General > About")]
    [InlineData("about:blank")]
    [InlineData("chrome-error://chromewebdata/")]
    public void Keeps_a_location_that_is_not_a_web_address_as_itself_a_device_screen_title_a_browser_page(string location)
    {
        Assert.True(Alike(location, location));
        Assert.False(Alike("Settings", "General"));
        Assert.False(Alike("Settings", "/Settings"));
    }

    /// <summary>Whether every location names the same screen as the first.</summary>
    private static bool Alike(string first, params string[] others) => others.All(other => CacheRoute.Same(first, other));
}
