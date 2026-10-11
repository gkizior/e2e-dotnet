// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

/// <summary>The <c>UserAgent</c> option is checked when the engine is built. A non-string value cannot be passed to a typed option.</summary>
public sealed class WebUserAgentTests
{
    [Fact]
    public void Rejects_an_empty_or_non_string_value_and_a_control_character()
    {
        var empty = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { UserAgent = "" }));
        Assert.Equal("INVALID_CONFIG", empty.Code);
        Assert.Contains("non-empty string", empty.Message, StringComparison.Ordinal);

        var control = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { UserAgent = "a\r\nx-injected: 1" }));
        Assert.Equal("INVALID_CONFIG", control.Code);
        Assert.Contains("control character", control.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_a_user_agent_header_beside_it_which_would_override_it_on_the_app_site_only()
    {
        var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions
        {
            UserAgent = "playwright",
            Headers = new Dictionary<string, string> { ["User-Agent"] = "other" },
        }));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Contains("conflict", error.Message, StringComparison.Ordinal);
        _ = new WebEngine(new WebEngineOptions { UserAgent = "playwright", Headers = new Dictionary<string, string> { ["x-preview"] = "token" } });
    }
}
