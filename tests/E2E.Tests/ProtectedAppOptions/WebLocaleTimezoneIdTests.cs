// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

public sealed class WebLocaleTimezoneIdTests
{
    [Theory]
    [InlineData("de-DE", "Europe/Berlin")]
    [InlineData("zh-Hant-TW", "UTC")]
    [InlineData("de-DE-u-co-phonebk", "US/Eastern")]
    [InlineData(null, "GMT")]
    public void Accepts_a_language_tag_and_an_IANA_time_zone(string? locale, string timezoneId)
    {
        _ = new WebEngine(new WebEngineOptions { Locale = locale, TimezoneId = timezoneId });
    }

    [Theory]
    [InlineData("", null, "BCP 47")]
    [InlineData("not a locale", null, "BCP 47")]
    [InlineData("de_DE", null, "BCP 47")]
    [InlineData("und", null, "BCP 47")]
    [InlineData("x-private", null, "BCP 47")]
    [InlineData(null, "", "IANA time zone")]
    [InlineData(null, "Mars/Olympus_Mons", "IANA time zone")]
    [InlineData(null, "GMT+25", "IANA time zone")]
    [InlineData(null, "+01:00", "IANA time zone")]
    [InlineData(null, "europe/berlin", "IANA time zone")]
    [InlineData(null, "utc", "IANA time zone")]
    public void Rejects_a_value_that_is_no_language_tag_or_time_zone(string? locale, string? timezoneId, string message)
    {
        var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { Locale = locale, TimezoneId = timezoneId }));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_accept_language_header_beside_locale_which_would_override_it_on_the_app_site_only()
    {
        var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions
        {
            Locale = "de-DE",
            Headers = new Dictionary<string, string> { ["Accept-Language"] = "fr" },
        }));
        Assert.Contains("conflict", error.Message, StringComparison.Ordinal);
        _ = new WebEngine(new WebEngineOptions { Locale = "de-DE", Headers = new Dictionary<string, string> { ["x-preview"] = "token" } });
    }
}
