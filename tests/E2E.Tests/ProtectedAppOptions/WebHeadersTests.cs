// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

/// <summary>
/// The <c>Headers</c> option is checked when the engine is built. Not ported: "rejects headers that are not a plain
/// object, null included", and the "must be a string" part of the value check. A dictionary of strings cannot hold either.
/// </summary>
public sealed class WebHeadersTests
{
    [Fact]
    public void Rejects_a_header_name_outside_the_token_grammar_as_INVALID_CONFIG()
    {
        foreach (var name in new[] { "x bypass", "x:bypass", "", "x\nbypass" })
        {
            var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { Headers = new Dictionary<string, string> { [name] = "v" } }));
            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Contains("invalid header name", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Rejects_a_value_that_is_not_a_string_or_carries_a_control_character_tab_excepted()
    {
        foreach (var value in new[] { "v\r\nx-b: injected", "token\u0000suffix", "\u007f" })
        {
            var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { Headers = new Dictionary<string, string> { ["x-a"] = value } }));
            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Contains("control character", error.Message, StringComparison.Ordinal);
        }

        _ = new WebEngine(new WebEngineOptions { Headers = new Dictionary<string, string> { ["x-a"] = "a\tb" } });
    }
}
