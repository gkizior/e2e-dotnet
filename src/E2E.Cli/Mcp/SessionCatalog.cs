// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Cli.Mcp;

/// <summary>A catalog tool that refused or failed. The message is the whole text the agent reads, code first.</summary>
internal sealed class ToolFailedException(string message) : Exception(message);

/// <summary>What a catalog tool needs of the live session it belongs to.</summary>
internal interface ISessionContext
{
    E2ESession Session { get; }

    /// <summary>Whether a secret was filled in this session, so a screenshot may show it.</summary>
    bool PixelsTainted { get; set; }
}

/// <summary>
/// The catalog of a live session: every tool <c>call</c> can run. <c>observe</c> shows the whole screen, the
/// grammar is what the target's engine honors, <c>screenshot</c> answers <c>PIXEL_TAINTED</c> once a secret has
/// been filled, <c>type_secret</c> exists when the config declares a secret, and <c>locate</c> tries a semantic
/// locator the way a test would. The verbs run through the testing agent's own tool executor, so a coding agent
/// acts exactly as the model of an <c>act</c> does.
/// </summary>
internal sealed class SessionCatalog
{
    /// <summary>How many matching nodes <c>locate</c> describes.</summary>
    private const int MaxLocateNodes = 10;

    private SessionCatalog(IReadOnlyList<KeyValuePair<string, CatalogTool>> entries, IReadOnlySet<string> readOnly)
    {
        Entries = entries;
        ReadOnly = readOnly;
    }

    /// <summary>Every tool <c>call</c> can run, in the order <c>tools</c> lists them.</summary>
    public IReadOnlyList<KeyValuePair<string, CatalogTool>> Entries { get; }

    /// <summary>The tools that change nothing on the app.</summary>
    public IReadOnlySet<string> ReadOnly { get; }

    /// <summary>The verbs the agent grammar names, whether or not the session's engine declares them: a missing one is not a typo.</summary>
    public static bool IsGrammarVerb(string name) => GrammarVerbs.Contains(name);

    /// <summary>The tool a name refers to, or null.</summary>
    public CatalogTool? Find(string name) => Entries.FirstOrDefault(pair => pair.Key == name).Value;

    /// <summary>Builds the catalog from what the session's engine declares and the secrets the config holds.</summary>
    public static SessionCatalog Create(ISessionContext context, EngineCapabilities capabilities, bool hasSecrets)
    {
        ArgumentNullException.ThrowIfNull(context);
        var tools = new List<KeyValuePair<string, CatalogTool>>();
        void Add(string name, CatalogTool tool) => tools.Add(new(name, tool));

        Add("observe", Observe(context));
        Add("tap", Verb(context, "tap", "tap", "Tap or click one node: the gesture for a button, link, menu item, tab, checkbox, row, or field.", Schema(Target)));
        Add("double_tap", Verb(context, "double_tap", "double_tap", "Double-click one node. Use it only when a tap does nothing, such as a label that opens an editor on a double-click.", Schema(Target)));
        Add("type", Verb(context, "type", "fill", "Replace the text in a field with text.", Schema(Target, ("text", Property("string", "The text the field should hold"), true)), ("text", "value")));
        Add("press", Verb(context, "press", "press", "Press a key such as Enter, Escape, or Tab. With a target, the key goes to that node; without one, to whatever has focus.", Schema(OptionalTarget, ("key", Property("string", "The key, such as Enter"), true))));
        Add("select", Verb(context, "select", "select", "Choose an option of a combobox or select, by its value or label.", Schema(Target, ("value", Property("string", "The option's value or label"), true))));
        Add("check", Verb(context, "check", "check", "Check a checkbox or radio.", Schema(Target)));
        Add("uncheck", Verb(context, "uncheck", "uncheck", "Uncheck a checkbox.", Schema(Target)));
        Add("clear", Verb(context, "clear", "clear", "Clear a field.", Schema(Target)));
        if (capabilities.HasFlag(EngineCapabilities.Scroll))
        {
            Add("scroll_to", Verb(context, "scroll_to", "scroll_to", "Scroll until a node is inside the viewport. With a target, one listed node above or below the fold. With text, a node the screen does not list yet: the list named by the target (or the viewport) is paged screen by screen until a node reading that text shows.", Schema(OptionalTarget, ("text", Property("string", "The visible text of the node to reach, or a distinctive part of it"), false), ("direction", Direction, false))));
            Add("scroll", Verb(context, "scroll", "scroll", "Scroll the viewport, or one scrollable node when a target is given, by a screen or a few.", Schema(OptionalTarget, ("direction", Direction, true), ("times", TimesProperty, false))));
        }

        Add("navigate", Verb(context, "navigate", "navigate", "Open a path or URL. A path resolves against the app URL; only http and https URLs are allowed.", Schema([], ("url", Property("string", "A path or URL"), true))));
        if (capabilities.HasFlag(EngineCapabilities.History))
        {
            Add("back", Verb(context, "back", "back", "Go back one entry in the browser history.", Schema([])));
        }

        if (capabilities.HasFlag(EngineCapabilities.Screenshot))
        {
            Add("screenshot", Screenshot(context));
        }

        if (hasSecrets)
        {
            Add("type_secret", TypeSecret(context));
        }

        Add("locate", Locate(context));
        return new SessionCatalog(tools, new HashSet<string>(["observe", "locate", "screenshot"], StringComparer.Ordinal));
    }

    /// <summary>
    /// Builds the locator and the matching <c>Screen</c> call from the tool arguments: exactly one of role, text,
    /// label, placeholder, or testId.
    /// </summary>
    public static LocateQuery BuildLocateQuery(LocateArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var exact = args.Exact != false;
        var given = new[] { args.Role, args.Text, args.Label, args.Placeholder, args.TestId }.Count(value => value is not null);
        if (given != 1)
        {
            throw new ConfigurationException("INVALID_ARGUMENT", "locate needs exactly one of role, text, label, placeholder, or testId");
        }

        var suffix = exact ? "" : ", exact: false";
        if (args.Role is { } role)
        {
            var name = args.Name is null ? "" : ", " + Quote(args.Name) + suffix;
            return new LocateQuery(
                LocatorQuery.ForRole(role, args.Name, new RoleOptions { Exact = exact }),
                "Screen.GetByRole(" + Quote(role) + name + ")");
        }

        if (args.Name is not null)
        {
            throw new ConfigurationException("INVALID_ARGUMENT", "name only narrows a role query");
        }

        if (args.TestId is { } testId)
        {
            return new LocateQuery(LocatorQuery.For("testid", testId, new TextMatchOptions(), nameof(testId)), "Screen.GetByTestId(" + Quote(testId) + ")");
        }

        var options = new TextMatchOptions { Exact = exact };
        if (args.Text is { } text)
        {
            return new LocateQuery(LocatorQuery.For("text", text, options, nameof(text)), "Screen.GetByText(" + Quote(text) + suffix + ")");
        }

        return args.Label is { } label
            ? new LocateQuery(LocatorQuery.For("label", label, options, nameof(label)), "Screen.GetByLabel(" + Quote(label) + suffix + ")")
            : new LocateQuery(LocatorQuery.For("placeholder", args.Placeholder!, options, "placeholder"), "Screen.GetByPlaceholder(" + Quote(args.Placeholder!) + suffix + ")");
    }

    /// <summary>Renders a locate result: the count, the verdict a test would get, and the nodes, each through the attempt's redactor.</summary>
    public static string DescribeLocate(LocateQuery query, int count, IReadOnlyList<SemanticNode> nodes, Redactor redactor)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(redactor);
        var lines = new List<string>
        {
            (count == 1 ? "1 node matches " : count.ToString(CultureInfo.InvariantCulture) + " nodes match ") + redactor.Redact(query.Query.Describe()) + ".",
        };
        if (count == 1)
        {
            lines.Add("Use: " + redactor.Redact(query.Code));
        }
        else if (count == 0)
        {
            lines.Add("A test using this locator would fail with NOT_FOUND. Check the accessible name in the observation (observe), or loosen the match with exact: false.");
        }
        else
        {
            lines.Add("A test action on " + redactor.Redact(query.Code) + " would fail with STRICT_MODE. Narrow it with a name, GetByRole(role, name), .Filter(...), .First(), or .Nth(i), or scope it under a container.");
        }

        lines.AddRange(nodes.Select(node => "- " + DescribeNode(node, redactor)));
        if (count > nodes.Count)
        {
            lines.Add("- and " + (count - nodes.Count).ToString(CultureInfo.InvariantCulture) + " more");
        }

        return string.Join('\n', lines);
    }

    private static readonly HashSet<string> GrammarVerbs = new(
        ["observe", "tap", "double_tap", "type", "type_secret", "press", "select", "check", "uncheck", "clear", "scroll_to", "scroll", "navigate", "back", "screenshot"],
        StringComparer.Ordinal);

    private static readonly (string Name, JsonNode Schema, bool Required)[] Target =
        [("target", Property("string", "Node ref from the newest observation, such as the value after ref= in observe"), true)];

    private static readonly (string Name, JsonNode Schema, bool Required)[] OptionalTarget =
        [("target", Property("string", "Node ref from the newest observation, such as the value after ref= in observe"), false)];

    private static JsonNode Direction => new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("up", "down", "left", "right") };

    private static JsonNode TimesProperty => new JsonObject
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["maximum"] = AgentTools.MaxScrollTimes,
        ["description"] = "How many screens to scroll in this one call; default 1",
    };

    private static JsonObject Property(string type, string description)
    {
        var property = new JsonObject { ["type"] = type };
        if (type == "string")
        {
            property["minLength"] = 1;
        }

        property["description"] = description;
        return property;
    }

    private static JsonElement Schema((string Name, JsonNode Schema, bool Required)[] target, params (string Name, JsonNode Schema, bool Required)[] more)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in target.Concat(more))
        {
            properties[name] = schema.DeepClone();
            if (isRequired)
            {
                required.Add(name);
            }
        }

        var root = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
        {
            root["required"] = required;
        }

        root["additionalProperties"] = false;
        return JsonSerializer.SerializeToElement(root);
    }

    private static CatalogTool Observe(ISessionContext context)
    {
        return new CatalogTool
        {
            Description = "Look at the whole current screen: every node with its ref, role, name, and state. Action results show the screen after the action; call this to look again after waiting for something in progress.",
            InputSchema = Schema([]),
            Execute = async (_, token) =>
            {
                var outcome = await RunAsync(context, "observe", "observe", default, token).ConfigureAwait(false);
                return outcome.Content.StartsWith("observed\n", StringComparison.Ordinal) ? outcome.Content["observed\n".Length..] : outcome.Content;
            },
        };
    }

    /// <summary>A grammar verb: the arguments are renamed to the agent tool's (<c>target</c> is its <c>ref</c>), and the tool runs as in an act.</summary>
    private static CatalogTool Verb(ISessionContext context, string name, string agentTool, string description, JsonElement schema, (string From, string To)? rename = null)
    {
        return new CatalogTool
        {
            Description = description,
            InputSchema = schema,
            Execute = async (args, token) =>
            {
                var mapped = new JsonObject();
                foreach (var property in args.EnumerateObject())
                {
                    var key = property.Name == "target" ? "ref" : rename is { } map && map.From == property.Name ? map.To : property.Name;
                    mapped[key] = JsonNode.Parse(property.Value.GetRawText());
                }

                var outcome = await RunAsync(context, name, agentTool, args, token, mapped).ConfigureAwait(false);
                return outcome.Content;
            },
        };
    }

    private static CatalogTool TypeSecret(ISessionContext context)
    {
        return new CatalogTool
        {
            Description = "Fill a configured secret into an editable field by its name. You never see the value: every output shows the secret as <secret:name>. After a fill, screenshot withholds pixels.",
            InputSchema = Schema(Target, ("secret", Property("string", "The secret's name from the config"), true)),
            Execute = async (args, token) =>
            {
                var mapped = new JsonObject { ["ref"] = args.GetProperty("target").GetString(), ["secret"] = args.GetProperty("secret").GetString() };

                // Before the fill: a fill that reached the page and then failed must still withhold pixels.
                context.PixelsTainted = true;
                var outcome = await RunAsync(context, "type_secret", "fill_secret", args, token, mapped).ConfigureAwait(false);
                return outcome.Content;
            },
        };
    }

    private static async Task<ToolOutcome> RunAsync(ISessionContext context, string name, string agentTool, JsonElement args, CancellationToken token, JsonObject? mapped = null)
    {
        ToolOutcome outcome;
        var target = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("target", out var node) ? node.GetString() : null;
        var subject = target ?? (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("url", out var url) ? url.GetString() : null);
        var label = subject is null ? name : name + " " + subject;
        try
        {
            outcome = await context.Session.Agent.RunToolAsync(agentTool, JsonSerializer.SerializeToElement(mapped ?? new JsonObject()), token).ConfigureAwait(false);
        }
        catch (E2EException ex)
        {
            throw new ToolFailedException(Failure(label, ex.Code, ex.Message, null));
        }

        if (!outcome.Succeeded)
        {
            var message = outcome.Content.StartsWith("failed: ", StringComparison.Ordinal) ? outcome.Content["failed: ".Length..] : outcome.Content;
            throw new ToolFailedException(Failure(label, outcome.Code, message, target));
        }

        return outcome;
    }

    private static string Failure(string label, string? code, string message, string? target)
    {
        if (code == "NOT_FOUND" && target is not null && message.StartsWith("No control matched", StringComparison.Ordinal))
        {
            message = "node " + target + " is not on the current screen; re-observe for the newest refs";
        }

        return label + " failed: " + (code is null ? "" : code + ": ") + message;
    }

    private static CatalogTool Screenshot(ISessionContext context)
    {
        return new CatalogTool
        {
            Description = "Take a screenshot of the viewport, attached as an image. Secure fields are covered. After a secret was filled in the session, it answers PIXEL_TAINTED and attaches nothing.",
            InputSchema = Schema([]),
            Execute = async (_, token) =>
            {
                if (context.PixelsTainted)
                {
                    return new ScreenshotOutput(null, "No screenshot: a secret was filled in this attempt and the app may show it outside a secure field. PIXEL_TAINTED");
                }

                var shot = await context.Session.Engine.ScreenshotAsync(token).ConfigureAwait(false);
                var (width, height) = PngSize(shot.Png);
                var text = "Screenshot attached: " + width.ToString(CultureInfo.InvariantCulture) + " by " + height.ToString(CultureInfo.InvariantCulture)
                    + " pixels (" + shot.Scale.ToString("0.##", CultureInfo.InvariantCulture) + " per CSS pixel).";
                return new ScreenshotOutput(shot.Png, text);
            },
            ToModelOutput = (_, output, _) =>
            {
                var shot = (ScreenshotOutput)output!;
                IReadOnlyList<ToolOutputPart> parts = shot.Png is null
                    ? [new ToolOutputPart.Text(shot.Text)]
                    : [new ToolOutputPart.Text(shot.Text), new ToolOutputPart.File(Convert.ToBase64String(shot.Png), "image/png")];
                return Task.FromResult<ToolModelOutput>(new ToolModelOutput.Content(parts));
            },
        };
    }

    private static (int Width, int Height) PngSize(byte[] png) =>
        png.Length < 24 ? (0, 0) : (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));

    private static CatalogTool Locate(ISessionContext context)
    {
        return new CatalogTool
        {
            Description = "Try a semantic locator against the live screen before writing it into a test: Screen.GetByRole(role, name), GetByText, GetByLabel, GetByPlaceholder, or GetByTestId. Returns how many nodes match and which, plus the test code to use. Exactly one of role, text, label, placeholder, or testId; name narrows a role query. Matching is exact unless exact is false.",
            InputSchema = Schema(
                [],
                ("role", Property("string", "ARIA role, e.g. \"button\", \"textbox\", \"link\""), false),
                ("name", Property("string", "Accessible name, with role"), false),
                ("text", Property("string", "Visible text"), false),
                ("label", Property("string", "Accessible label"), false),
                ("placeholder", Property("string", "Placeholder text"), false),
                ("testId", Property("string", "Test id"), false),
                ("exact", new JsonObject { ["type"] = "boolean", ["description"] = "false for substring, case-insensitive matching" }, false)),
            Execute = async (args, token) =>
            {
                var query = BuildLocateQuery(new LocateArgs
                {
                    Role = Text(args, "role"),
                    Name = Text(args, "name"),
                    Text = Text(args, "text"),
                    Label = Text(args, "label"),
                    Placeholder = Text(args, "placeholder"),
                    TestId = Text(args, "testId"),
                    Exact = args.TryGetProperty("exact", out var exact) ? exact.GetBoolean() : null,
                });
                var observation = await context.Session.Engine.ObserveAsync(token).ConfigureAwait(false);
                var matches = LocatorResolver.Resolve(observation, query.Query);
                return DescribeLocate(query, matches.Count, [.. matches.Take(MaxLocateNodes)], context.Session.Redactor);
            },
        };
    }

    private static string? Text(JsonElement args, string name) => args.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>
    /// A located node by what names it, as the observation shows it: name, text, and value have passed the secret
    /// ledger, and a secure node carries no value.
    /// </summary>
    private static string DescribeNode(SemanticNode node, Redactor redactor)
    {
        var parts = new List<string> { node.Role ?? "node" };
        var name = string.IsNullOrEmpty(node.Name) ? node.Text : node.Name;
        if (!string.IsNullOrEmpty(name))
        {
            parts.Add(Quote(redactor.Redact(TextRules.Normalize(name))));
        }

        if (!string.IsNullOrEmpty(node.Name) && !string.IsNullOrEmpty(node.Text) && node.Text != node.Name)
        {
            parts.Add("text " + Quote(redactor.Redact(TextRules.Normalize(node.Text))));
        }

        if (!node.States.Secure && node.Value is not null)
        {
            parts.Add("value " + Quote(redactor.Redact(node.Value)));
        }

        var states = new List<string>();
        void State(bool on, string label)
        {
            if (on)
            {
                states.Add(label);
            }
        }

        State(node.States.Checked, "checked");
        State(node.States.Disabled, "disabled");
        State(node.States.Expanded, "expanded");
        State(node.States.Selected, "selected");
        State(node.States.Pressed, "pressed");
        State(node.States.Focused, "focused");
        if (states.Count > 0)
        {
            parts.Add("[" + string.Join(", ", states) + "]");
        }

        return string.Join(' ', parts);
    }

    private static string Quote(string text) => JsonSerializer.Serialize(text, E2E.Cli.Mcp.Tools.Json);

    private sealed record ScreenshotOutput(byte[]? Png, string Text);
}

/// <summary>The arguments of <c>locate</c>.</summary>
internal sealed class LocateArgs
{
    public string? Role { get; init; }

    public string? Name { get; init; }

    public string? Text { get; init; }

    public string? Label { get; init; }

    public string? Placeholder { get; init; }

    public string? TestId { get; init; }

    public bool? Exact { get; init; }
}

/// <summary>A locator and the <c>Screen</c> call that builds it.</summary>
internal sealed record LocateQuery(LocatorQuery Query, string Code);
