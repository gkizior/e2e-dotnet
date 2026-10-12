// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests.Links;

public sealed class LinksTests
{
    [Fact]
    public void Parses_custom_schemes_and_web_links_keeping_the_query_the_app_reads()
    {
        Assert.Equal("myapp://orders/42?ref=mail", DeviceLinks.LinkTarget("myapp://orders/42?ref=mail").AbsoluteUri);
        Assert.Equal("https://example.com/verify", DeviceLinks.LinkTarget("HTTPS://Example.com/verify").AbsoluteUri);
    }

    [Fact]
    public void Refuses_schemes_that_read_local_content_or_wrap_a_URL_that_does_in_any_case_or_padding()
    {
        string[] denied =
        [
            "file:///etc/passwd",
            "data:text/html,hi",
            "javascript:alert(1)",
            "view-source:file:///etc/passwd",
            "VIEW-SOURCE:file:///etc/passwd",
            " view-source:https://example.com/",
            "view-\tsource:file:///etc/passwd",
            "blob:https://example.com/0b7c4c1e",
            "filesystem:https://example.com/temporary/x",
        ];
        foreach (var link in denied)
        {
            Assert.Equal("POLICY_DENIED", Assert.Throws<ConfigurationException>(() => DeviceLinks.LinkTarget(link)).Code);
            var unpadded = string.Concat(link.Where(character => !char.IsWhiteSpace(character)));
            Assert.Equal("POLICY_DENIED", Assert.Throws<ConfigurationException>(() => DeviceLinks.AssertAppId(unpadded)).Code);
        }
    }

    [Fact]
    public void Lets_an_app_id_through_to_openApp_and_refuses_a_link_forbidden_schemes_first()
    {
        foreach (var app in new[] { "Settings", "com.apple.Preferences", "com.android.settings", "Notes: Pro", " Clock " })
        {
            DeviceLinks.AssertAppId(app);
        }

        foreach (var denied in new[] { "file:///etc/passwd", "data:text/html,hi", "javascript:alert(1)", "FILE:x", " data:,x " })
        {
            Assert.Equal("POLICY_DENIED", Assert.Throws<ConfigurationException>(() => DeviceLinks.AssertAppId(denied)).Code);
        }

        foreach (var link in new[] { "myapp://orders/42", "https://example.com/verify?token=s3cret", "myapp:orders" })
        {
            var refused = Assert.Throws<TestException>(() => DeviceLinks.AssertAppId(link));
            Assert.Equal("INVALID_ARGUMENT", refused.Code);
            Assert.DoesNotContain("s3cret", refused.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Refuses_a_malformed_link_without_echoing_it_wherever_a_token_may_sit()
    {
        string[] malformed =
        [
            "not-a-url?token=s3cret",
            "https://exa mple.com/?token=s3cret",
            "https://user:s3cret@exa mple.com/verify",
            "https://exa mple.com/token=s3cret",
            "://x?token=s3cret",
        ];
        foreach (var link in malformed)
        {
            var refused = Assert.Throws<TestException>(() => DeviceLinks.LinkTarget(link));
            Assert.Equal("INVALID_ARGUMENT", refused.Code);
            Assert.Contains("openLink needs an absolute URL", refused.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("s3cret", refused.Message, StringComparison.Ordinal);
        }
    }
}
