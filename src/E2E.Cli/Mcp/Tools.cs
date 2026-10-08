// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace E2E.Cli.Mcp;

/// <summary>What the server registers: the contract and the body of one of its fixed tools.</summary>
internal sealed class McpToolSpec
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>The closed JSON Schema the server checks the arguments against before <see cref="Call"/> runs.</summary>
    public required JsonElement InputSchema { get; init; }

    /// <summary>Hint for clients: a tool that changes nothing on the app or disk.</summary>
    public required bool ReadOnly { get; init; }

    /// <summary>Runs the tool on validated arguments; a thrown error becomes an error result.</summary>
    public required Func<JsonElement, CancellationToken, Task<CallToolResult>> Call { get; init; }
}

/// <summary>
/// One tool of a session's catalog, behind the MCP <c>call</c> tool: a description, the JSON Schema of its
/// arguments, a body, and an optional renderer for the model. The .NET stand-in for an AI SDK tool.
/// </summary>
internal sealed class CatalogTool
{
    public string? Description { get; init; }

    public required JsonElement InputSchema { get; init; }

    /// <summary>Runs the tool on validated arguments and returns its output; null for a tool that cannot run.</summary>
    public Func<JsonElement, CancellationToken, Task<object?>>? Execute { get; init; }

    /// <summary>Renders the output for a model, given the call's arguments and the output.</summary>
    public Func<JsonElement?, object?, CancellationToken, Task<ToolModelOutput>>? ToModelOutput { get; init; }
}

/// <summary>What a catalog tool's <see cref="CatalogTool.ToModelOutput"/> renders.</summary>
internal abstract record ToolModelOutput
{
    /// <summary>Text, or an error's text when <paramref name="IsError"/> is set.</summary>
    public sealed record Text(string Value, bool IsError = false) : ToolModelOutput;

    /// <summary>A value serialized as JSON, or an error's value when <paramref name="IsError"/> is set.</summary>
    public sealed record Json(object? Value, bool IsError = false) : ToolModelOutput;

    /// <summary>Text and file parts.</summary>
    public sealed record Content(IReadOnlyList<ToolOutputPart> Parts) : ToolModelOutput;
}

/// <summary>One part of a <see cref="ToolModelOutput.Content"/> output.</summary>
internal abstract record ToolOutputPart
{
    public sealed record Text(string Value) : ToolOutputPart;

    /// <summary>A file as base64 data, such as a PNG screenshot.</summary>
    public sealed record File(string Data, string MediaType) : ToolOutputPart;
}

/// <summary>
/// The bridge from a catalog tool to the MCP <c>call</c> tool. The server keeps its own tool list fixed and
/// small, and serves a session's vocabulary as a catalog: <c>tools</c> renders it from the same descriptions
/// and schemas the testing agent reads, and <c>call</c> validates the arguments against the schema, runs the
/// tool, and renders its output in the MCP result envelope.
/// </summary>
internal static partial class Tools
{
    /// <summary>How much of a description the catalog shows per tool: its first sentence, bounded.</summary>
    private const int CatalogSentenceMax = 160;

    /// <summary>JSON as <c>JSON.stringify(value, null, 2)</c> writes it.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static CallToolResult TextResult(string text)
    {
        return new CallToolResult { Content = [new TextContentBlock { Text = text }] };
    }

    /// <summary>
    /// A failure as a tool result the agent can react to, never a protocol error: the code and message are
    /// what a test would have reported.
    /// </summary>
    public static CallToolResult ErrorResult(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return new CallToolResult { Content = [new TextContentBlock { Text = CodedMessage(cause) }], IsError = true };
    }

    /// <summary>The message, after the code of an e2e error.</summary>
    public static string CodedMessage(Exception cause)
    {
        ArgumentNullException.ThrowIfNull(cause);
        return cause is E2EException coded ? coded.Code + ": " + coded.Message : cause.Message;
    }

    /// <summary>The JSON Schema of a tool's arguments, without the draft marker clients never need.</summary>
    public static JsonObject ToolJsonSchema(CatalogTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.InputSchema.ValueKind != JsonValueKind.Object)
        {
            return new JsonObject { ["type"] = "object" };
        }

        var schema = JsonNode.Parse(tool.InputSchema.GetRawText())!.AsObject();
        schema.Remove("$schema");
        return schema;
    }

    /// <summary>
    /// One catalog line: the name, the argument names (<c>?</c> marks an optional one), the first sentence of
    /// the description, and the read-only mark. <c>- tap {target}: Tap or click one node.</c>
    /// </summary>
    public static string CatalogLine(string name, CatalogTool tool, bool readOnly)
    {
        var schema = ToolJsonSchema(tool);
        var required = (schema["required"] as JsonArray)?.Select(key => key?.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        var args = (schema["properties"] as JsonObject)?.Select(property => required.Contains(property.Key) ? property.Key : property.Key + "?").ToList() ?? [];
        var signature = args.Count == 0 ? name : name + " {" + string.Join(", ", args) + "}";
        return "- " + signature + ": " + FirstSentence(ToolDescription(name, tool)) + (readOnly ? " [read-only]" : "");
    }

    /// <summary>The full contract of one tool: its description and the JSON Schema of its arguments.</summary>
    public static string DescribeToolDetail(string name, CatalogTool tool, bool readOnly)
    {
        return string.Join(
            '\n',
            name + (readOnly ? " [read-only]" : ""),
            ToolDescription(name, tool),
            "",
            "Arguments (JSON Schema):",
            ToolJsonSchema(tool).ToJsonString(Json));
    }

    /// <summary>The first sentence of <paramref name="text"/>, cut at a word near 160 characters.</summary>
    public static string FirstSentence(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var flat = Whitespace().Replace(text, " ").Trim();
        var guarded = Abbreviations().Replace(flat, match => match.Value.Replace('.', '\0'));
        var end = SentenceEnd().Match(guarded);
        var sentence = (end.Success ? guarded[..(end.Index + 1)] : guarded).Replace('\0', '.');
        if (sentence.Length <= CatalogSentenceMax)
        {
            return sentence;
        }

        var cut = sentence.LastIndexOf(' ', CatalogSentenceMax - 1);
        var length = cut > 0 ? cut : CatalogSentenceMax - 1;

        // Never split a surrogate pair: an emoji at the cut would become an invalid character.
        if (char.IsHighSurrogate(sentence[length - 1]))
        {
            length--;
        }

        return TrailingPunctuation().Replace(sentence[..length], "") + "…";
    }

    /// <summary>
    /// Validates the arguments against the tool's own schema and runs it. The server's <c>call</c> tool takes
    /// any object, so this is where a wrong argument is caught, before the tool runs.
    /// </summary>
    public static async Task<CallToolResult> InvokeToolAsync(string name, CatalogTool tool, JsonElement args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.Execute is null)
        {
            throw new ConfigurationException(EngineErrorCodes.UnsupportedCapability, "tool \"" + name + "\" has no execute function");
        }

        var issues = ArgumentSchema.Validate(tool.InputSchema, args);
        if (issues.Count > 0)
        {
            throw new ConfigurationException(
                "INVALID_ARGUMENT",
                "call " + name + ": " + string.Join("; ", issues.Select(issue => issue.ToString())) + "; tools {tool: " + JsonSerializer.Serialize(name, Json) + "} shows its arguments");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw new E2EException(EngineErrorCodes.Cancelled, "the tool call was cancelled");
        }

        return await ResultFromOutputAsync(tool, await tool.Execute(args, cancellationToken).ConfigureAwait(false), args, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders a tool's output the way the tool renders it for a model: text as text, and a
    /// <see cref="CatalogTool.ToModelOutput"/> that yields file parts as images. Anything else is JSON, so a
    /// structured output is never lost.
    /// </summary>
    public static async Task<CallToolResult> ResultFromOutputAsync(CatalogTool tool, object? output, JsonElement? input = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (tool.ToModelOutput is not null)
        {
            var rendered = await tool.ToModelOutput(input, output, cancellationToken).ConfigureAwait(false);
            var content = ContentFromModelOutput(rendered);
            if (content is not null)
            {
                return new CallToolResult { Content = content, IsError = rendered is ToolModelOutput.Text { IsError: true } or ToolModelOutput.Json { IsError: true } ? true : null };
            }
        }

        return output switch
        {
            string text => TextResult(text),
            null => TextResult("Done."),
            _ => TextResult(JsonSerializer.Serialize(output, Json)),
        };
    }

    private static List<ContentBlock>? ContentFromModelOutput(ToolModelOutput output)
    {
        switch (output)
        {
            case ToolModelOutput.Text text:
                return [new TextContentBlock { Text = text.Value }];
            case ToolModelOutput.Json json:
                return [new TextContentBlock { Text = JsonSerializer.Serialize(json.Value, Json) }];
            case ToolModelOutput.Content content:
                var parts = new List<ContentBlock>();
                foreach (var part in content.Parts)
                {
                    parts.Add(part switch
                    {
                        ToolOutputPart.Text text => new TextContentBlock { Text = text.Value },
                        ToolOutputPart.File file => new ImageContentBlock { Data = Encoding.UTF8.GetBytes(file.Data), MimeType = file.MediaType },
                        _ => throw new ArgumentOutOfRangeException(nameof(output), part, "unknown output part"),
                    });
                }

                return parts.Count == 0 ? null : parts;
            default:
                return null;
        }
    }

    private static string ToolDescription(string name, CatalogTool tool)
    {
        return string.IsNullOrEmpty(tool.Description) ? name : tool.Description;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>Abbreviations whose period does not end a sentence.</summary>
    [GeneratedRegex(@"\b(?:e\.g|i\.e|etc|vs)\.", RegexOptions.IgnoreCase)]
    private static partial Regex Abbreviations();

    [GeneratedRegex(@"[.!?](?:\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex("[,;:]$")]
    private static partial Regex TrailingPunctuation();
}

/// <summary>One reason a value does not fit its schema.</summary>
/// <param name="Path">The keys and indexes from the root to the value; empty for the root.</param>
/// <param name="Message">What is wrong, in zod's words.</param>
internal sealed record SchemaIssue(IReadOnlyList<string> Path, string Message)
{
    /// <summary><c>path.to.value: message</c>, or the message alone at the root.</summary>
    public override string ToString() => Path.Count == 0 ? Message : string.Join('.', Path) + ": " + Message;
}

/// <summary>
/// A strict check of tool arguments against the JSON Schema subset tool schemas use: unions, constants, objects (closed, or a
/// record of one value schema), strings, numbers, integers, booleans, arrays, and enums. Upstream validates
/// with zod; the issues and their messages are zod's, so an agent reads the same refusal from either.
/// </summary>
internal static class ArgumentSchema
{
    /// <summary>Every issue <paramref name="value"/> has against <paramref name="schema"/>; empty when it fits.</summary>
    public static IReadOnlyList<SchemaIssue> Validate(JsonElement schema, JsonElement value)
    {
        var issues = new List<SchemaIssue>();
        Check(schema, value, [], issues);
        return issues;
    }

    private static void Check(JsonElement schema, JsonElement? value, List<string> path, List<SchemaIssue> issues)
    {
        if (schema.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (schema.TryGetProperty("enum", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            if (value is not { } candidate || !options.EnumerateArray().Any(option => JsonElement.DeepEquals(option, candidate)))
            {
                var values = options.EnumerateArray().Select(Stringify).ToList();
                issues.Add(new SchemaIssue([.. path], values.Count == 1 ? "Invalid input: expected " + values[0] : "Invalid option: expected one of " + string.Join('|', values)));
            }

            return;
        }

        if (schema.TryGetProperty("const", out var constant))
        {
            if (value is not { } candidate || !JsonElement.DeepEquals(constant, candidate))
            {
                issues.Add(new SchemaIssue([.. path], "Invalid input: expected " + Stringify(constant)));
            }

            return;
        }

        // A union, as zod renders a nullable or either-or value: one branch must fit. A nullable value reports
        // its inner type's issues, as zod does; any other union says only that the input is invalid.
        if (Branches(schema) is { } branches)
        {
            if (!branches.Any(branch => Fits(branch, value)))
            {
                var inner = branches.Where(branch => !IsNull(branch)).ToList();
                if (inner.Count == 1)
                {
                    Check(inner[0], value, path, issues);
                }
                else
                {
                    issues.Add(new SchemaIssue([.. path], "Invalid input"));
                }
            }

            return;
        }

        var type = schema.TryGetProperty("type", out var declared) && declared.ValueKind == JsonValueKind.String ? declared.GetString() : null;
        switch (type)
        {
            case "object":
                CheckObject(schema, value, path, issues);
                break;
            case "array":
                CheckArray(schema, value, path, issues);
                break;
            case "string":
                if (Expect(value, JsonValueKind.String, "string", path, issues))
                {
                    CheckSize(schema, value!.Value.GetString()!.Length, "string", "characters", path, issues);
                }

                break;
            case "number" or "integer":
                if (Expect(value, JsonValueKind.Number, "number", path, issues))
                {
                    var number = Number(value!.Value);
                    if (type == "integer" && Math.Floor(number) != number)
                    {
                        issues.Add(new SchemaIssue([.. path], "Invalid input: expected int, received number"));
                        return;
                    }

                    CheckBound(schema, number, path, issues);
                }

                break;
            case "boolean":
                if (value is not { ValueKind: JsonValueKind.True or JsonValueKind.False })
                {
                    issues.Add(new SchemaIssue([.. path], "Invalid input: expected boolean, received " + Received(value)));
                }

                break;
            case "null":
                Expect(value, JsonValueKind.Null, "null", path, issues);
                break;
        }
    }

    /// <summary>The branches of an <c>anyOf</c> or <c>oneOf</c>, or one schema per entry of a <c>type</c> list; null for a plain schema.</summary>
    private static List<JsonElement>? Branches(JsonElement schema)
    {
        if ((schema.TryGetProperty("anyOf", out var union) || schema.TryGetProperty("oneOf", out union)) && union.ValueKind == JsonValueKind.Array)
        {
            return [.. union.EnumerateArray()];
        }

        if (schema.TryGetProperty("type", out var types) && types.ValueKind == JsonValueKind.Array)
        {
            return [.. types.EnumerateArray().Select(type =>
            {
                var branch = JsonNode.Parse(schema.GetRawText())!.AsObject();
                branch["type"] = JsonNode.Parse(type.GetRawText());
                return JsonSerializer.SerializeToElement(branch);
            })];
        }

        return null;
    }

    /// <summary>A JSON number as a double; one too large for a double is infinite rather than an error.</summary>
    private static double Number(JsonElement number)
    {
        return double.Parse(number.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private static bool IsNull(JsonElement schema)
    {
        return schema.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "null";
    }

    private static bool Fits(JsonElement schema, JsonElement? value)
    {
        var issues = new List<SchemaIssue>();
        Check(schema, value, [], issues);
        return issues.Count == 0;
    }

    private static void CheckObject(JsonElement schema, JsonElement? value, List<string> path, List<SchemaIssue> issues)
    {
        var properties = schema.TryGetProperty("properties", out var declared) && declared.ValueKind == JsonValueKind.Object ? declared : (JsonElement?)null;
        var extra = schema.TryGetProperty("additionalProperties", out var additional) ? additional : (JsonElement?)null;
        var record = properties is null && extra is { ValueKind: JsonValueKind.Object };
        if (!Expect(value, JsonValueKind.Object, record ? "record" : "object", path, issues))
        {
            return;
        }

        var required = schema.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Select(name => name.GetString()).ToHashSet(StringComparer.Ordinal)
            : [];
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (properties is { } shape)
        {
            foreach (var property in shape.EnumerateObject())
            {
                known.Add(property.Name);
                var present = value!.Value.TryGetProperty(property.Name, out var child);
                if (present || required.Contains(property.Name))
                {
                    Check(property.Value, present ? child : null, [.. path, property.Name], issues);
                }
            }
        }

        var unknown = value!.Value.EnumerateObject().Where(property => !known.Contains(property.Name)).ToList();
        if (extra is { ValueKind: JsonValueKind.False })
        {
            if (unknown.Count > 0)
            {
                issues.Add(new SchemaIssue([.. path], "Unrecognized key" + (unknown.Count > 1 ? "s" : "") + ": " + string.Join(", ", unknown.Select(property => Quote(property.Name)))));
            }
        }
        else if (extra is { ValueKind: JsonValueKind.Object } valueSchema)
        {
            foreach (var property in unknown)
            {
                Check(valueSchema, property.Value, [.. path, property.Name], issues);
            }
        }
    }

    private static void CheckArray(JsonElement schema, JsonElement? value, List<string> path, List<SchemaIssue> issues)
    {
        if (!Expect(value, JsonValueKind.Array, "array", path, issues))
        {
            return;
        }

        if (schema.TryGetProperty("items", out var items))
        {
            var index = 0;
            foreach (var item in value!.Value.EnumerateArray())
            {
                Check(items, item, [.. path, index.ToString(CultureInfo.InvariantCulture)], issues);
                index++;
            }
        }

        CheckSize(schema, value!.Value.GetArrayLength(), "array", "items", path, issues);
    }

    /// <summary>A length bound: <c>minLength</c> and <c>maxLength</c>, or <c>minItems</c> and <c>maxItems</c>.</summary>
    private static void CheckSize(JsonElement schema, int size, string origin, string unit, List<string> path, List<SchemaIssue> issues)
    {
        var (min, max) = origin == "array" ? ("minItems", "maxItems") : ("minLength", "maxLength");
        if (schema.TryGetProperty(min, out var floor) && size < Number(floor))
        {
            issues.Add(new SchemaIssue([.. path], "Too small: expected " + origin + " to have >=" + floor.GetRawText() + " " + unit));
        }
        else if (schema.TryGetProperty(max, out var ceiling) && size > Number(ceiling))
        {
            issues.Add(new SchemaIssue([.. path], "Too big: expected " + origin + " to have <=" + ceiling.GetRawText() + " " + unit));
        }
    }

    private static void CheckBound(JsonElement schema, double number, List<string> path, List<SchemaIssue> issues)
    {
        if (schema.TryGetProperty("minimum", out var floor) && number < Number(floor))
        {
            issues.Add(new SchemaIssue([.. path], "Too small: expected number to be >=" + floor.GetRawText()));
        }
        else if (schema.TryGetProperty("exclusiveMinimum", out var above) && above.ValueKind == JsonValueKind.Number && number <= Number(above))
        {
            issues.Add(new SchemaIssue([.. path], "Too small: expected number to be >" + above.GetRawText()));
        }
        else if (schema.TryGetProperty("maximum", out var ceiling) && number > Number(ceiling))
        {
            issues.Add(new SchemaIssue([.. path], "Too big: expected number to be <=" + ceiling.GetRawText()));
        }
        else if (schema.TryGetProperty("exclusiveMaximum", out var below) && below.ValueKind == JsonValueKind.Number && number >= Number(below))
        {
            issues.Add(new SchemaIssue([.. path], "Too big: expected number to be <" + below.GetRawText()));
        }
    }

    /// <summary>Records zod's type issue when <paramref name="value"/> is missing or of another kind.</summary>
    private static bool Expect(JsonElement? value, JsonValueKind kind, string expected, List<string> path, List<SchemaIssue> issues)
    {
        if (value?.ValueKind == kind)
        {
            return true;
        }

        issues.Add(new SchemaIssue([.. path], "Invalid input: expected " + expected + ", received " + Received(value)));
        return false;
    }

    /// <summary>The type name zod reports for what it received.</summary>
    private static string Received(JsonElement? value)
    {
        return value?.ValueKind switch
        {
            null or JsonValueKind.Undefined => "undefined",
            JsonValueKind.Null => "null",
            JsonValueKind.Array => "array",
            JsonValueKind.Object => "object",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            _ => "boolean",
        };
    }

    /// <summary>A primitive as zod prints it in a message: a string in double quotes, anything else as JSON.</summary>
    private static string Stringify(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? Quote(value.GetString()!) : value.GetRawText();
    }

    private static string Quote(string text) => "\"" + text + "\"";
}
