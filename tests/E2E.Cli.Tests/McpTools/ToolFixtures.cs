// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;
using E2E.Cli.Mcp;
using ModelContextProtocol.Protocol;

namespace E2E.Cli.Tests.McpTools;

/// <summary>The tools and helpers upstream's <c>mcp-tools.test.ts</c> declares at the top of the file.</summary>
internal static class ToolFixtures
{
    /// <summary>A request nobody cancels.</summary>
    public static readonly CancellationToken Extra = CancellationToken.None;

    /// <summary>
    /// A grammar-shaped tool, its schema as the AI SDK renders
    /// <c>z.object({ target: z.string().min(1).describe('Node id'), times: z.number().int().optional() })</c>.
    /// </summary>
    public static readonly CatalogTool Tap = new()
    {
        Description = "Tap or click one node. The result waits for the effect and reports what changed.",
        InputSchema = Json(
            """
            {
              "$schema": "http://json-schema.org/draft-07/schema#",
              "type": "object",
              "properties": {
                "target": { "type": "string", "minLength": 1, "description": "Node id" },
                "times": { "type": "integer" }
              },
              "required": ["target"],
              "additionalProperties": false
            }
            """),
        Execute = (input, _) => Task.FromResult<object?>("Tapped #" + input.GetProperty("target").GetString() + "."),
    };

    /// <summary>The schema of a tool without arguments: <c>z.object({})</c>.</summary>
    public static readonly JsonElement NoArguments = Json("""{ "type": "object", "properties": {}, "additionalProperties": false }""");

    public static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Each content part as its kind and value: <c>("text", text)</c>, or <c>("image:&lt;type&gt;", base64)</c>.</summary>
    public static (string Kind, string Value)[] Parts(CallToolResult result)
    {
        return result.Content.Select(part => part switch
        {
            TextContentBlock text => ("text", text.Text),
            ImageContentBlock image => ("image:" + image.MimeType, Encoding.UTF8.GetString(image.Data.Span)),
            _ => (part.Type, ""),
        }).ToArray();
    }
}
