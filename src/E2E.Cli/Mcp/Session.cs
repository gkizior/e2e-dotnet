// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace E2E.Cli.Mcp;

/// <summary>
/// The fixed, four-tool surface of <c>e2e mcp</c>: <c>open_session</c>, <c>tools</c>, <c>call</c>, and
/// <c>close_session</c>. The list never changes; a session's vocabulary is a catalog behind <c>call</c>.
/// This port does not open sessions yet, so every session lookup finds none, and <c>open_session</c>
/// answers <c>UNSUPPORTED_CAPABILITY</c>.
/// </summary>
internal sealed class SessionHost
{
    /// <summary>How many sessions <c>e2e mcp --max-sessions</c> allows at once, each a browser or a device.</summary>
    public static readonly (int Min, int Max, int Default) SessionBounds = (1, 16, 4);

    private static readonly JsonElement OpenSchema = Parse(
        """
        {
          "type": "object",
          "properties": {
            "target": { "type": "string", "minLength": 1, "description": "Target name from the config; required when the config declares several" },
            "config": { "type": "string", "minLength": 1, "description": "Path to an e2e config file, relative to the server's directory; default: the nearest e2e.config.json" },
            "headed": { "type": "boolean", "description": "Show the browser or simulator, when the engine supports it; pass true when the user wants to watch. Default: headless, unless the server was started with --headed" }
          },
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement CatalogSchema = Parse(
        """
        {
          "type": "object",
          "properties": {
            "tool": { "type": "string", "minLength": 1, "description": "A catalog tool name, for its full contract" },
            "session": { "type": "string", "minLength": 1, "description": "Session id from open_session; may be omitted only while one session is open" }
          },
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement CallSchema = Parse(
        """
        {
          "type": "object",
          "properties": {
            "tool": { "type": "string", "minLength": 1, "description": "A catalog tool name, e.g. \"observe\", \"tap\", \"locate\", \"screenshot\"" },
            "args": { "type": "object", "propertyNames": { "type": "string" }, "additionalProperties": {}, "description": "The tool's arguments; omit for a tool without any" },
            "session": { "type": "string", "minLength": 1, "description": "Session id from open_session; may be omitted only while one session is open" }
          },
          "required": ["tool"],
          "additionalProperties": false
        }
        """);

    private static readonly JsonElement CloseSchema = Parse(
        """
        {
          "type": "object",
          "properties": {
            "session": { "type": "string", "minLength": 1, "description": "Session id from open_session; may be omitted only while one session is open" }
          },
          "additionalProperties": false
        }
        """);

    /// <summary>The server's tools: the same four whatever the project, the config, or the target.</summary>
    public IReadOnlyList<McpToolSpec> ToolSpecs()
    {
        return [OpenSpec(), CatalogSpec(), CallSpec(), CloseSpec()];
    }

    /// <summary>Why the session a call names, or the only live one when it names none, cannot be found: none is ever live yet.</summary>
    private static ConfigurationException NoSession(string? session)
    {
        return session is null
            ? new ConfigurationException("NO_SESSION", "no session is open; call open_session first")
            : new ConfigurationException("NO_SESSION", "session \"" + session + "\" is not open; no session is open");
    }

    private static McpToolSpec OpenSpec()
    {
        return new McpToolSpec
        {
            Name = "open_session",
            Description = "Open a live session on one target of an e2e project: loads the config, starts the app command the target declares (if any), boots the engine (a browser, a simulator), opens the app URL, and returns the session id, the catalog of tools it can run, and the first observation. Several sessions can be open at once, one per agent, each with its own engine: pass the returned session id to every later call. Then act with call and look with call {tool: \"observe\"}.",
            InputSchema = OpenSchema,
            ReadOnly = false,
            Call = (_, _) => throw new ConfigurationException(
                EngineErrorCodes.UnsupportedCapability,
                "this e2e mcp opens no session yet: it serves the guide (e2e://guide) and the fixed tools; live sessions come in a later release of the .NET port"),
        };
    }

    private static McpToolSpec CatalogSpec()
    {
        return new McpToolSpec
        {
            Name = "tools",
            Description = "List the tools a session can run through call: observe, the grammar its engine honors (one tool per action the engine declares, type_secret when a secret is configured, and screenshot and the point tools, which answer PIXEL_TAINTED once a secret has been filled), locate, start_recording and stop_recording when the engine records video, and the project's own tools. With tool, shows that tool's full description and the JSON Schema of its arguments.",
            InputSchema = CatalogSchema,
            ReadOnly = true,
            Call = (args, _) => throw NoSession(String(args, "session")),
        };
    }

    private static McpToolSpec CallSpec()
    {
        return new McpToolSpec
        {
            Name = "call",
            Description = "Run one tool of a session by name, with its arguments as an object: call {tool: \"tap\", args: {target: \"n42\"}, session: \"<id>\"}. The session's catalog (from open_session or tools) names the tools and their arguments. Actions report what changed on screen; node ids are valid only for the newest observation.",
            InputSchema = CallSchema,
            ReadOnly = false,
            Call = (args, _) => throw NoSession(String(args, "session")),
        };
    }

    private static McpToolSpec CloseSpec()
    {
        return new McpToolSpec
        {
            Name = "close_session",
            Description = "Close a live session: save a recording still running, end the attempt, dispose the engine, and stop the app processes the session started.",
            InputSchema = CloseSchema,
            ReadOnly = false,
            Call = (args, _) => String(args, "session") is { } session ? throw NoSession(session) : Task.FromResult(Tools.TextResult("No session is open.")),
        };
    }

    private static string? String(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out var value) ? value.GetString() : null;
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
