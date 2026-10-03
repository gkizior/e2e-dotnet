// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Tests;

public sealed class RoutesTests
{
    [Theory]
    [InlineData("http://h/app/", "/x", "http://h/x")]
    [InlineData("http://h/app/", "x", "http://h/app/x")]
    [InlineData("http://h/app", "x", "http://h/x")]
    [InlineData("http://h/app/", "../x", "http://h/x")]
    [InlineData("http://h/app/", "?q=1", "http://h/app/?q=1")]
    [InlineData("http://h/app/", "//other/x", "http://other/x")]
    [InlineData("http://h/app/", "https://other/x", "https://other/x")]
    [InlineData("http://h/app/", "", "http://h/app/")]
    [InlineData("http://h/app/", null, "http://h/app/")]
    [InlineData(null, "https://other/x", "https://other/x")]
    public void Resolve_follows_url_resolution(string? baseUrl, string? url, string expected)
    {
        Assert.Equal(expected, Routes.Resolve(baseUrl, url));
    }

    [Theory]
    [InlineData("/x")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(null)]
    public void A_relative_url_without_a_base_url_needs_an_app_url(string? url)
    {
        var error = Assert.Throws<TestException>(() => Routes.Resolve(null, url));
        Assert.Equal("APP_URL_REQUIRED", error.Code);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("data:text/html,hi")]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    public void Forbidden_schemes_are_denied(string url)
    {
        Assert.Equal("POLICY_DENIED", Assert.Throws<TestException>(() => Routes.Resolve("http://h/", url)).Code);
        Assert.Equal("POLICY_DENIED", Assert.Throws<TestException>(() => Routes.Resolve(null, url)).Code);
    }
}
