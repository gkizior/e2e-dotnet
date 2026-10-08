// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Urls;

public sealed class ResolveNavigationUrlTests
{
    [Fact]
    public void Resolves_relative_paths_against_the_base()
    {
        Assert.Equal("http://localhost:3000/billing", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "/billing"));
        Assert.Equal("http://localhost:3000/app/settings", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "settings"));
    }

    [Fact]
    public void Admits_any_http_s_origin_a_click_reaches_one_just_as_well()
    {
        Assert.Equal("https://other.test/x", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "https://other.test/x"));
    }

    [Fact]
    public void Always_denies_file_data_and_javascript()
    {
        Assert.Contains("scheme", Assert.Throws<TestException>(() => E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "file:///etc/passwd")).Message, StringComparison.Ordinal);
        Assert.Contains("scheme", Assert.Throws<TestException>(() => E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "data:text/html,x")).Message, StringComparison.Ordinal);
        Assert.Contains("scheme", Assert.Throws<TestException>(() => E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "javascript:alert(1)")).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("view-source:file:///etc/passwd", "view-source:")]
    [InlineData("view-source:http://localhost:3000/", "view-source:")]
    [InlineData("VIEW-SOURCE:file:///etc/passwd", "view-source:")]
    [InlineData("  view-source:file:///etc/passwd", "view-source:")]
    [InlineData("\tview-source:file:///etc/passwd\n", "view-source:")]
    [InlineData("FiLe:///etc/passwd", "file:")]
    [InlineData(" file:///etc/passwd", "file:")]
    [InlineData("blob:http://localhost:3000/0b7c4c1e", "blob:")]
    [InlineData("filesystem:http://localhost:3000/temporary/x", "filesystem:")]
    [InlineData("chrome://version", "chrome:")]
    [InlineData("chrome-extension://abcdefghijklmnop/page.html", "chrome-extension:")]
    [InlineData("devtools://devtools/bundled/inspector.html", "devtools:")]
    [InlineData("about:blank#x", "about:")]
    [InlineData("about:blank?x", "about:")]
    [InlineData("about:srcdoc", "about:")]
    [InlineData("about:version", "about:")]
    [InlineData("ftp://example.test/x", "ftp:")]
    [InlineData("ws://localhost:3000/socket", "ws:")]
    [InlineData("myapp://orders/42", "myapp:")]
    public void Denies_only_http_s_is_navigable(string url, string scheme)
    {
        var error = Assert.Throws<TestException>(() => E2E.Internal.Routes.Resolve("http://localhost:3000/app/", url));
        Assert.Equal("POLICY_DENIED", error.Code);
        Assert.Equal("forbidden URL scheme: " + scheme, error.Message);
        Assert.Equal("POLICY_DENIED", Assert.Throws<TestException>(() => E2E.Internal.Routes.Resolve(null, url)).Code);
    }

    [Fact]
    public void Reads_a_percent_encoded_scheme_as_a_path_not_a_scheme()
    {
        // System.Uri decodes an unreserved %66 to f where WHATWG keeps it; the result is still an http path.
        Assert.Equal("http://localhost:3000/app/view-source%3Afile:///etc/passwd", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "view-source%3Afile:///etc/passwd"));
        Assert.Equal("http://localhost:3000/app/file:///etc/passwd", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "%66ile:///etc/passwd"));
    }

    [Fact]
    public void Admits_exactly_about_blank_which_loads_nothing()
    {
        Assert.Equal("about:blank", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "about:blank"));
        Assert.Equal("about:blank", E2E.Internal.Routes.Resolve(null, " ABOUT:blank "));
    }

    [Fact]
    public void Admits_http_s_in_any_case_and_with_surrounding_whitespace()
    {
        Assert.Equal("https://other.test/x", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "HTTPS://Other.test/x"));
        Assert.Equal("http://localhost:3000/a", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "  http://localhost:3000/a "));
        Assert.Equal("http://other.test/x", E2E.Internal.Routes.Resolve("http://localhost:3000/app/", "//other.test/x"));
    }
}
