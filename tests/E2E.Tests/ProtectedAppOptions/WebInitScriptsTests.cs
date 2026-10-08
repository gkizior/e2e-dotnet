// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

/// <summary>The <c>InitScripts</c> option is checked when the engine is built, so a script that cannot run fails before a browser launches.</summary>
public sealed class WebInitScriptsTests
{
    [Fact]
    public void Rejects_anything_else_naming_the_entry()
    {
        var cases = new (WebInitScript?[] Scripts, string Message)[]
        {
            (["ok", null!], @"^initScripts\[1\] must be a string of source.*got null"),
            ([WebInitScript.FromPath("")], @"^initScripts\[0\] path must be a non-empty string"),
            // Upstream's sparse-array hole is a null entry in C#.
            (["ok", null!, "ok"], @"^initScripts\[1\] must be a string of source.*got null"),
        };
        foreach (var (scripts, message) in cases)
        {
            var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { InitScripts = scripts! }));
            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Matches(message, error.Message);
        }
    }
}
