// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;
using static E2E.Cli.Tests.McpTools.ToolFixtures;

namespace E2E.Cli.Tests.McpTools;

public sealed class ResultFromOutputTests
{
    private static readonly CatalogTool Plain = new() { Description = "x", InputSchema = NoArguments };

    [Fact]
    public async Task Passes_text_through_and_serializes_structured_output_as_JSON()
    {
        var text = await Tools.ResultFromOutputAsync(Plain, "Done.");
        var structured = await Tools.ResultFromOutputAsync(Plain, new { ok = true });
        var none = await Tools.ResultFromOutputAsync(Plain, null);

        Assert.Equal([("text", "Done.")], Parts(text));
        Assert.Equal([("text", "{\n  \"ok\": true\n}")], Parts(structured));
        Assert.Equal([("text", "Done.")], Parts(none));
        Assert.All([text, structured, none], result => Assert.Null(result.IsError));
    }

    [Fact]
    public async Task Renders_a_toModelOutput_file_part_as_an_image_the_way_the_device_screenshot_tool_does()
    {
        var screenshot = new CatalogTool
        {
            Description = "x",
            InputSchema = NoArguments,
            ToModelOutput = (_, output, _) => Task.FromResult<ToolModelOutput>(output is Shot { Png: { } png }
                ? new ToolModelOutput.Content([new ToolOutputPart.File(png, "image/png")])
                : new ToolModelOutput.Text("Screenshot withheld: UNSUPPORTED_CAPABILITY")),
        };

        var image = await Tools.ResultFromOutputAsync(screenshot, new Shot("AAAA"));
        var withheld = await Tools.ResultFromOutputAsync(screenshot, new Shot(null));

        Assert.Equal([("image:image/png", "AAAA")], Parts(image));
        Assert.Null(image.IsError);
        Assert.Equal([("text", "Screenshot withheld: UNSUPPORTED_CAPABILITY")], Parts(withheld));
        Assert.Null(withheld.IsError);
    }

    [Fact]
    public async Task Hands_the_call_arguments_to_a_renderer_that_reads_its_input()
    {
        var echo = new CatalogTool
        {
            Description = "x",
            InputSchema = Json("""{ "type": "object", "properties": { "label": { "type": "string" } }, "required": ["label"], "additionalProperties": false }"""),
            ToModelOutput = (input, output, _) => Task.FromResult<ToolModelOutput>(new ToolModelOutput.Text(input!.Value.GetProperty("label").GetString() + ": " + output)),
        };

        var result = await Tools.ResultFromOutputAsync(echo, 42, Json("""{ "label": "answer" }"""));

        Assert.Equal([("text", "answer: 42")], Parts(result));
    }

    [Fact]
    public async Task Renders_string_outputs_with_the_custom_renderer_before_passing_them_through()
    {
        var rendered = new CatalogTool
        {
            Description = "x",
            InputSchema = NoArguments,
            ToModelOutput = async (_, output, _) =>
            {
                await Task.Yield();
                return new ToolModelOutput.Text("formatted: " + output);
            },
        };

        var result = await Tools.ResultFromOutputAsync(rendered, "raw string");

        Assert.Equal([("text", "formatted: raw string")], Parts(result));
    }

    [Fact]
    public async Task Preserves_model_error_outputs_as_MCP_errors()
    {
        var errorText = new CatalogTool
        {
            Description = "x",
            InputSchema = NoArguments,
            ToModelOutput = (_, _, _) => Task.FromResult<ToolModelOutput>(new ToolModelOutput.Text("not available", IsError: true)),
        };
        var errorJson = new CatalogTool
        {
            Description = "x",
            InputSchema = NoArguments,
            ToModelOutput = (_, output, _) => Task.FromResult<ToolModelOutput>(new ToolModelOutput.Json(output, IsError: true)),
        };

        var text = await Tools.ResultFromOutputAsync(errorText, "raw string");
        var json = await Tools.ResultFromOutputAsync(errorJson, new { reason = "not available" });

        Assert.Equal([("text", "not available")], Parts(text));
        Assert.True(text.IsError);
        Assert.Equal([("text", "{\n  \"reason\": \"not available\"\n}")], Parts(json));
        Assert.True(json.IsError);
    }

    /// <summary>A screenshot tool's output: the PNG as base64, or none when it was withheld.</summary>
    private sealed record Shot(string? Png);
}
