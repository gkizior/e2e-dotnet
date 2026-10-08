// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;
using static E2E.Cli.Tests.McpTools.ToolFixtures;

namespace E2E.Cli.Tests.McpTools;

public sealed class CatalogLineTests
{
    [Fact]
    public void Shows_the_name_the_argument_names_with_optional_ones_marked_and_the_first_sentence()
    {
        Assert.Equal("- tap {target, times?}: Tap or click one node.", Tools.CatalogLine("tap", Tap, false));
    }

    [Fact]
    public void Does_not_end_the_sentence_at_an_abbreviation_and_bounds_a_run_on_sentence()
    {
        var press = new CatalogTool
        {
            Description = "Send one key (e.g. \"Enter\", \"Escape\", \"Tab\") to one node. More text.",
            InputSchema = Json("""{ "type": "object", "properties": { "key": { "type": "string" } }, "required": ["key"], "additionalProperties": false }"""),
            Execute = (_, _) => Task.FromResult<object?>("ok"),
        };
        Assert.Equal("- press {key}: Send one key (e.g. \"Enter\", \"Escape\", \"Tab\") to one node.", Tools.CatalogLine("press", press, false));
        var runOn = new CatalogTool
        {
            Description = string.Concat(Enumerable.Repeat("word ", 60)) + "end",
            InputSchema = NoArguments,
            Execute = (_, _) => Task.FromResult<object?>("ok"),
        };
        var line = Tools.CatalogLine("long", runOn, false);
        Assert.True(line.Length < 200, line);
        Assert.EndsWith("…", line, StringComparison.Ordinal);
    }
}
