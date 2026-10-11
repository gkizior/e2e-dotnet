// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

/// <summary>
/// The <c>BasicAuth</c> option is checked when the engine is built. Not ported: "declares a secrets.get() password as
/// the engine secret it resolves per attempt" (the port has no engine secrets; COMPATIBILITY.md), "rejects credentials
/// that are not a plain object", the non-string password cases of the username check, and the check that a plain
/// password declares no secret. The options are typed.
/// </summary>
public sealed class WebBasicAuthTests
{
    [Fact]
    public void Accepts_a_username_and_password()
    {
        _ = new WebEngine(new WebEngineOptions { BasicAuth = new WebBasicAuth("ada", "") });
        _ = new WebEngine(new WebEngineOptions { BasicAuth = new WebBasicAuth("ada", "plain") });
    }

    [Fact]
    public void Rejects_a_missing_or_colon_bearing_username_and_a_non_string_password()
    {
        var missing = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { BasicAuth = new WebBasicAuth("", "x") }));
        Assert.Equal("INVALID_CONFIG", missing.Code);
        Assert.Contains("non-empty username", missing.Message, StringComparison.Ordinal);

        var colon = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { BasicAuth = new WebBasicAuth("ada:x", "x") }));
        Assert.Equal("INVALID_CONFIG", colon.Code);
        Assert.Contains("\":\"", colon.Message, StringComparison.Ordinal);
    }
}
