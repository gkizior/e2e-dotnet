// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.Tests.Tool;

/// <summary>The port has no <c>defineTool</c>: <see cref="AgentTool"/> takes its place.</summary>
public sealed class DefineToolPlatformsTests
{
    private static AgentTool Define(bool mutates, IReadOnlyList<string>? platforms) =>
        new("noop", new JsonObject { ["type"] = "object" }, mutates, (_, _) => Task.FromResult("ok"), platforms);

    [Fact]
    public void Offers_a_scoped_tool_only_on_its_declared_platforms()
    {
        var defined = Define(true, ["ios", "android"]);

        Assert.True(defined.AppliesTo("ios"));
        Assert.False(defined.AppliesTo("web"));
        Assert.Equal(["ios", "android"], defined.Platforms);
    }

    [Fact]
    public void Rejects_an_empty_or_malformed_platforms_list()
    {
        Assert.Contains("platforms", Assert.Throws<TestException>(() => Define(false, [])).Message, StringComparison.Ordinal);
        Assert.Contains("platforms", Assert.Throws<TestException>(() => Define(false, [""])).Message, StringComparison.Ordinal);
    }
}
