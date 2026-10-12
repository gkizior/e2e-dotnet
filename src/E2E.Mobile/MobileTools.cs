// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>
/// One agent-side tool for device targets: its name, what the model is told, the JSON schema of its input, and what it does.
/// </summary>
/// <param name="Name">The tool's name, fixed.</param>
/// <param name="Description">What the model is told it does.</param>
/// <param name="InputSchema">The JSON schema of the tool's input.</param>
/// <param name="Mutates">Whether the tool changes the device. The trace cache records a mutating tool as a replay gap.</param>
/// <param name="Platforms">The platforms of the engines the tool was built from. It is never offered on another.</param>
/// <param name="ExecuteAsync">Runs the tool on the attempt that is running, with its input as the model wrote it, and answers what the model reads.</param>
public sealed record DeviceTool(
    string Name,
    string Description,
    JsonObject InputSchema,
    bool Mutates,
    IReadOnlyList<MobilePlatform> Platforms,
    Func<JsonObject, CancellationToken, Task<string>> ExecuteAsync);

/// <summary>
/// Agent-side tools for device targets. Everything a model plans through beyond the grammar verbs the engine already
/// unlocks (tap, type, scroll, screenshot, tap_at) lives here: opening another app, a free-form swipe, and system alerts.
/// Each tool is scoped to the platforms of the engines it was built from, so a mixed suite never offers it on a browser.
/// </summary>
public static class MobileTools
{
    /// <summary>
    /// Builds the tool pack for one or more device engines, keyed by tool name. Pass every device engine a config
    /// declares: tool names are fixed, so two packs cannot be merged, and each tool is offered only on the platforms those
    /// engines drive. A worker runs one attempt at a time, so at execution the pack dispatches to the session whose attempt
    /// is running. Mutating tools are recorded as replay gaps by the trace cache. A step that stays within the grammar verbs
    /// replays with no model turn, so prefer the target's <c>app.bundleId</c> over <c>open_app</c> when a test always starts
    /// in the same app.
    /// </summary>
    public static IReadOnlyDictionary<string, DeviceTool> Create(params MobileEngine[] engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        if (engines.Length == 0)
        {
            throw new ArgumentException("mobileTools needs at least one engine", nameof(engines));
        }

        var platforms = engines.Select(engine => engine.Options.Platform).Distinct().ToList();

        DeviceSession Active()
        {
            var running = engines.SelectMany(engine => engine.ActiveSessions).ToList();
            return running.Count == 1
                ? running[0]
                : throw new EngineException(
                    EngineErrorCodes.InvalidState,
                    running.Count == 0
                        ? "no agent-device attempt is running; device tools act inside a test attempt only"
                        : "several agent-device attempts are running in one worker; tools cannot pick a device",
                    retryable: false);
        }

        var tools = new[]
        {
            new DeviceTool(
                "open_app",
                "Open an app by bundle id, package, or display name (e.g. \"Settings\"), bringing it to the foreground. Set relaunch to restart it fresh. Takes an app, not a URL: a link is refused.",
                Schema(new JsonObject { ["app"] = Primitive("string", minLength: 1), ["relaunch"] = Primitive("boolean") }, "app"),
                true,
                platforms,
                async (input, token) =>
                {
                    var app = RequireString(input, "app");
                    await Active().OpenAppAsync(app, new OpenAppOptions { Relaunch = OptionalBoolean(input, "relaunch") }, token).ConfigureAwait(false);
                    return $"Opened {app}.";
                }),
            new DeviceTool(
                "swipe",
                "Swipe from one screen point to another in logical pixels, e.g. to reveal a row action (swipe the row far left) or to dismiss a sheet.",
                Schema(new JsonObject { ["from"] = PointSchema(), ["to"] = PointSchema() }, "from", "to"),
                true,
                platforms,
                async (input, token) =>
                {
                    var from = RequirePoint(input, "from");
                    var to = RequirePoint(input, "to");
                    await Active().CommandAsync(
                        "swipe",
                        DeviceMethods.InteractionsSwipe,
                        DeviceArgs.Of(("from", DeviceSession.PointNode(from)), ("to", DeviceSession.PointNode(to))),
                        token).ConfigureAwait(false);
                    return $"Swiped from ({DeviceSession.Invariant(from.X)}, {DeviceSession.Invariant(from.Y)}) to ({DeviceSession.Invariant(to.X)}, {DeviceSession.Invariant(to.Y)}).";
                }),
            new DeviceTool(
                "alert",
                "Accept or dismiss a visible system alert or permission prompt. Use it when the alert's buttons are not listed on screen; a listed button tapped by id is the same tap and replays from the trace cache, this tool does not.",
                Schema(new JsonObject { ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("accept", "dismiss") } }, "action"),
                true,
                platforms,
                async (input, token) =>
                {
                    var action = RequireString(input, "action");
                    if (action is not ("accept" or "dismiss"))
                    {
                        throw new TestException("INVALID_ARGUMENT", "alert takes an action of accept or dismiss");
                    }

                    await Active().CommandAsync("alert", DeviceMethods.CommandAlert, DeviceArgs.Of(("action", JsonValue.Create(action))), token).ConfigureAwait(false);
                    return $"Alert {action}ed.";
                }),
        };
        return tools.ToDictionary(tool => tool.Name);
    }

    private static JsonObject Schema(JsonObject properties, params string[] required) => new()
    {
        ["type"] = "object",
        ["properties"] = properties,
        ["required"] = new JsonArray([.. required.Select(name => (JsonNode?)JsonValue.Create(name))]),
        ["additionalProperties"] = false,
    };

    private static JsonObject Primitive(string type, int? minLength = null)
    {
        var schema = new JsonObject { ["type"] = type };
        if (minLength is { } length)
        {
            schema["minLength"] = length;
        }

        return schema;
    }

    private static JsonObject PointSchema() => Schema(new JsonObject { ["x"] = Primitive("number"), ["y"] = Primitive("number") }, "x", "y");

    private static string RequireString(JsonObject input, string name) =>
        input[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0
            ? text
            : throw new TestException("INVALID_ARGUMENT", $"{name} must be a non-empty string");

    private static bool OptionalBoolean(JsonObject input, string name) =>
        input[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    private static Point RequirePoint(JsonObject input, string name) =>
        input[name] is JsonObject point && Number(point["x"]) is { } x && Number(point["y"]) is { } y
            ? new Point(x, y)
            : throw new TestException("INVALID_ARGUMENT", $"{name} must be a point with a number x and y");

    private static double? Number(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;
}
