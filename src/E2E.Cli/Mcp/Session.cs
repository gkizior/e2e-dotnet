// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using E2E.Engine;
using E2E.Internal;
using ModelContextProtocol.Protocol;

namespace E2E.Cli.Mcp;

/// <summary>What <c>open_session</c> asks for.</summary>
internal sealed class OpenSessionOptions
{
    /// <summary>The target to open; otherwise the server's <c>--target</c>, else the config's only target.</summary>
    public string? Target { get; init; }

    /// <summary>A config file to load instead of the server's default, relative to the server's directory.</summary>
    public string? Config { get; init; }

    /// <summary>Whether this session shows its UI; otherwise the server's <c>--headed</c>.</summary>
    public bool? Headed { get; init; }
}

/// <summary>How a <see cref="SessionHost"/> finds the config, boots the engine, and reports.</summary>
internal sealed class SessionHostOptions
{
    /// <summary>The absolute path of the config a session loads, without evaluating it; the argument overrides the server's default.</summary>
    public required Func<string?, string> LocateConfig { get; init; }

    /// <summary>Loads the config at an absolute path fresh for each session, so an edited config applies without a restart.</summary>
    public Func<string, Task<E2EConfig>> LoadConfig { get; init; } = path => Task.Run(() => E2EConfig.Load(path));

    /// <summary>Creates the engine of one session; the argument says whether the session shows its UI.</summary>
    public Func<bool, IEngine> CreateEngine { get; init; } = headed => new WebEngine(headless: !headed);

    /// <summary>Whether a session shows its UI when <c>open_session</c> does not say, from <c>--headed</c>.</summary>
    public bool Headed { get; init; }

    /// <summary>The target every session opens on, from <c>--target</c>; a call may still name one.</summary>
    public string? DefaultTarget { get; init; }

    /// <summary>How many sessions may be open at once, from <c>--max-sessions</c>.</summary>
    public int MaxSessions { get; init; } = SessionHost.SessionBounds.Default;

    /// <summary>A session nobody has touched for this long is closed, so no browser is left behind.</summary>
    public TimeSpan Idle { get; init; } = SessionHost.SessionIdle;

    /// <summary>How long one session may live, whatever happens.</summary>
    public TimeSpan Ttl { get; init; } = SessionHost.SessionTtl;

    /// <summary>Logs a line for the operator and the client: <c>info</c>, <c>warning</c>, or <c>error</c>.</summary>
    public Action<string, string> Log { get; init; } = static (_, _) => { };
}

/// <summary>
/// The live sessions behind <c>e2e mcp</c>: each one a standalone attempt on one target, driven by a coding
/// agent through a fixed, four-tool surface. <c>open_session</c> loads the project's config and starts the
/// engine; <c>tools</c> renders the session's catalog; <c>call</c> runs one catalog tool by name;
/// <c>close_session</c> ends the attempt and disposes the engine. Several sessions may be open at once, up to the
/// server's limit, so parallel agents each drive their own browser. A call names its session by id, and may leave
/// it out only while one is open. The catalog is data, not registrations, so it follows the config and the
/// target without a restart and the client's tool list never changes. A session never reads or writes the replay
/// cache: nobody asked it to record a test.
/// </summary>
internal sealed class SessionHost : IDisposable
{
    /// <summary>How many sessions <c>e2e mcp --max-sessions</c> allows at once, each a browser or a device.</summary>
    public static readonly (int Min, int Max, int Default) SessionBounds = (1, 16, 4);

    /// <summary>A session nobody has touched for this long is closed.</summary>
    internal static readonly TimeSpan SessionIdle = TimeSpan.FromMinutes(30);

    /// <summary>How long one session may live.</summary>
    internal static readonly TimeSpan SessionTtl = TimeSpan.FromHours(4);

    /// <summary>How long the attempt outlives the session's lifetime, so a session that hit it is still closed in order.</summary>
    private static readonly TimeSpan CloseGrace = TimeSpan.FromMinutes(1);

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

    private readonly SessionHostOptions _options;
    private readonly SessionRegistry<LiveSession> _sessions;
    private readonly SecretLedger _secrets = new();

    /// <summary>Aborts every open still running once the server shuts down, so none leaves a browser behind.</summary>
    private readonly CancellationTokenSource _shutdown = new();

    public SessionHost(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _sessions = new SessionRegistry<LiveSession>(options.MaxSessions);
    }

    public bool IsOpen => _sessions.HasLive;

    /// <summary>The server's tools: the same four whatever the project, the config, or the target, each redacting what it returns.</summary>
    public IReadOnlyList<McpToolSpec> ToolSpecs()
    {
        return [Redacting(OpenSpec()), Redacting(CatalogSpec()), Redacting(CallSpec()), Redacting(CloseSpec())];
    }

    /// <summary>
    /// Opens a session and returns its opening text: the summary, the catalog, and the first screen.
    /// <paramref name="cancellationToken"/> is the request's: a client that cancels it, or disconnects, aborts the
    /// open and what it started.
    /// </summary>
    public Task<string> OpenAsync(OpenSessionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var headed = options.Headed ?? _options.Headed;
        return _sessions.AdmitAsync(id => OpenSessionAsync(id, options, headed, cancellationToken));
    }

    /// <summary>Closes one session, the only one when <paramref name="session"/> is omitted, and returns what happened; a session already closing returns that close.</summary>
    public async Task<string> CloseAsync(string reason, string? session = null)
    {
        if (session is null && !_sessions.HasLive)
        {
            return "No session is open.";
        }

        return await _sessions.CloseAsync(session, reason, live => TeardownAsync(live, reason)).ConfigureAwait(false);
    }

    /// <summary>Closes every session, for a server that is shutting down; null when none was open.</summary>
    public async Task<string?> CloseAllAsync(string reason)
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        var summaries = await _sessions.CloseAllAsync(reason, live => TeardownAsync(live, reason)).ConfigureAwait(false);
        return summaries.Count == 0 ? null : string.Join('\n', summaries);
    }

    /// <summary>The session's catalog, or one tool's full contract.</summary>
    public string Catalog(string? session, string? tool)
    {
        var live = _sessions.Resolve(session);
        if (tool is null)
        {
            return string.Join(
                '\n',
                [
                    "Session " + live.Id + " on target \"" + live.TargetName + "\": " + live.Catalog.Entries.Count.ToString(CultureInfo.InvariantCulture)
                        + " tools. Run one with call {tool, args}; tools {tool} shows a tool's arguments.",
                    .. CatalogLines(live),
                ]);
        }

        var found = live.Catalog.Find(tool) ?? throw UnknownTool(live, tool);
        return Tools.DescribeToolDetail(tool, found, live.Catalog.ReadOnly.Contains(tool));
    }

    /// <summary>Runs one catalog tool in the live session; what comes back, a result or a failure, passes the attempt's secret ledger.</summary>
    public async Task<CallToolResult> CallAsync(string? session, string name, JsonElement? args, CancellationToken cancellationToken)
    {
        var live = _sessions.Resolve(session);
        var tool = live.Catalog.Find(name) ?? throw UnknownTool(live, name);
        CallToolResult result;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, live.Abort.Token);
            // One call at a time: the page is one thing, and a second call would act on a screen the first is changing.
            await live.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                live.Calls++;
                Touch(live);
                var input = args is { ValueKind: JsonValueKind.Object } given ? given : Parse("{}");
                result = await Tools.InvokeToolAsync(name, tool, input, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                live.Gate.Release();
            }
        }
#pragma warning disable CA1031 // A failure is a result the agent can react to, never a protocol error.
        catch (Exception cause)
#pragma warning restore CA1031
        {
            result = Tools.ErrorResult(cause);
        }

        return RedactResult(result, text => live.Session.Redactor.Redact(_secrets.Redact(text)));
    }

    public void Dispose()
    {
        _shutdown.Dispose();
    }

    internal static CallToolResult RedactResult(CallToolResult result, Func<string, string> redact)
    {
        return new CallToolResult
        {
            Content = [.. result.Content.Select(block => block is TextContentBlock text ? new TextContentBlock { Text = redact(text.Text) } : block)],
            IsError = result.IsError,
        };
    }

    private static string? String(JsonElement args, string name)
    {
        return args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static ConfigurationException UnknownTool(LiveSession live, string name)
    {
        if (SessionCatalog.IsGrammarVerb(name))
        {
            return new ConfigurationException(
                "UNSUPPORTED_CAPABILITY",
                "tool \"" + name + "\" is not available in this session: the engine of target \"" + live.TargetName + "\" declares no such action, or the config declares no secret; tools lists what it can do");
        }

        return new ConfigurationException(
            "UNKNOWN_TOOL",
            "tool \"" + name + "\" is not available in this session; tools: " + string.Join(", ", live.Catalog.Entries.Select(pair => pair.Key)));
    }

    private static string DescribeEngine(IEngine engine) => engine.Platform + " " + engine.Version;

    /// <summary>Logs a line for the operator and the client, redacted with every secret value the process knows.</summary>
    private void Log(string level, string message) => _options.Log(level, _secrets.Redact(message));

    private async Task<string> OpenSessionAsync(string id, OpenSessionOptions options, bool headed, CancellationToken request)
    {
        // The config is claimed before it is read: a session on another config is refused while one is open.
        var configPath = _options.LocateConfig(options.Config);
        _sessions.ClaimConfig(id, configPath);
        var config = await _options.LoadConfig(configPath).ConfigureAwait(false);
        var secrets = config.Secrets.Names.Select(config.Secrets.Get).ToList();

        // Known to the process before anything can fail with one, so an open failure is redacted like any other text.
        _secrets.Add(secrets);
        var target = ResolveTarget(config, options.Target);
        var abort = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token, request);
        E2ESession? session = null;
        try
        {
            abort.Token.ThrowIfCancellationRequested();
            var engine = _options.CreateEngine(headed);
            session = await E2ESession.StartAsync(
                new E2ESessionOptions
                {
                    Engine = engine,
                    BaseUrl = target.App.Url,
                    ProjectRoot = config.ProjectRoot,
                    TargetName = target.Name,
                    Cache = null,
                    CacheEnabled = false,
                    CacheMode = CacheMode.Off,
                    TestTitle = "e2e mcp session " + id,
                    TestTimeout = _options.Ttl + CloseGrace,
                    LaunchTimeout = config.LaunchTimeout,
                    CleanupTimeout = config.CleanupTimeout,
                    ActionTimeout = config.ActionTimeout,
                    AssertionTimeout = config.AssertionTimeout,
                },
                abort.Token).ConfigureAwait(false);

            // Every configured secret is a step secret: type_secret can fill it, and every output shows its name.
            session.Remember(secrets);
            var live = new LiveSession(id, target.Name, configPath, headed, engine, session, abort);
            live.Catalog = SessionCatalog.Create(live, engine.Capabilities, secrets.Count > 0);
            var text = OpeningText(live, target, config, await FirstScreenAsync(live, target, abort.Token).ConfigureAwait(false));

            // A first screen that came back despite the cancel must not become a live session nobody asked for.
            abort.Token.ThrowIfCancellationRequested();
            live.Lifetime = new Timer(_ => EndOnItsOwn(live, "the session reached its time limit of " + Math.Round(_options.Ttl.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " minutes"), null, _options.Ttl, Timeout.InfiniteTimeSpan);
            _sessions.Activate(live);
            Touch(live);
            return text;
        }
        catch (Exception cause)
        {
            if (session is not null)
            {
                await DisposeQuietlyAsync(session).ConfigureAwait(false);
            }

            var cancelled = abort.IsCancellationRequested;
            abort.Dispose();
            if (cancelled && cause is OperationCanceledException)
            {
                throw new E2EException(EngineErrorCodes.Cancelled, "opening session " + id + " was cancelled", cause);
            }

            throw;
        }
    }

    private async Task<string> TeardownAsync(LiveSession live, string reason)
    {
        // Cancels the call in flight, if any, so it does not hold the engine the dispose is about to close.
        await live.Abort.CancelAsync().ConfigureAwait(false);
        live.StopTimers();
        var lines = new List<string> { "Session " + live.Id + " closed (" + reason + "); " + live.Calls.ToString(CultureInfo.InvariantCulture) + " tool calls ran." };
        try
        {
            live.Session.Complete();
            await live.Session.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed cleanup is reported to the agent, never thrown past the close.
        catch (Exception cause)
#pragma warning restore CA1031
        {
            lines.Add("Cleanup: " + Tools.CodedMessage(cause));
        }

        live.Abort.Dispose();

        // Redacted here, not only at the tool boundary: CloseAllAsync writes it to stderr.
        return live.Session.Redactor.Redact(_secrets.Redact(string.Join('\n', lines)));
    }

    private static async Task DisposeQuietlyAsync(E2ESession session)
    {
        try
        {
            session.Complete();
            await session.DisposeAsync().ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The open already failed; its own error is the one to report.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>Closes a live session that ended without a close_session: it sat idle, or reached its limit.</summary>
    private void EndOnItsOwn(LiveSession live, string why)
    {
        if (!_sessions.IsLive(live.Id))
        {
            return;
        }

        Log("warning", "session " + live.Id + " ended: " + why);
        _ = CloseAsync(why, live.Id).ContinueWith(
            task => Log("error", "closing session " + live.Id + " failed: " + Tools.CodedMessage(task.Exception!.GetBaseException())),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    private void Touch(LiveSession live)
    {
        // A call that finishes after the close must not leave a timer behind.
        if (live.Abort.IsCancellationRequested)
        {
            return;
        }

        live.Idle?.Dispose();
        live.Idle = new Timer(_ => EndOnItsOwn(live, "idle for " + Math.Round(_options.Idle.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " minutes"), null, _options.Idle, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Opens the app when the target declares a URL, then observes.</summary>
    private static async Task<string> FirstScreenAsync(LiveSession live, TargetConfig target, CancellationToken token)
    {
        var agent = live.Session.Agent;
        var opening = "";
        if (!string.IsNullOrEmpty(target.App.Url))
        {
            try
            {
                var navigated = await agent.RunToolAsync("navigate", Parse(JsonSerializer.Serialize(new { url = target.App.Url })), token).ConfigureAwait(false);
                if (!navigated.Succeeded)
                {
                    opening = "Opening " + target.App.Url + " failed: " + navigated.Content + "\nUse navigate once the app is reachable.\n";
                }
            }
            catch (E2EException cause)
            {
                opening = "Opening " + target.App.Url + " failed: " + Tools.CodedMessage(cause) + "\nUse navigate once the app is reachable.\n";
            }
        }

        var observed = await agent.RunToolAsync("observe", Parse("{}"), token).ConfigureAwait(false);
        return opening + (observed.Content.StartsWith("observed\n", StringComparison.Ordinal) ? observed.Content["observed\n".Length..] : observed.Content);
    }

    private string OpeningText(LiveSession live, TargetConfig target, E2EConfig config, string screen)
    {
        var lines = new List<string>
        {
            "Session " + live.Id + " open on target \"" + target.Name + "\" (platform " + target.Platform + ", engine " + DescribeEngine(live.Engine) + "), "
                + (live.Headed ? "headed" : "headless") + "; config " + live.ConfigPath + ".",
        };
        if (!string.IsNullOrEmpty(target.App.Url))
        {
            lines.Add("App: " + target.App.Url + ".");
        }

        var names = config.Secrets.Names.ToList();
        if (names.Count > 0)
        {
            lines.Add("Secrets: " + string.Join(", ", names.Select(name => "\"" + name + "\"")) + ". Fill one into any input with type_secret and its name; you never see the value.");
        }

        lines.Add("Pass session \"" + live.Id + "\" to every tools, call, and close_session; with several sessions open, a call without it fails.");
        lines.Add("Tools (run one with call {tool, args}; tools {tool} shows a tool's arguments):");
        lines.AddRange(CatalogLines(live));
        lines.Add("Node refs (ref=...) are valid only for the newest observation; every action shows the screen after it, and observe shows the whole screen. Call close_session when you are done.");
        return string.Join('\n', lines) + "\n\n" + screen;
    }

    private static IEnumerable<string> CatalogLines(LiveSession live) =>
        live.Catalog.Entries.Select(pair => Tools.CatalogLine(pair.Key, pair.Value, live.Catalog.ReadOnly.Contains(pair.Key)));

    private TargetConfig ResolveTarget(E2EConfig config, string? requested)
    {
        var name = requested ?? _options.DefaultTarget;
        if (name is not null && config.Targets.All(target => target.Name != name))
        {
            throw new ConfigurationException(
                "UNKNOWN_TARGET",
                "unknown target \"" + name + "\"; the config declares " + string.Join(", ", config.Targets.Select(target => "\"" + target.Name + "\"")));
        }

        return config.Target;
    }

    private McpToolSpec Redacting(McpToolSpec spec)
    {
        return new McpToolSpec
        {
            Name = spec.Name,
            Description = spec.Description,
            InputSchema = spec.InputSchema,
            ReadOnly = spec.ReadOnly,
            Call = async (args, token) =>
            {
                CallToolResult result;
                try
                {
                    result = await spec.Call(args, token).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A failure becomes a result the agent can react to, never a protocol error, and is redacted the same way.
                catch (Exception cause)
#pragma warning restore CA1031
                {
                    result = Tools.ErrorResult(cause);
                }

                return RedactResult(result, _secrets.Redact);
            },
        };
    }

    // --- the fixed tools ---

    private McpToolSpec OpenSpec()
    {
        return new McpToolSpec
        {
            Name = "open_session",
            Description = "Open a live session on one target of an e2e project: loads the config, boots the engine (a browser), opens the app URL, and returns the session id, the catalog of tools it can run, and the first observation. Several sessions can be open at once, one per agent, each with its own engine: pass the returned session id to every later call. Then act with call and look with call {tool: \"observe\"}.",
            InputSchema = OpenSchema,
            ReadOnly = false,
            Call = async (args, token) => Tools.TextResult(await OpenAsync(
                new OpenSessionOptions
                {
                    Target = String(args, "target"),
                    Config = String(args, "config"),
                    Headed = args.TryGetProperty("headed", out var headed) ? headed.GetBoolean() : null,
                },
                token).ConfigureAwait(false)),
        };
    }

    private McpToolSpec CatalogSpec()
    {
        return new McpToolSpec
        {
            Name = "tools",
            Description = "List the tools a session can run through call: observe, the grammar its engine honors (one tool per action the engine declares, type_secret when a secret is configured, and screenshot, which answers PIXEL_TAINTED once a secret has been filled), and locate. With tool, shows that tool's full description and the JSON Schema of its arguments.",
            InputSchema = CatalogSchema,
            ReadOnly = true,
            Call = (args, _) => Task.FromResult(Tools.TextResult(Catalog(String(args, "session"), String(args, "tool")))),
        };
    }

    private McpToolSpec CallSpec()
    {
        return new McpToolSpec
        {
            Name = "call",
            Description = "Run one tool of a session by name, with its arguments as an object: call {tool: \"tap\", args: {target: \"n42\"}, session: \"<id>\"}. The session's catalog (from open_session or tools) names the tools and their arguments. Actions report what changed on screen; node ids are valid only for the newest observation.",
            InputSchema = CallSchema,
            ReadOnly = false,
            Call = (args, token) => CallAsync(String(args, "session"), String(args, "tool")!, args.TryGetProperty("args", out var given) ? given : null, token),
        };
    }

    private McpToolSpec CloseSpec()
    {
        return new McpToolSpec
        {
            Name = "close_session",
            Description = "Close a live session: end the attempt, dispose the engine, and stop the browser the session started.",
            InputSchema = CloseSchema,
            ReadOnly = false,
            Call = async (args, _) => Tools.TextResult(await CloseAsync("closed by the agent", String(args, "session")).ConfigureAwait(false)),
        };
    }

    /// <summary>One open session: its attempt, its catalog, and the timers that end it.</summary>
    private sealed class LiveSession(string id, string targetName, string configPath, bool headed, IEngine engine, E2ESession session, CancellationTokenSource abort)
        : INamedSession, ISessionContext
    {
        public string Id { get; } = id;

        public string TargetName { get; } = targetName;

        public string ConfigPath { get; } = configPath;

        public bool Headed { get; } = headed;

        public IEngine Engine { get; } = engine;

        public E2ESession Session { get; } = session;

        /// <summary>Cancels the call in flight, and the attempt, when the session closes.</summary>
        public CancellationTokenSource Abort { get; } = abort;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public SessionCatalog Catalog { get; set; } = null!;

        public bool PixelsTainted { get; set; }

        public int Calls { get; set; }

        public Timer? Idle { get; set; }

        public Timer? Lifetime { get; set; }

        public void StopTimers()
        {
            Idle?.Dispose();
            Lifetime?.Dispose();
        }
    }
}
