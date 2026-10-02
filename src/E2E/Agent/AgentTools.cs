// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

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

    private static readonly JsonElement Done = Parse(
        """
        {
          "type": "object",
          "properties": {
            "status": { "type": "string", "enum": ["passed", "failed", "blocked"] },
            "summary": { "type": "string" },
            "code": { "type": "string" }
          },
          "required": ["status", "summary"]
        }
        """);

    public static IReadOnlyList<ModelTool> Act { get; } =
    [
        Tool("tap", "Activate a control.", Target),
        Tool("fill", "Replace the text in a field.", WithValue(Target, "value")),
        Tool("fill_secret", "Type a declared secret into a password or secret field. Pass the secret name, never the value.", WithValue(Target, "secret")),
        Tool("press", "Press a key such as Enter, Escape, or Tab. Omit role and name to press the focused control.", WithValue(Target, "key")),
        Tool("select", "Choose an option in a combobox.", WithValue(Target, "value")),
        Tool("check", "Check a checkbox.", Target),
        Tool("uncheck", "Uncheck a checkbox.", Target),
        Tool("clear", "Clear a field.", Target),
        Tool("navigate", "Open a path or URL.", Parse("""{ "type": "object", "properties": { "url": { "type": "string" } }, "required": ["url"] }""")),
        Tool("done", "End the step with a verdict.", Done),
    ];

    public static IReadOnlyList<ModelTool> Judge { get; } =
    [
        Tool("done", "End the judgment. Use ASSERTION_FAILED or ASSERTION_INCONCLUSIVE when status is failed.", Done),
    ];

    public static IReadOnlyList<ModelTool> Extract { get; } =
    [
        Tool("extract", "Return the data read from the screen.", Parse("""{ "type": "object", "properties": { "data": { "type": "object" } }, "required": ["data"] }""")),
        Tool("done", "Use this only when the data is not on screen.", Done),
    ];

    private static ModelTool Tool(string name, string description, JsonElement parameters)
    {
        return new ModelTool { Name = name, Description = description, Parameters = parameters };
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
}
