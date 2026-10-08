// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// Upstream's server still declares logging and sends log messages; the 2026-07-28 MCP revision deprecates them.
#pragma warning disable MCP9005

namespace E2E.Cli.Mcp;

/// <summary>What <see cref="Server.ServeAsync"/> serves, and where.</summary>
internal sealed class ServeOptions
{
    public required string Cwd { get; init; }

    /// <summary>The config every session loads unless <c>open_session</c> names one.</summary>
    public string? ConfigPath { get; init; }

    /// <summary>The target every session opens on; otherwise a call names one, or the only one is used.</summary>
    public string? Target { get; init; }

    /// <summary>Whether a session shows its UI when <c>open_session</c> does not set <c>headed</c>.</summary>
    public bool Headed { get; init; }

    /// <summary>How many sessions may be open at once, from <c>--max-sessions</c>.</summary>
    public int MaxSessions { get; init; } = SessionHost.SessionBounds.Default;

    public required string Version { get; init; }

    public required Stream Stdin { get; init; }

    /// <summary>The protocol stream: nothing else may write to it.</summary>
    public required Stream Stdout { get; init; }

    /// <summary>Diagnostics for the operator, normally stderr.</summary>
    public required Action<string> Log { get; init; }
}

/// <summary>
/// The <c>e2e mcp</c> server: a live session on the app for a coding agent, with the skill as resources,
/// served over one stdio transport. The tool list is four tools and never changes: the session's own
/// vocabulary is a catalog behind <c>call</c>. Streams are injected, so the CLI hands it the process's and a
/// test hands it pipes.
/// </summary>
internal static class Server
{
    public const string Instructions = """
        e2e-dotnet is the .NET port of the e2e end-to-end test framework; this server drives an e2e project's app (its e2e.config.json) live.
        Call open_session (optionally with a target, a config path, and headed: true when the user wants to watch) to get a session, its tool catalog, and the first observation. Then call {tool, args} runs any catalog tool: observe, the grammar its engine honors, type_secret, locate, screenshot, start_recording and stop_recording when the engine records video (a video of the app for a pull request), and the project's own tools; tools lists them, tools {tool} shows one tool's arguments. close_session when done.
        Several sessions can be open at once, each with its own browser or device, so parallel agents (subagents) each open their own: pass the session id from open_session to every tools, call, and close_session.
        Write deterministic tests (E2ETest classes) from what you saw and run them with dotnet test. Resources e2e://guide and e2e://guide/{topic} hold the writing guide.
        This build does not open sessions yet: open_session answers UNSUPPORTED_CAPABILITY. Until it does, read e2e://guide/writing-tests and e2e://guide/writing-tests-nunit or e2e://guide/writing-tests-xunit, and write tests from the app's markup.
        """;

    private const string GuideUri = "e2e://guide";

    private const string Markdown = "text/markdown";

    /// <summary>How long the server waits for the protocol to close before it exits anyway.</summary>
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);

    /// <summary>Serves until the client disconnects or <paramref name="cancellationToken"/> is cancelled; returns the exit code.</summary>
    public static async Task<int> ServeAsync(ServeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var specs = new SessionHost().ToolSpecs();
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "e2e", Title = "e2e", Version = options.Version },
            ServerInstructions = Instructions,
            Capabilities = new ServerCapabilities { Tools = new(), Resources = new(), Logging = new() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [.. specs.Select(ToTool)] }),
                CallToolHandler = (request, cancel) => CallAsync(specs, request.Params, cancel),
                ListResourcesHandler = (_, _) => ValueTask.FromResult(ListGuide()),
                ListResourceTemplatesHandler = (_, _) => ValueTask.FromResult(ListGuideTemplates()),
                ReadResourceHandler = (request, _) => ValueTask.FromResult(ReadGuide(request.Params?.Uri)),
            },
        };

        // No logger factory: the SDK logs nothing, so nothing but the protocol reaches the protocol stream.
        var transport = new StreamServerTransport(options.Stdin, options.Stdout, "e2e");
        var server = McpServer.Create(transport, serverOptions);
        var running = server.RunAsync(cancellationToken);
        Log(server, options, LoggingLevel.Info, "e2e mcp " + options.Version + " serving " + options.Cwd);
        try
        {
            // A blocking read on stdin ignores the token, so a signal stops the wait rather than the read.
            await running.WaitAsync(cancellationToken).ConfigureAwait(false);
            options.Log("[info] client disconnected; closing every session");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A signal: the server shuts down as on a disconnect.
        }

        // Disposing waits for the stdin read too; a client that keeps stdin open must not hold the exit.
        await Task.WhenAny(DisposeAsync(server, transport), Task.Delay(CloseGrace, CancellationToken.None)).ConfigureAwait(false);
        return 0;
    }

    private static async Task DisposeAsync(McpServer server, StreamServerTransport transport)
    {
        await server.DisposeAsync().ConfigureAwait(false);
        await transport.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>Logs a line for the operator, and for the client at the level it asked for.</summary>
    private static void Log(McpServer server, ServeOptions options, LoggingLevel level, string message)
    {
        options.Log("[" + level.ToString().ToLowerInvariant() + "] " + message);
        if (server.LoggingLevel is { } minimum && level < minimum)
        {
            return;
        }

        var notification = new LoggingMessageNotificationParams { Level = level, Logger = "e2e", Data = JsonSerializer.SerializeToElement(message) };
        _ = server.SendNotificationAsync(NotificationMethods.LoggingMessageNotification, notification).ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private static Tool ToTool(McpToolSpec spec)
    {
        return new Tool
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = spec.InputSchema,
            Annotations = new ToolAnnotations { ReadOnlyHint = spec.ReadOnly, OpenWorldHint = false },
        };
    }

    /// <summary>
    /// Checks the arguments against the tool's closed schema, then runs it. A refusal or a failure is an error
    /// result the agent can react to. Only a tool the server does not have is a protocol error, as upstream.
    /// </summary>
    private static async ValueTask<CallToolResult> CallAsync(IReadOnlyList<McpToolSpec> specs, CallToolRequestParams? request, CancellationToken cancellationToken)
    {
        var name = request?.Name ?? "";
        var spec = specs.FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new McpProtocolException("Tool " + name + " not found", McpErrorCode.InvalidParams);
        var args = JsonSerializer.SerializeToElement(request?.Arguments ?? new Dictionary<string, JsonElement>());
        var issues = ArgumentSchema.Validate(spec.InputSchema, args);
        if (issues.Count > 0)
        {
            var text = "Input validation error: Invalid arguments for tool " + name + ": " + string.Join(", ", issues.Select(issue => issue.ToString()));
            return new CallToolResult { Content = [new TextContentBlock { Text = text }], IsError = true };
        }

        try
        {
            return await spec.Call(args, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any failure is the agent's to read, as a test would have reported it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Tools.ErrorResult(ex);
        }
    }

    /// <summary>The skill as resources, for clients that read context without a tool call.</summary>
    private static ListResourcesResult ListGuide()
    {
        var guide = new Resource
        {
            Uri = GuideUri,
            Name = "guide",
            Title = "e2e guide",
            Description = "How to set up e2e, write tests, use agent steps, run them, and read a failing run",
            MimeType = Markdown,
        };
        return new ListResourcesResult
        {
            Resources = [guide, .. Skill.Topics().Select(topic => new Resource { Uri = GuideUri + "/" + topic, Name = topic, MimeType = Markdown })],
        };
    }

    private static ListResourceTemplatesResult ListGuideTemplates()
    {
        var template = new ResourceTemplate
        {
            UriTemplate = GuideUri + "/{topic}",
            Name = "guide-topic",
            Title = "e2e guide topic",
            Description = "One topic of the guide: " + string.Join(", ", Skill.Topics()),
            MimeType = Markdown,
        };
        return new ListResourceTemplatesResult { ResourceTemplates = [template] };
    }

    private static ReadResourceResult ReadGuide(string? uri)
    {
        string text;
        if (uri == GuideUri)
        {
            text = Skill.ReadGuide(null) ?? "The skill is not part of this installation.";
        }
        else if (uri is not null && uri.StartsWith(GuideUri + "/", StringComparison.Ordinal) && !uri[(GuideUri.Length + 1)..].Contains('/', StringComparison.Ordinal))
        {
            var topic = Uri.UnescapeDataString(uri[(GuideUri.Length + 1)..]);
            text = Skill.ReadGuide(topic) ?? throw new McpProtocolException(
                "UNKNOWN_TOPIC: unknown topic \"" + topic + "\"; topics: " + string.Join(", ", Skill.Topics()),
                McpErrorCode.ResourceNotFound);
        }
        else
        {
            throw new McpProtocolException("Resource " + uri + " not found", McpErrorCode.ResourceNotFound);
        }

        return new ReadResourceResult { Contents = [new TextResourceContents { Uri = uri, MimeType = Markdown, Text = text }] };
    }
}
