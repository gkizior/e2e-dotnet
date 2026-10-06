// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E;

internal static class AgentTools
{
    private static readonly JsonElement Target = Parse(
        """
        {
          "type": "object",
          "properties": {
            "role": { "type": "string", "description": "Accessible role, such as button or textbox." },
            "name": { "type": "string", "description": "Accessible name from the snapshot." },
            "testId": { "type": "string" },
            "ref": { "type": "string", "description": "ref from the current snapshot. Prefer role and name." }
          }
        }
        """);

    /// <summary>Screens one scroll call may move.</summary>
    public const int MaxScrollTimes = 20;

    /// <summary>Longest text <c>scroll_to</c> pages toward: a row label, never a paragraph.</summary>
    public const int MaxScrollToText = 200;

    private const string Directions = """{ "type": "string", "enum": ["up", "down", "left", "right"] }""";

    /// <summary>Codes the model may pick when it ends an act. The runtime assigns every other code.</summary>
    public static readonly string[] ModelErrorCodes =
    [
        "ACTION_FAILED",
        "AUTOMATION_UNSUPPORTED",
        "ASSERTION_FAILED",
        "AUTHENTICATION_FAILED",
        "AUTH_CREDENTIAL_UNAVAILABLE",
        "AUTH_CREDENTIAL_INVALID",
        "SECRET_UNAVAILABLE",
        "ENVIRONMENT_UNAVAILABLE",
        "SEED_DATA_MISSING",
        "TEST_SETUP_FAILED",
        "APP_UNREACHABLE",
        "APP_NOT_OPEN",
        "POLICY_DENIED",
    ];

    /// <summary>The model codes a blocked verdict accepts: credentials, environment, test setup, or automation.</summary>
    public static readonly string[] BlockableCodes =
    [
        "AUTOMATION_UNSUPPORTED",
        "AUTH_CREDENTIAL_UNAVAILABLE",
        "AUTH_CREDENTIAL_INVALID",
        "SECRET_UNAVAILABLE",
        "ENVIRONMENT_UNAVAILABLE",
        "SEED_DATA_MISSING",
        "TEST_SETUP_FAILED",
        "APP_UNREACHABLE",
        "APP_NOT_OPEN",
        "POLICY_DENIED",
    ];

    private static readonly JsonElement Done = VerdictSchema(["passed", "failed", "blocked"], ModelErrorCodes);

    private static readonly JsonElement JudgeDone = VerdictSchema(["passed", "failed"], ["ASSERTION_FAILED", "ASSERTION_INCONCLUSIVE"]);

    private static readonly ModelTool Observe = Tool(
        "observe",
        "Look at the screen again. Action results already include the screen, so call this only after waiting for something in progress, never right after an action.",
        Parse("""{ "type": "object", "properties": {} }"""));

    private static readonly ModelTool ScrollTo = Tool(
        "scroll_to",
        "Scroll until a node is inside the viewport. With a target alone, one listed control above or below the fold. " +
        "With text, a node the screen does not list yet: the list named by the target (or the viewport) is paged screen by screen " +
        "until a node reading that text shows, as one action however far. Prefer either to blind scrolling.",
        Extend(
            Target,
            $$"""
            {
              "text": { "type": "string", "maxLength": {{MaxScrollToText}}, "description": "The visible text of the node to reach, or a distinctive part of it. With text, the target is the list to page." },
              "direction": {{Directions}}
            }
            """));

    private static readonly ModelTool Scroll = Tool(
        "scroll",
        "Scroll the viewport, or one scrollable control when a target is given, by a screen or a few. " +
        "The result shows the screen after the scroll. For a row far down a long list, prefer scroll_to with the row's text.",
        Extend(
            Target,
            $$"""
            {
              "direction": {{Directions}},
              "times": { "type": "integer", "minimum": 1, "maximum": {{MaxScrollTimes}}, "description": "How many screens to scroll in this one call, 1 to {{MaxScrollTimes}}; default 1." }
            }
            """,
            "direction"));

    private static readonly ModelTool Back = Tool(
        "back",
        "Go back one step in the browser history. The result shows the screen you return to.",
        Parse("""{ "type": "object", "properties": {} }"""));

    private static readonly ModelTool[] Verbs =
    [
        Tool("tap", "Activate a control.", Target),
        Tool("double_tap", "Double-click a control. Use it only when a tap does nothing, such as a label that opens an editor on a double-click.", Target),
        Tool("fill", "Replace the text in a field.", WithValue(Target, "value")),
        Tool("fill_secret", "Type a declared secret into a password or secret field. Pass the secret name, never the value.", WithValue(Target, "secret")),
        Tool("press", "Press a key such as Enter, Escape, or Tab. Omit role and name to press the focused control.", WithValue(Target, "key")),
        Tool("select", "Choose an option in a combobox.", WithValue(Target, "value")),
        Tool("check", "Check a checkbox.", Target),
        Tool("uncheck", "Uncheck a checkbox.", Target),
        Tool("clear", "Clear a field.", Target),
    ];

    private static readonly ModelTool Navigate = Tool("navigate", "Open a path or URL.", Parse("""{ "type": "object", "properties": { "url": { "type": "string" } }, "required": ["url"] }"""));

    /// <summary>The act tools an engine with these capabilities can honor. Scroll and back tools need their capability.</summary>
    public static IReadOnlyList<ModelTool> ActFor(EngineCapabilities capabilities)
    {
        var tools = new List<ModelTool> { Observe };
        tools.AddRange(Verbs);
        if (capabilities.HasFlag(EngineCapabilities.Scroll))
        {
            tools.Add(ScrollTo);
            tools.Add(Scroll);
        }

        tools.Add(Navigate);
        if (capabilities.HasFlag(EngineCapabilities.History))
        {
            tools.Add(Back);
        }

        tools.Add(Tool("done", "End the step with a verdict. blocked requires a code naming what blocked you.", Done));
        return tools;
    }

    public static IReadOnlyList<ModelTool> Judge { get; } =
    [
        Tool("done", "End the judgment. Use ASSERTION_FAILED or ASSERTION_INCONCLUSIVE when status is failed.", JudgeDone),
    ];

    /// <summary>The extract tools, with <c>data</c> described by the JSON schema of the requested type.</summary>
    public static IReadOnlyList<ModelTool> ExtractFor(JsonNode schema)
    {
        var parameters = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject { ["data"] = schema.DeepClone() },
            ["required"] = new JsonArray("data"),
        };
        return
        [
            Tool("extract", "Return the data read from the screen.", JsonSerializer.SerializeToElement(parameters)),
            Tool("done", "Use this only when the data is not on screen.", JudgeDone),
        ];
    }

    private static ModelTool Tool(string name, string description, JsonElement parameters)
    {
        return new ModelTool { Name = name, Description = description, Parameters = parameters };
    }

    private static JsonElement VerdictSchema(string[] statuses, string[] codes)
    {
        return Parse(
            $$"""
            {
              "type": "object",
              "properties": {
                "status": { "type": "string", "enum": {{JsonSerializer.Serialize(statuses)}} },
                "summary": { "type": "string" },
                "code": { "type": "string", "enum": {{JsonSerializer.Serialize(codes)}} }
              },
              "required": ["status", "summary"]
            }
            """);
    }

    private static JsonElement WithValue(JsonElement target, string property)
    {
        using var document = JsonDocument.Parse(target.GetRawText());
        var properties = new Dictionary<string, object?>();
        foreach (var item in document.RootElement.GetProperty("properties").EnumerateObject())
        {
            properties[item.Name] = JsonSerializer.Deserialize<object>(item.Value.GetRawText());
        }

        properties[property] = new Dictionary<string, string> { ["type"] = "string" };
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new[] { property },
        });
    }

    private static JsonElement Extend(JsonElement target, string properties, params string[] required)
    {
        var merged = new Dictionary<string, object?>();
        foreach (var item in target.GetProperty("properties").EnumerateObject())
        {
            merged[item.Name] = item.Value.Clone();
        }

        foreach (var item in Parse(properties).EnumerateObject())
        {
            merged[item.Name] = item.Value.Clone();
        }

        var schema = new Dictionary<string, object?> { ["type"] = "object", ["properties"] = merged };
        if (required.Length > 0)
        {
            schema["required"] = required;
        }

        return JsonSerializer.SerializeToElement(schema);
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

internal static class Args
{
    public static string? String(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    public static int? Int(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number))
            {
                return number;
            }

            if (property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out number))
            {
                return number;
            }
        }

        return null;
    }
}
