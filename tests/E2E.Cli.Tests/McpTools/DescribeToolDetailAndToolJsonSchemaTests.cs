// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;
using static E2E.Cli.Tests.McpTools.ToolFixtures;

namespace E2E.Cli.Tests.McpTools;

public sealed class DescribeToolDetailAndToolJsonSchemaTests
{
    [Fact]
    public void Renders_the_full_description_and_the_JSON_Schema_without_the_draft_marker()
    {
        var detail = Tools.DescribeToolDetail("tap", Tap, false);
        Assert.Contains("Tap or click one node. The result waits for the effect and reports what changed.", detail, StringComparison.Ordinal);
        Assert.Contains("Arguments (JSON Schema):", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("$schema", detail, StringComparison.Ordinal);
        var schema = Tools.ToolJsonSchema(Tap);
        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal(["target"], schema["required"]!.AsArray().Select(key => key!.GetValue<string>()));
        Assert.Equal("string", schema["properties"]!["target"]!["type"]!.GetValue<string>());
        Assert.Equal("Node id", schema["properties"]!["target"]!["description"]!.GetValue<string>());
        Assert.Equal("integer", schema["properties"]!["times"]!["type"]!.GetValue<string>());
    }
}
