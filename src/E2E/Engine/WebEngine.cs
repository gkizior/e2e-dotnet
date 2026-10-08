// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.RegularExpressions;
using E2E.Internal;
using Microsoft.Playwright;

namespace E2E.Engine;

/// <summary>A page size in CSS pixels.</summary>
public sealed record WebViewport(int Width, int Height)
{
    /// <summary>The size every attempt's page starts with unless the options say otherwise: 1280 by 720.</summary>
    public static WebViewport Default { get; } = new(1280, 720);
}

/// <summary>HTTP basic authentication the browser answers a <c>401</c> challenge with.</summary>
public sealed class WebBasicAuth
{
    public WebBasicAuth(string username, string password)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        Username = username;
        Password = password;
    }

    public WebBasicAuth(string username, Secret password)
        : this(username, (password ?? throw new ArgumentNullException(nameof(password))).Value)
    {
    }

    /// <summary>The user name. RFC 7617 does not allow <c>:</c> in one.</summary>
    public string Username { get; }

    internal string Password { get; }
}

/// <summary>
/// A script every document runs before the page's own, in every tab and
/// frame: JavaScript source, a file of it, or a function source the page
/// calls. A string converts to source.
/// </summary>
public sealed class WebInitScript
{
    private WebInitScript(string? source, string? path, string? function)
    {
        Source = source;
        Path = path;
        Function = function;
    }

    /// <summary>The JavaScript source, or <see langword="null"/> for a file or a function.</summary>
    public string? Source { get; }

    /// <summary>The file of JavaScript source, relative to the project root, or <see langword="null"/> for source or a function.</summary>
    public string? Path { get; }

    /// <summary>
    /// The source of a function the page calls, such as <c>() =&gt; { ... }</c>,
    /// or <see langword="null"/> for source or a file. It cannot close over test
    /// variables; <c>Browser.AddInitScriptAsync</c> passes it one JSON argument.
    /// </summary>
    public string? Function { get; }

    public static WebInitScript FromSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new WebInitScript(source, null, null);
    }

    public static WebInitScript FromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return new WebInitScript(null, path, null);
    }

    public static WebInitScript FromFunction(string function)
    {
        ArgumentNullException.ThrowIfNull(function);
        return new WebInitScript(null, null, function);
    }

    public static implicit operator WebInitScript(string source) => FromSource(source);

    /// <summary>Why this script cannot run, or <see langword="null"/>.</summary>
    internal string? Problem() => Path is { Length: 0 } ? "path must be a non-empty string" : null;

    /// <summary>
    /// The page source: the source itself, a function called with its JSON
    /// <paramref name="argument"/> (parsed in the page, since an object literal
    /// would turn an own <c>__proto__</c> key into the prototype), or the file's
    /// text with a <c>sourceURL</c>, so a stack trace in the page names it. A
    /// file that cannot be read throws what <paramref name="fail"/> makes of the problem.
    /// </summary>
    internal async Task<string> ReadAsync(string? argument, string projectRoot, Func<string, Exception, Exception> fail, CancellationToken cancellationToken)
    {
        if (Source is not null)
        {
            return Source;
        }

        if (Function is not null)
        {
            return "(" + Function + "\n)(" + (argument is null ? "" : "JSON.parse(" + System.Text.Json.JsonSerializer.Serialize(argument) + ")") + ");";
        }

        var file = Path!;
        try
        {
            file = System.IO.Path.GetFullPath(file, projectRoot);
            return await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false) + "\n//# sourceURL=" + file.ReplaceLineEndings("");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            var cause = ex is FileNotFoundException or DirectoryNotFoundException ? "ENOENT" : ex.Message;
            throw fail("path cannot be read: " + file + " (" + cause + ")", ex);
        }
    }
}

/// <summary>
/// Attach to a remote Chromium over CDP instead of launching one. Supplying
/// <see cref="ReconnectEndpoint"/> opts into a persistent context instead.
/// </summary>
public sealed class WebConnectOptions
{
    /// <summary>
    /// Resolves the CDP endpoint (a <c>ws://</c>, <c>wss://</c>, or <c>http://</c> DevTools URL).
    /// Called when each attempt starts, so a hosted endpoint provisioned per run can be used.
    /// With <see cref="ReconnectEndpoint"/>, each call must provision a fresh, dedicated browser.
    /// </summary>
    public required Func<CancellationToken, Task<string>> CdpEndpoint { get; init; }

    /// <summary>
    /// Opts into a dedicated persistent remote context. <see cref="CdpEndpoint"/> provisions
    /// a fresh browser at each attempt start; this resolver reconnects to that
    /// same browser after a transport drop. Called once before the next
    /// operation, within its budget. The original browser and page must survive.
    /// Dispatched operations are never retried. The host owns browser cleanup.
    /// Context replacement, headers, basicAuth, userAgent, locale, and timezoneId are unavailable in this mode.
    /// </summary>
    public Func<CancellationToken, Task<string>>? ReconnectEndpoint { get; init; }
}

/// <summary>Options of the browser engine: how it drives the app.</summary>
public sealed class WebEngineOptions
{
    /// <summary>Runs Chromium without a window. When unset, <c>E2E_HEADLESS=0</c> or <c>false</c> shows the window; headless otherwise.</summary>
    public bool? Headless { get; init; }

    /// <summary>
    /// Initial viewport of every attempt's page; default 1280 by 720. <see langword="null"/>
    /// emulates no size: the page fills the browser window, whatever size it has.
    /// </summary>
    public WebViewport? Viewport { get; init; } = WebViewport.Default;

    /// <summary>The attribute that carries an element's test id: what <c>GetByTestId</c> resolves and <see cref="SemanticNode.TestId"/> reports.</summary>
    public string TestIdAttribute { get; init; } = "data-testid";

    /// <summary>
    /// Headers added to every request bound for the app's host (the host of the
    /// session's base URL). Requests to any other host never carry them, so a
    /// secret header stays with the app it unlocks. Names are case-insensitive
    /// and replace a header the page already sends under the same name.
    /// Routing every request turns the browser's HTTP cache off.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Basic-auth credentials the browser answers a challenge with, wherever one is issued.</summary>
    public WebBasicAuth? BasicAuth { get; init; }

    /// <summary>The <c>User-Agent</c> the browser sends and <c>navigator.userAgent</c> reports. Defaults to the browser's own.</summary>
    public string? UserAgent { get; init; }

    /// <summary>
    /// The locale every attempt's context runs in, a BCP 47 tag such as <c>de-DE</c>:
    /// what <c>navigator.language</c>, <c>Intl</c> formatting, and the <c>Accept-Language</c>
    /// header report. Defaults to the browser's own. An <c>accept-language</c> entry in
    /// <see cref="Headers"/> beside it is <c>INVALID_CONFIG</c>.
    /// </summary>
    public string? Locale { get; init; }

    /// <summary>
    /// The IANA time zone every attempt's context runs in, such as <c>Europe/Berlin</c>:
    /// what <c>Date</c> and <c>Intl</c> resolve local time against. Defaults to the machine's.
    /// </summary>
    public string? TimezoneId { get; init; }

    /// <summary>Scripts every document runs before the page's own, in every tab and frame, in order.</summary>
    public IReadOnlyList<WebInitScript>? InitScripts { get; init; }

    /// <summary>Attach to a remote Chromium over CDP instead of launching a local one.</summary>
    public WebConnectOptions? Connect { get; init; }
}

/// <summary>
/// Browser engine for the web target. It drives Chromium through Playwright,
/// reads a semantic tree (role, name, text, test id, state), and performs the
/// same node actions the document engine does. Install browsers once with
/// <c>playwright.ps1 install chromium</c> from this project's build output.
/// </summary>
public sealed partial class WebEngine : IEngine
{
    public const string EngineVersion = "1.1.0";

    private const string SkipInstallVariable = "E2E_SKIP_BROWSER_INSTALL";

    private const string SkipBrowserGcVariable = "PLAYWRIGHT_SKIP_BROWSER_GC";

    // Playwright's install is a no-op when the build is already there; one run per mode serves every session in the process.
    private static readonly Lazy<Task<int>> HeadlessChromiumInstall = new(() => Task.Run(() => RunChromiumInstall(headed: false, Microsoft.Playwright.Program.Main)));
    private static readonly Lazy<Task<int>> HeadedChromiumInstall = new(() => Task.Run(() => RunChromiumInstall(headed: true, Microsoft.Playwright.Program.Main)));

    private readonly WebEngineOptions _options;
    private readonly bool _headless;

    /// <summary>The configured headers, names lower-cased so they replace the browser's own; null when none are set.</summary>
    private readonly Dictionary<string, string>? _siteHeaders;

    // The default contexts earlier attempts rode, so a cdpEndpoint that hands one back is refused.
    // Process-wide: a test fixture creates an engine for each attempt, retries included.
    private static readonly HashSet<string> UsedContexts = new(StringComparer.Ordinal);

    public WebEngine(bool? headless = null)
        : this(new WebEngineOptions { Headless = headless })
    {
    }

    public WebEngine(WebEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        _options = options;
        _siteHeaders = options.Headers is { Count: > 0 } headers
            ? headers.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value, StringComparer.Ordinal)
            : null;
        if (options.Headless is bool chosen)
        {
            _headless = chosen;
        }
        else
        {
            var env = Environment.GetEnvironmentVariable("E2E_HEADLESS");
            _headless = !string.Equals(env, "0", StringComparison.Ordinal) && !string.Equals(env, "false", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string Platform => "web";

    /// <summary>Attaches to a CDP endpoint; tests replace it to stand in for the remote browser.</summary>
    internal Func<IPlaywright, string, TimeSpan, CancellationToken, Task<IBrowser>> ConnectCdp { get; init; } = BrowserConnection.ConnectCdpAsync;

    /// <summary>Starts the Playwright driver; tests replace it so a fake remote needs no driver process.</summary>
    internal Func<Task<IPlaywright>> CreatePlaywright { get; init; } = Playwright.CreateAsync;

    /// <summary>The clock operation budgets read; tests replace it to control elapsed time.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    public string Version => EngineVersion;

    public EngineCapabilities Capabilities =>
        EngineCapabilities.Observation
        | EngineCapabilities.Actions
        | EngineCapabilities.Location
        | EngineCapabilities.Keyboard
        | EngineCapabilities.Scroll
        | EngineCapabilities.History;

    public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var initScripts = await ReadInitScriptsAsync(options.ProjectRoot, cancellationToken).ConfigureAwait(false);
            if (_options.Connect is null)
            {
                await EnsureChromiumAsync(!_headless, cancellationToken).ConfigureAwait(false);
            }

            var playwright = await CreatePlaywright().ConfigureAwait(false);
            var app = Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var parsed) ? parsed : null;
            var seams = new SessionSeams(Clock, ConnectCdp);
            if (_options.Connect is { ReconnectEndpoint: { } reconnect } persistent)
            {
                return await WebSession.StartPersistentAsync(
                    playwright,
                    new PersistentConnect(persistent.CdpEndpoint, reconnect, UsedContexts),
                    options.ActionTimeout,
                    _options,
                    initScripts,
                    context => InstallSiteHeadersAsync(context, app),
                    url => SiteHeadersFor(url, app),
                    seams,
                    cancellationToken).ConfigureAwait(false);
            }

            IBrowser browser;
            if (_options.Connect is { } connect)
            {
                var endpoint = await connect.CdpEndpoint(cancellationToken).ConfigureAwait(false);

                // As upstream, a connect that never answers is bounded by the browser launch budget.
                browser = await ConnectCdp(playwright, endpoint, E2EDefaults.LaunchTimeout, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _headless }).ConfigureAwait(false);
            }

            var session = new WebSession(playwright, browser, options.ActionTimeout, _options, initScripts, context => InstallSiteHeadersAsync(context, app), url => SiteHeadersFor(url, app), seams);
            await session.NewContextAsync().ConfigureAwait(false);
            return session;
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal) || ex.Message.Contains("browserType.launch", StringComparison.Ordinal))
        {
            throw new EngineException(
                "ENVIRONMENT_UNAVAILABLE",
                "Chromium is not installed for Playwright. Unset " + SkipInstallVariable + " to install it on launch, or from the build output run: playwright.ps1 install chromium",
                ex);
        }
        catch (Exception ex) when (WebErrors.IsPlaywright(ex))
        {
            throw new EngineException(EngineErrorCodes.EngineFailure, "browser launch failed: " + WebErrors.Message(ex), ex);
        }
    }

    /// <summary>True when <paramref name="url"/> is bound for the app's host, the only requests the configured headers ride.</summary>
    internal static bool IsAppRequest(string url, Uri app)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var target)
            && (target.Scheme == Uri.UriSchemeHttp || target.Scheme == Uri.UriSchemeHttps)
            && string.Equals(target.Host, app.Host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The <c>playwright install</c> arguments for Chromium: <c>--only-shell</c> on a headless run, which launches the headless shell.</summary>
    internal static string[] InstallArgs(bool headed)
    {
        return headed ? ["chromium"] : ["--only-shell", "chromium"];
    }

    /// <summary>
    /// Runs <paramref name="install"/> with the Chromium install arguments and <c>PLAYWRIGHT_SKIP_BROWSER_GC=1</c>
    /// unless the user set it, so the install keeps other tools' browsers.
    /// </summary>
    internal static int RunChromiumInstall(bool headed, Func<string[], int> install)
    {
        // Program.Main starts the driver with this process's environment, so the value is set on the process.
        Environment.SetEnvironmentVariable(SkipBrowserGcVariable, Environment.GetEnvironmentVariable(SkipBrowserGcVariable) ?? "1");
        return install(["install", .. InstallArgs(headed)]);
    }

    internal static async Task EnsureChromiumAsync(bool headed, CancellationToken cancellationToken)
    {
        var skip = Environment.GetEnvironmentVariable(SkipInstallVariable);
        if (string.Equals(skip, "1", StringComparison.Ordinal) || string.Equals(skip, "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var install = headed ? HeadedChromiumInstall : HeadlessChromiumInstall;
        var exitCode = await install.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new EngineException(
                "ENVIRONMENT_UNAVAILABLE",
                "Playwright could not install Chromium (exit code " + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "). Install it yourself and set " + SkipInstallVariable + "=1 to skip this step.");
        }
    }

    private static void Validate(WebEngineOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TestIdAttribute))
        {
            throw new EngineException("INVALID_CONFIG", "testIdAttribute must name an attribute.");
        }

        if (options.Viewport is { } viewport && (viewport.Width <= 0 || viewport.Height <= 0))
        {
            throw new EngineException("INVALID_CONFIG", "viewport width and height must be positive.");
        }

        if (options.BasicAuth is { } auth && auth.Username.Contains(':', StringComparison.Ordinal))
        {
            throw new EngineException("INVALID_CONFIG", "basicAuth username cannot contain ':'.");
        }

        if (options.UserAgent is not null && options.Headers is { } headers
            && headers.Keys.Any(name => string.Equals(name, "user-agent", StringComparison.OrdinalIgnoreCase)))
        {
            throw new EngineException("INVALID_CONFIG", "Set the user agent with userAgent or a user-agent header, not both.");
        }

        if (options.Locale is { } locale)
        {
            ValidateLocale(locale, options.Headers);
        }

        if (options.TimezoneId is { } timezoneId && !IsTimeZone(timezoneId))
        {
            throw new EngineException("INVALID_CONFIG", "timezoneId must be an IANA time zone such as \"Europe/Berlin\", got \"" + timezoneId + "\".");
        }

        for (var index = 0; index < (options.InitScripts?.Count ?? 0); index++)
        {
            var script = options.InitScripts![index];
            if ((script is null ? "must be a string of source, a { path }, or a function, got null" : script.Problem()) is { } problem)
            {
                throw new EngineException("INVALID_CONFIG", InitScriptAt(index) + problem + ".");
            }
        }

        // The options applied when the engine creates a browser context, which a persistent context never is.
        if (options.Connect?.ReconnectEndpoint is not null)
        {
            var creationOptions = new (string Name, bool Set)[]
            {
                ("headers", options.Headers is not null),
                ("basicAuth", options.BasicAuth is not null),
                ("userAgent", options.UserAgent is not null),
                ("locale", options.Locale is not null),
                ("timezoneId", options.TimezoneId is not null),
            }.Where(option => option.Set).Select(option => option.Name).ToList();
            if (creationOptions.Count > 0)
            {
                throw new EngineException(
                    "INVALID_CONFIG",
                    "connect.reconnectEndpoint uses a persistent context; " + string.Join(", ", creationOptions) + (creationOptions.Count == 1 ? " requires" : " require") + " a newly created context");
            }
        }
    }

    private static string InitScriptAt(int index) => "initScripts[" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + "] ";

    // The configured init scripts as page source, read before the browser
    // launches, so a missing file fails the attempt with nothing to close.
    private async Task<List<string>> ReadInitScriptsAsync(string? projectRoot, CancellationToken cancellationToken)
    {
        var sources = new List<string>();
        var scripts = _options.InitScripts ?? [];
        for (var index = 0; index < scripts.Count; index++)
        {
            var at = InitScriptAt(index);
            var root = System.IO.Path.GetFullPath(string.IsNullOrEmpty(projectRoot) ? Directory.GetCurrentDirectory() : projectRoot);
            sources.Add(await scripts[index].ReadAsync(null, root, (message, cause) => new EngineException("INVALID_CONFIG", at + message, cause), cancellationToken).ConfigureAwait(false));
        }

        return sources;
    }

    /// <summary>The live page and context behind a web session, for tests that drive the browser out of band; null for any other session.</summary>
    internal static LiveSurface? SurfaceOf(IEngineSession session) =>
        session is WebSession web ? new LiveSurface(() => web.LivePage, () => web.LiveContext) : null;

    // Refuses a locale that is not a BCP 47 tag, and one an accept-language
    // header would override on the app's host while navigator.language kept
    // reporting it.
    private static void ValidateLocale(string locale, IReadOnlyDictionary<string, string>? headers)
    {
        if (LanguageTagPattern().Match(locale) is not { Success: true } tag
            || string.Equals(tag.Groups["language"].Value, "und", StringComparison.OrdinalIgnoreCase))
        {
            // "und" (undetermined) parses, but Chromium refuses it at context creation.
            throw new EngineException("INVALID_CONFIG", "locale must be a BCP 47 language tag such as \"de-DE\", got \"" + locale + "\".");
        }

        if (headers is not null && headers.Keys.Any(name => string.Equals(name, "accept-language", StringComparison.OrdinalIgnoreCase)))
        {
            throw new EngineException("INVALID_CONFIG", "locale and an accept-language header in headers conflict; set locale only.");
        }
    }

    // True when the name is an IANA time zone spelled with its own case. The
    // runtime lookup ignores case, and Chromium refuses a miscased name
    // (europe/berlin), so the spelling must also be one ICU knows or a tz
    // database file. Links (US/Eastern, GMT) are both.
    private static bool IsTimeZone(string timezoneId)
    {
        return TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var zone)
            && zone.HasIanaId
            && (TimeZoneInfo.TryConvertIanaIdToWindowsId(timezoneId, out _) || IsZoneInfoName(timezoneId));
    }

    // True when each segment of the name is an entry of the tz database
    // directory the runtime reads on Unix, matched with its own case. ICU
    // misses a few zones (EST5EDT), and is absent in invariant globalization.
    private static bool IsZoneInfoName(string timezoneId)
    {
        var directory = Environment.GetEnvironmentVariable("TZDIR") is { Length: > 0 } tzdir ? tzdir : "/usr/share/zoneinfo";
        var caseSensitive = new EnumerationOptions { MatchCasing = MatchCasing.CaseSensitive, MatchType = MatchType.Simple };
        foreach (var segment in timezoneId.Split('/'))
        {
            if (!Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory, segment, caseSensitive).Any())
            {
                return false;
            }

            directory = Path.Combine(directory, segment);
        }

        return true;
    }

    // A Unicode locale identifier, as Intl.Locale parses one: language, then
    // optional script, region, variants, extensions, and private use.
    [GeneratedRegex(@"^(?<language>[a-z]{2,3}|[a-z]{5,8})(-[a-z]{4})?(-([a-z]{2}|[0-9]{3}))?(-([a-z0-9]{5,8}|[0-9][a-z0-9]{3}))*(-[0-9a-wyz](-[a-z0-9]{2,8})+)*(-x(-[a-z0-9]{1,8})+)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LanguageTagPattern();

    // A clean context: the viewport the session holds now (setViewport
    // outlives restart and clearState), or no emulation when there is none.
    private static BrowserNewContextOptions ContextOptions(WebEngineOptions options, ViewportSize? viewport)
    {
        var context = new BrowserNewContextOptions
        {
            ViewportSize = viewport ?? ViewportSize.NoViewport,
            Locale = options.Locale,
            TimezoneId = options.TimezoneId,
        };
        if (options.UserAgent is not null)
        {
            context.UserAgent = options.UserAgent;
        }

        if (options.BasicAuth is { } auth)
        {
            context.HttpCredentials = new HttpCredentials { Username = auth.Username, Password = auth.Password };
        }

        return context;
    }

    /// <summary>
    /// The configured headers, names lower-cased, that a request to <paramref name="url"/>
    /// carries: all of them for the app's host, none for any other host or when there is no app.
    /// </summary>
    private IReadOnlyDictionary<string, string>? SiteHeadersFor(string url, Uri? app)
    {
        return app is not null && IsAppRequest(url, app) ? _siteHeaders : null;
    }

    /// <summary>
    /// Adds the configured headers to every request bound for the app's host, through a
    /// context route that falls back to the network. Registered before any test route,
    /// so it runs last: a test route's fallback reaches it, and a continue, which skips
    /// it, merges the same headers itself.
    /// </summary>
    private async Task InstallSiteHeadersAsync(IBrowserContext context, Uri? app)
    {
        if (_siteHeaders is not { } headers || app is null)
        {
            return;
        }

        await context.RouteAsync(url => IsAppRequest(url, app), async route =>
        {
            var merged = new Dictionary<string, string>(route.Request.Headers, StringComparer.Ordinal);
            foreach (var (name, value) in headers)
            {
                merged[name] = value;
            }

            try
            {
                await route.FallbackAsync(new RouteFallbackOptions { Headers = merged }).ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The page closed under the request; nothing waits for it.
            }
        }).ConfigureAwait(false);
    }

    private sealed class WebSession : IBrowserSession
    {
        private readonly IPlaywright _playwright;
        private readonly TimeSpan _actionTimeout;
        private readonly string _testIdAttribute;
        private readonly WebEngineOptions _options;
        private readonly Func<IBrowserContext, Task> _setUpContext;
        private readonly Func<string, IReadOnlyDictionary<string, string>?> _siteHeaders;
        private readonly SessionSeams _seams;
        private readonly PersistentConnect? _persistent;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Lock _recoveryLock = new();

        // When an operation that reconnected first began: its budget, which every page call in it shares, started then.
        private readonly AsyncLocal<long?> _operationStart = new();

        // Attempt-scoped routes, registered on the context so they cover every page,
        // and registered again on each context ClearStateAsync opens or a reconnect attaches.
        private readonly List<(Func<string, bool> Matches, Func<IRoute, Task> PlaywrightHandler, Func<IBrowserRoute, Task> Handler)> _routes = [];

        // The attempt's init scripts, configured then added, applied to each context the session opens.
        private readonly List<string> _initScripts;
        private IBrowserContext? _context;
        private Exception? _pending;
        private IPage? _page;
        private ViewportSize? _viewport;
        private Dictionary<string, IFrame> _frames = new(StringComparer.Ordinal);
        private int _nextRef = 1;
        private IBrowser _browser;

        // Persistent recovery: the remote default context and page the attempt rides,
        // the shared reconnect in flight, the failure that ended the attempt, and
        // whether an observation was captured since the last reconnect.
        private string? _contextId;
        private TargetIdentity? _target;
        private Task? _recovery;
        private Exception? _failure;
        private bool _observed = true;
        private bool _firstPage = true;
        private bool _disposed;

        public WebSession(IPlaywright playwright, IBrowser browser, TimeSpan actionTimeout, WebEngineOptions options, List<string> initScripts, Func<IBrowserContext, Task> setUpContext, Func<string, IReadOnlyDictionary<string, string>?> siteHeaders, SessionSeams seams, PersistentConnect? persistent = null)
        {
            _playwright = playwright;
            _initScripts = initScripts;
            _browser = browser;
            _options = options;
            _setUpContext = setUpContext;
            _siteHeaders = siteHeaders;
            _seams = seams;
            _persistent = persistent;
            _testIdAttribute = options.TestIdAttribute;
            _viewport = options.Viewport is { } viewport ? new ViewportSize { Width = viewport.Width, Height = viewport.Height } : null;
            _actionTimeout = actionTimeout;
        }

        private IPage Page
        {
            get
            {
                ThrowPending();
                return _page ?? throw new EngineException(EngineErrorCodes.InvalidState, "no app page is open; call app.open() first");
            }
        }

        private float ActionMs => (float)_actionTimeout.TotalMilliseconds;

        internal IPage LivePage => Page;

        internal IBrowserContext LiveContext => RequireContext();

        public string Route => Routes.PathOf(Page.Url);

        public Task OpenAsync(string url, CancellationToken cancellationToken) => RunAsync("navigation", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (_page is null || _page.IsClosed)
                {
                    await NewPageAsync(RequireContext()).ConfigureAwait(false);
                }

                // The budget is the navigation's: opening the first page does not spend it.
                var budget = Budget("navigate to " + url, cancellationToken);
                await budget.WithinAsync(Page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = budget.PlaywrightTimeout,
                })).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "navigate to " + url);
            }
        });

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken) => RunAsync("observe", cancellationToken, async () =>
        {
            var budget = Budget("observe", cancellationToken);
            var walk = new FrameWalk();
            List<WebNode> roots;
            try
            {
                roots = await CollectAsync(Page.MainFrame, walk, budget).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.NavigationStaleOr(ex, "observe");
            }

            _frames = walk.Frames;
            _observed = true;
            return new Observation { Route = Route, Roots = roots.Select(ToNode).ToList(), Truncated = walk.Truncated, ScrollPosition = walk.Scroll };
        });

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => RunAsync("perform", cancellationToken, async () =>
        {
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(action);
            RequireObservation();
            var budget = Budget(action.GetType().Name.ToLowerInvariant(), cancellationToken);
            var frame = _frames.GetValueOrDefault(node.Ref) ?? Page.MainFrame;
            if (frame.IsDetached)
            {
                throw new EngineException(EngineErrorCodes.NodeStale, $"Node {node.Ref} is not on the page. Observe again.", retryable: true);
            }

            IJSHandle handle;
            try
            {
                handle = await budget.WithinAsync(frame.EvaluateHandleAsync(PageScript.Find, node.Ref)).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.NavigationStaleOr(ex, "locate " + node.Ref);
            }
            catch (EngineException ex) when (ex.Code == EngineErrorCodes.OperationTimeout)
            {
                // As upstream, the deadline cutting off any part of an action is the action's cut-off.
                throw WebErrors.ClassifyAction(ex, action);
            }

            // A frame's handle to a missing ref is a plain JSHandle, which
            // AsElement rejects instead of returning null.
            var element = handle as IElementHandle;
            if (element is null)
            {
                await handle.DisposeAsync().ConfigureAwait(false);
                throw new EngineException(EngineErrorCodes.NodeStale, $"Node {node.Ref} is not on the page. Observe again.", retryable: true);
            }

            await using var disposeElement = element.ConfigureAwait(false);
            try
            {
                await budget.WithinAsync(DispatchAsync(element, action, budget)).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex) || ex is EngineException { Code: EngineErrorCodes.OperationTimeout })
            {
                throw WebErrors.ClassifyAction(ex, action);
            }
        });

        public Task PressAsync(string key, CancellationToken cancellationToken) => RunAsync("keyboard.press", cancellationToken, () =>
        {
            RequireObservation();
            return InputAsync(() => Page.Keyboard.PressAsync(key), "press " + key, cancellationToken);
        });

        // Direct test input: it needs no observation, so it stays available after a reconnect.
        public Task KeyboardPressAsync(string key, CancellationToken cancellationToken) => RunAsync("keyboard.press", cancellationToken, () =>
            InputAsync(() => Page.Keyboard.PressAsync(key), "press " + key, cancellationToken));

        public Task BackAsync(CancellationToken cancellationToken) => RunAsync("back", cancellationToken, async () =>
        {
            var budget = Budget("back", cancellationToken);
            try
            {
                await budget.WithinAsync(Page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.Load, Timeout = budget.PlaywrightTimeout })).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "back");
            }
        });

        public Task ForwardAsync(CancellationToken cancellationToken) => RunAsync("forward", cancellationToken, async () =>
        {
            var budget = Budget("forward", cancellationToken);
            try
            {
                await budget.WithinAsync(Page.GoForwardAsync(new PageGoForwardOptions { WaitUntil = WaitUntilState.Load, Timeout = budget.PlaywrightTimeout })).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "forward");
            }
        });

        public Task ReloadAsync(CancellationToken cancellationToken) => RunAsync("reload", cancellationToken, async () =>
        {
            var budget = Budget("reload", cancellationToken);
            try
            {
                await budget.WithinAsync(Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = budget.PlaywrightTimeout })).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "reload");
            }
        });

        public Task RestartAsync(CancellationToken cancellationToken) => RunAsync("restart", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = RequireContext();
            _page = null;
            foreach (var page in context.Pages.ToList())
            {
                await page.CloseAsync().ConfigureAwait(false);
            }

            await NewPageAsync(context).ConfigureAwait(false);
        });

        public async Task ClearStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_persistent is not null)
            {
                throw new EngineException(
                    EngineErrorCodes.UnsupportedCapability,
                    "app.clearState() is unavailable with connect.reconnectEndpoint: it replaces the browser context, and the attempt rides one persistent context");
            }

            ThrowPending();
            var context = _context;
            _context = null;
            _page = null;
            if (context is not null)
            {
                await context.CloseAsync().ConfigureAwait(false);
            }

            await NewContextAsync().ConfigureAwait(false);
            await NewPageAsync(_context!).ConfigureAwait(false);
        }

        public Task<string> GetUrlAsync(CancellationToken cancellationToken) => RunAsync("url", cancellationToken, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Page.Url);
        });

        public Task<string> GetTitleAsync(CancellationToken cancellationToken) => RunAsync("title", cancellationToken, () =>
        {
            return Budget("title", cancellationToken).WithinAsync(Page.TitleAsync());
        });

        public Task<System.Text.Json.JsonElement?> EvaluateAsync(string expression, object? arg, bool hasArg, CancellationToken cancellationToken) => RunAsync("evaluate", cancellationToken, async () =>
        {
            var budget = Budget("evaluate", cancellationToken);
            try
            {
                var call = hasArg
                    ? Page.EvaluateAsync<System.Text.Json.JsonElement?>(expression, arg)
                    : Page.EvaluateAsync<System.Text.Json.JsonElement?>(expression);
                return await budget.WithinAsync(call).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                throw new TestException("EVALUATE_FAILED", ex.Message, ex);
            }
        });

        public Task<BrowserResponse> WaitForResponseAsync(Func<string, bool> matches, TimeSpan timeout, CancellationToken cancellationToken) => RunAsync("waitForResponse", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            IResponse response;
            try
            {
                response = await Page.WaitForResponseAsync(
                    candidate => matches(candidate.Url),
                    new PageWaitForResponseOptions { Timeout = (float)timeout.TotalMilliseconds }).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "waitForResponse");
            }

            // Started now, while the browser still holds the body, and awaited
            // only by the caller's body read on a budget of its own: the timeout
            // bounds the match, never a body still streaming in behind headers
            // that already arrived.
            return new BrowserResponse
            {
                Url = response.Url,
                Status = response.Status,
                Headers = response.Headers,
                Body = ReadBodyAsync(response),
            };
        });

        public Task RouteAsync(Func<string, bool> matches, Func<IBrowserRoute, Task> handler, CancellationToken cancellationToken) => RunAsync("route", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = RequireContext();
            Func<IRoute, Task> playwrightHandler = route => HandleRouteAsync(route, handler);
            await context.RouteAsync(matches, playwrightHandler).ConfigureAwait(false);
            _routes.Add((matches, playwrightHandler, handler));
        });

        public Task UnrouteAsync(Func<IBrowserRoute, Task> handler, CancellationToken cancellationToken) => RunAsync("unroute", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = RequireContext();
            var index = _routes.FindIndex(stored => stored.Handler == handler);
            if (index == -1)
            {
                return;
            }

            var (matches, playwrightHandler, _) = _routes[index];
            _routes.RemoveAt(index);
            await context.UnrouteAsync(matches, playwrightHandler).ConfigureAwait(false);
        });

        public Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken) => RunAsync<IReadOnlyList<BrowserCookie>>("cookies", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cookies = await RequireContext().CookiesAsync().ConfigureAwait(false);
            return cookies.Select(cookie => new BrowserCookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Domain = cookie.Domain,
                Path = cookie.Path,
                Expires = cookie.Expires >= 0 ? (long)Math.Floor(cookie.Expires) : null,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure,
                SameSite = cookie.SameSite.ToString(),
            }).ToList();
        });

        public Task SetCookiesAsync(IReadOnlyList<BrowserCookie> cookies, CancellationToken cancellationToken) => RunAsync("cookies", cancellationToken, () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(cookies);
            return RequireContext().AddCookiesAsync(cookies.Select(cookie => new Cookie
            {
                Name = cookie.Name,
                Value = cookie.Value,
                Url = cookie.Url,
                Domain = cookie.Url is null ? cookie.Domain : null,
                Path = cookie.Url is null ? cookie.Path ?? "/" : null,
                Expires = cookie.Expires,
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure,
                SameSite = cookie.SameSite switch
                {
                    null => null,
                    "Strict" => SameSiteAttribute.Strict,
                    "Lax" => SameSiteAttribute.Lax,
                    _ => SameSiteAttribute.None,
                },
            }));
        });

        public Task SetViewportAsync(int width, int height, CancellationToken cancellationToken) => RunAsync("setViewport", cancellationToken, async () =>
        {
            var budget = Budget("setViewport", cancellationToken);
            _viewport = new ViewportSize { Width = width, Height = height };
            if (_page is not null)
            {
                await budget.WithinAsync(_page.SetViewportSizeAsync(width, height)).ConfigureAwait(false);
            }
        });

        public Task AddInitScriptAsync(string source, CancellationToken cancellationToken) => RunAsync("addInitScript", cancellationToken, async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = RequireContext();
            await context.AddInitScriptAsync(source).ConfigureAwait(false);
            _initScripts.Add(source);
        });

        public Task KeyboardTypeAsync(string text, CancellationToken cancellationToken) => RunAsync("keyboard.type", cancellationToken, () =>
            InputAsync(() => Page.Keyboard.TypeAsync(text), "keyboard.type", cancellationToken));

        public Task MouseMoveAsync(float x, float y, CancellationToken cancellationToken) => RunAsync("mouse.move", cancellationToken, () =>
            InputAsync(() => Page.Mouse.MoveAsync(x, y), "mouse.move", cancellationToken));

        public Task MouseWheelAsync(float deltaX, float deltaY, CancellationToken cancellationToken) => RunAsync("mouse.wheel", cancellationToken, () =>
            InputAsync(() => Page.Mouse.WheelAsync(deltaX, deltaY), "mouse.wheel", cancellationToken));

        public Task MouseDownAsync(CancellationToken cancellationToken) => RunAsync("mouse.down", cancellationToken, () =>
            InputAsync(() => Page.Mouse.DownAsync(), "mouse.down", cancellationToken));

        public Task MouseUpAsync(CancellationToken cancellationToken) => RunAsync("mouse.up", cancellationToken, () =>
            InputAsync(() => Page.Mouse.UpAsync(), "mouse.up", cancellationToken));

        /// <summary>Opens a clean context at the current viewport. Its first page opens on the first navigation.</summary>
        public async Task NewContextAsync()
        {
            var context = await _browser.NewContextAsync(ContextOptions(_options, _viewport)).ConfigureAwait(false);
            await ConfigureAsync(context).ConfigureAwait(false);
            _context = context;
        }

        public Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken) => RunAsync("scroll", cancellationToken, async () =>
        {
            RequireObservation();
            var budget = Budget("scroll", cancellationToken);
            try
            {
                await budget.WithinAsync(Page.EvaluateAsync(PageScript.ScrollViewport, Direction(direction))).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.NavigationStaleOr(ex, "scroll");
            }
            catch (EngineException ex) when (ex.Code == EngineErrorCodes.OperationTimeout)
            {
                // As upstream's viewport swipe, a scroll the deadline cut off may have moved the page.
                throw WebErrors.ClassifyAction(ex, new LocatorAction.Swipe(direction));
            }
        });

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            // Late recovery and page work can only reach this retired session.
            _disposed = true;
            await _lifetime.CancelAsync().ConfigureAwait(false);
            try
            {
                if (_recovery is { } recovery)
                {
                    await recovery.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }

                // An ordinary context is engine-owned; a persistent browser belongs to the host, so only the connection closes.
                if (_context is not null && _persistent is null)
                {
                    await _context.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await _browser.CloseAsync().ConfigureAwait(false);
                _playwright.Dispose();
                _lifetime.Dispose();
            }
        }

        /// <summary>
        /// Reads one frame's document, then each child frame's into the iframe
        /// node that owns it, sharing one node budget across all of them. A
        /// frame that detaches or navigates mid-read is left empty.
        /// </summary>
        private async Task<List<WebNode>> CollectAsync(IFrame frame, FrameWalk walk, OperationBudget budget)
        {
            if (walk.Remaining <= 0)
            {
                walk.Truncated = true;
                return [];
            }

            string json;
            try
            {
                json = await budget.WithinAsync(frame.EvaluateAsync<string>(PageScript.Collect, new { seed = _nextRef, max = walk.Remaining, testIdAttribute = _testIdAttribute })).ConfigureAwait(false);
            }
            catch (PlaywrightException) when (frame != _page?.MainFrame)
            {
                return [];
            }

            var dto = System.Text.Json.JsonSerializer.Deserialize<WebObservation>(json, JsonDefaults.Options);
            if (dto is null)
            {
                return [];
            }

            _nextRef = dto.Next;
            walk.Remaining -= dto.Count;
            walk.Truncated |= dto.Truncated;
            if (frame == Page.MainFrame)
            {
                walk.Scroll = dto.Scroll;
            }

            var roots = dto.Roots ?? [];
            var owners = new Dictionary<string, WebNode>(StringComparer.Ordinal);
            Index(roots, frame, walk, owners);
            foreach (var child in frame.ChildFrames)
            {
                var owner = await budget.WithinAsync(OwnerOfAsync(child)).ConfigureAwait(false);
                if (owner is not null && owners.TryGetValue(owner, out var node))
                {
                    node.Children = await CollectAsync(child, walk, budget).ConfigureAwait(false);
                }
            }

            return roots;
        }

        /// <summary>One operation's budget: the action timeout, shared by every page call in the operation.</summary>
        private OperationBudget Budget(string label, CancellationToken cancellationToken) =>
            new(_actionTimeout, label, cancellationToken, _seams.Clock, _operationStart.Value);

        /// <summary>Sends one keystroke or pointer event, with no element behind it, within its budget, and classifies its failure.</summary>
        private async Task InputAsync(Func<Task> input, string label, CancellationToken cancellationToken)
        {
            var budget = Budget(label, cancellationToken);
            try
            {
                await budget.WithinAsync(input()).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex) || ex is EngineException { Code: EngineErrorCodes.OperationTimeout })
            {
                throw WebErrors.ClassifyInput(ex, label);
            }
        }

        /// <summary>Sends one action to the element, each Playwright call within what is left of the operation's budget.</summary>
        private async Task DispatchAsync(IElementHandle element, LocatorAction action, OperationBudget budget)
        {
            switch (action)
            {
                case LocatorAction.Tap:
                    await element.ClickAsync(new ElementHandleClickOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.DoubleTap:
                    await element.DblClickAsync(new ElementHandleDblClickOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Fill fill:
                    await element.FillAsync(fill.Value, new ElementHandleFillOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Press press:
                    await element.PressAsync(press.Key, new ElementHandlePressOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.PressSequentially typed:
                    await element.FocusAsync().ConfigureAwait(false);

                    // A focus the deadline cut off must not type later, into whatever holds focus then.
                    budget.ThrowIfExpired();
                    await Page.Keyboard.TypeAsync(typed.Text, new KeyboardTypeOptions
                    {
                        Delay = typed.Delay is TimeSpan delay ? (float)delay.TotalMilliseconds : null,
                    }).ConfigureAwait(false);
                    break;
                case LocatorAction.Select select:
                    await element.SelectOptionAsync(select.Value, new ElementHandleSelectOptionOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Check:
                    await SetCheckedAsync(element, true, budget).ConfigureAwait(false);
                    break;
                case LocatorAction.Uncheck:
                    await SetCheckedAsync(element, false, budget).ConfigureAwait(false);
                    break;
                case LocatorAction.Clear:
                    await element.FillAsync("", new ElementHandleFillOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Focus:
                    await element.FocusAsync().ConfigureAwait(false);
                    break;
                case LocatorAction.ScrollIntoView:
                    await element.ScrollIntoViewIfNeededAsync(new ElementHandleScrollIntoViewIfNeededOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Swipe swipe:
                    await element.EvaluateAsync(PageScript.ScrollElement, Direction(swipe.Direction)).ConfigureAwait(false);
                    break;
                default:
                    throw new EngineException(EngineErrorCodes.UnsupportedCapability, "Web engine cannot perform " + action.GetType().Name + ".");
            }
        }

        private static async Task<string?> OwnerOfAsync(IFrame frame)
        {
            try
            {
                var element = await frame.FrameElementAsync().ConfigureAwait(false);
                await using var dispose = element.ConfigureAwait(false);
                return await element.EvaluateAsync<string?>(PageScript.RefOf).ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                return null;
            }
        }

        private static void Index(List<WebNode> nodes, IFrame frame, FrameWalk walk, Dictionary<string, WebNode> owners)
        {
            foreach (var node in nodes)
            {
                walk.Frames[node.Ref] = frame;
                if (node.Frame)
                {
                    owners[node.Ref] = node;
                }

                Index(node.Children ?? [], frame, walk, owners);
            }
        }

        private async Task NewPageAsync(IBrowserContext context)
        {
            var page = await NextPageAsync(context).ConfigureAwait(false);
            page.SetDefaultTimeout(ActionMs);
            if (_viewport is { } viewport)
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height).ConfigureAwait(false);
            }

            if (_persistent is not null)
            {
                var target = await TargetIdentityAsync(page).ConfigureAwait(false);

                // An identity that arrives after the attempt ended belongs to no page of the next one.
                if (_disposed)
                {
                    throw new EngineException(EngineErrorCodes.NodeStale, "the connection changed while reading the screen", retryable: true);
                }

                _target = target;
            }

            _page = page;
        }

        /// <summary>
        /// The page the attempt shows next. A persistent browser is fresh for the
        /// attempt, so its own first tab serves as the attempt's first page instead
        /// of a second tab beside it, which a hosted browser's live view would show
        /// behind the test's. The tab is navigated to <c>about:blank</c> first, so its
        /// document runs the context's init scripts as a new tab's does. Every later
        /// page is a new tab.
        /// </summary>
        private async Task<IPage> NextPageAsync(IBrowserContext context)
        {
            if (_firstPage && _persistent is not null && context.Pages.FirstOrDefault(page => !page.IsClosed) is { } initial)
            {
                // A navigation that failed leaves the tab to the next attempt at opening the first page.
                await initial.GotoAsync("about:blank", new PageGotoOptions { Timeout = ActionMs }).ConfigureAwait(false);
                _firstPage = false;
                return initial;
            }

            _firstPage = false;
            return await context.NewPageAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Installs what every context of the attempt carries: the closed-root hook, the
        /// init scripts configured then added, the site headers, and the test's routes.
        /// </summary>
        private async Task ConfigureAsync(IBrowserContext context)
        {
            await context.AddInitScriptAsync(PageScript.RecordClosedShadowRoots).ConfigureAwait(false);
            foreach (var script in _initScripts)
            {
                await context.AddInitScriptAsync(script).ConfigureAwait(false);
            }

            await _setUpContext(context).ConfigureAwait(false);
            foreach (var (matches, playwrightHandler, _) in _routes)
            {
                await context.RouteAsync(matches, playwrightHandler).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Opens an attempt on a fresh remote browser provisioned by <c>cdpEndpoint</c>,
        /// riding its default context. A browser an earlier attempt rode is refused.
        /// The first page opens on the first navigation.
        /// </summary>
        public static async Task<WebSession> StartPersistentAsync(
            IPlaywright playwright,
            PersistentConnect persistent,
            TimeSpan actionTimeout,
            WebEngineOptions options,
            List<string> initScripts,
            Func<IBrowserContext, Task> setUpContext,
            Func<string, IReadOnlyDictionary<string, string>?> siteHeaders,
            SessionSeams seams,
            CancellationToken cancellationToken)
        {
            SessionBinding binding;
            try
            {
                // As upstream, attaching a persistent attempt has the browser launch budget.
                binding = await AttachWithinAsync(playwright, seams, persistent.Provision, E2EDefaults.LaunchTimeout, seams.Clock.GetTimestamp(), null, "connection", cancellationToken, CancellationToken.None, static _ => Task.CompletedTask).ConfigureAwait(false);
            }
            catch
            {
                playwright.Dispose();
                throw;
            }

            var session = new WebSession(playwright, binding.Browser, actionTimeout, options, initScripts, setUpContext, siteHeaders, seams, persistent);
            try
            {
                if (!persistent.Claim(binding.ContextId))
                {
                    throw RecoveryFailed("cdpEndpoint reused a browser from a previous attempt; provision a fresh browser");
                }

                session._context = binding.Context;
                session._contextId = binding.ContextId;
                await session.ConfigureAsync(binding.Context).ConfigureAwait(false);
                return session;
            }
            catch
            {
                await session.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Observation-backed input needs evidence captured after a reconnect.</summary>
        private void RequireObservation()
        {
            if (!_observed)
            {
                throw new EngineException(EngineErrorCodes.NodeStale, "observe the screen again after CDP recovery before acting", retryable: true);
            }
        }

        private Task RunAsync(string label, CancellationToken cancellationToken, Func<Task> work) =>
            _persistent is null
                ? work()
                : RunPersistentAsync(label, cancellationToken, async () =>
                {
                    await work().ConfigureAwait(false);
                    return true;
                });

        /// <summary>
        /// Runs one operation. On a persistent context whose transport dropped, it
        /// first shares one reconnect, then dispatches once with the time that remains.
        /// An operation is never retried.
        /// </summary>
        private Task<T> RunAsync<T>(string label, CancellationToken cancellationToken, Func<Task<T>> work)
        {
            return _persistent is null ? work() : RunPersistentAsync(label, cancellationToken, work);
        }

        private async Task<T> RunPersistentAsync<T>(string label, CancellationToken cancellationToken, Func<Task<T>> work)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_failure is not null)
            {
                throw _failure;
            }

            if (_recovery is null && _browser.IsConnected)
            {
                return await work().ConfigureAwait(false);
            }

            var started = _seams.Clock.GetTimestamp();
            Task recovery;
            lock (_recoveryLock)
            {
                // Started on the pool, so the resolver, which is user code, never runs under the lock.
                recovery = _recovery ??= Task.Run(() => ReconnectAsync(label, started, cancellationToken), CancellationToken.None);
            }

            try
            {
                await recovery.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && _failure is not null)
            {
                // The reconnect this operation waited on was cancelled by the operation that started it.
                throw _failure;
            }

            lock (_recoveryLock)
            {
                if (_recovery == recovery)
                {
                    _recovery = null;
                }
            }

            // Recovery is the engine's own work, so it counts against the operation's budget.
            _operationStart.Value = started;
            return await work().ConfigureAwait(false);
        }

        /// <summary>
        /// Reconnects through <c>reconnectEndpoint</c> to the same browser and page,
        /// within the budget of the operation that found the transport dropped. Every
        /// observed ref is retired. A failure ends the attempt: a failed reconnect never
        /// quietly provisions another session.
        /// </summary>
        private async Task ReconnectAsync(string label, long started, CancellationToken cancellationToken)
        {
            var previous = (_contextId!, _target);
            var size = _page?.ViewportSize is { } current ? new ViewportSize { Width = current.Width, Height = current.Height } : _viewport;
            _observed = false;
            _frames = new(StringComparer.Ordinal);
            try
            {
                var binding = await AttachWithinAsync(_playwright, _seams, _persistent!.Reconnect, _actionTimeout, started, previous, label, cancellationToken, _lifetime.Token, async candidate =>
                {
                    await ConfigureAsync(candidate.Context).ConfigureAwait(false);
                    if (candidate.Page is { } page)
                    {
                        page.SetDefaultTimeout(ActionMs);
                        if (size is { } viewport)
                        {
                            await page.SetViewportSizeAsync(viewport.Width, viewport.Height).ConfigureAwait(false);
                        }
                    }
                }).ConfigureAwait(false);
                _browser = binding.Browser;
                _context = binding.Context;
                _page = binding.Page;
                _target = binding.Target;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
            {
                _failure = new EngineException(EngineErrorCodes.Cancelled, label + " cancelled", retryable: false);
                throw;
            }
            catch (Exception ex)
            {
                _failure = ex is EngineException ? ex : WebErrors.IsPlaywright(ex) ? WebErrors.Translate(ex, "connection") : ex;
                throw _failure;
            }
        }

        /// <summary>
        /// Attaches, then runs <paramref name="prepare"/> on the candidate, all within what is
        /// left of <paramref name="budget"/> since <paramref name="started"/>: a resolver, dial,
        /// or configuration still pending at the deadline is cancelled, and the operation fails
        /// with <c>OPERATION_TIMEOUT</c>. A candidate that fails or arrives late is closed.
        /// </summary>
        private static async Task<SessionBinding> AttachWithinAsync(
            IPlaywright playwright,
            SessionSeams seams,
            Func<CancellationToken, Task<string>> resolve,
            TimeSpan budget,
            long started,
            (string ContextId, TargetIdentity? Target)? previous,
            string label,
            CancellationToken cancellationToken,
            CancellationToken lifetime,
            Func<SessionBinding, Task> prepare)
        {
            TimeSpan Remaining() => budget - seams.Clock.GetElapsedTime(started);
            using var timer = new CancellationTokenSource(Remaining() > TimeSpan.Zero ? Remaining() : TimeSpan.Zero, seams.Clock);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime, timer.Token);
            var token = deadline.Token;
            async Task<SessionBinding> AttachAndPrepareAsync()
            {
                var candidate = await AttachPersistentAsync(playwright, seams, resolve, Remaining, previous, label, token).ConfigureAwait(false);
                try
                {
                    await prepare(candidate).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return candidate;
                }
                catch
                {
                    await CloseQuietlyAsync(candidate.Browser).ConfigureAwait(false);
                    throw;
                }
            }

            var attach = AttachAndPrepareAsync();
            try
            {
                return await attach.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A connection that arrives once the attach was abandoned is closed; a failure has no one to reach.
                _ = attach.ContinueWith(
                    static abandoned => abandoned.IsCompletedSuccessfully ? CloseQuietlyAsync(abandoned.Result.Browser) : Task.FromResult(abandoned.Exception),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
                if (cancellationToken.IsCancellationRequested || lifetime.IsCancellationRequested)
                {
                    throw;
                }

                throw new EngineException(EngineErrorCodes.OperationTimeout, label + " timed out", retryable: false);
            }
        }

        /// <summary>
        /// Owns an uncommitted connection until its identity is proven; a cancelled or
        /// late connection is closed. With <paramref name="previous"/>, the browser must
        /// be the one the attempt rode and must still hold its exact page.
        /// </summary>
        private static async Task<SessionBinding> AttachPersistentAsync(
            IPlaywright playwright,
            SessionSeams seams,
            Func<CancellationToken, Task<string>> resolve,
            Func<TimeSpan> remaining,
            (string ContextId, TargetIdentity? Target)? previous,
            string label,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var endpoint = await resolve(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw RecoveryFailed("the endpoint is empty");
            }

            // The resolver's time is spent from the dial's budget, as from the operation's.
            var left = remaining();
            if (left <= TimeSpan.Zero)
            {
                throw new EngineException(EngineErrorCodes.OperationTimeout, label + " timed out", retryable: false);
            }

            var browser = await seams.ConnectCdp(playwright, endpoint, left, token).ConfigureAwait(false);

            // A connection whose owner gave up is detached at once, not when its next read returns.
            using var detach = token.Register(() => _ = CloseQuietlyAsync(browser));
            try
            {
                token.ThrowIfCancellationRequested();
                var session = await browser.NewBrowserCDPSessionAsync().ConfigureAwait(false);
                string? contextId;
                try
                {
                    var contexts = (await session.SendAsync("Target.getBrowserContexts").ConfigureAwait(false))!.Value;
                    if (contexts.GetProperty("browserContextIds").GetArrayLength() > 0)
                    {
                        throw RecoveryFailed("the remote browser must contain only its dedicated default context");
                    }

                    contextId = contexts.TryGetProperty("defaultBrowserContextId", out var id) ? id.GetString() : null;
                }
                finally
                {
                    await DetachQuietlyAsync(session).ConfigureAwait(false);
                }

                token.ThrowIfCancellationRequested();
                var context = browser.Contexts.FirstOrDefault() ?? throw RecoveryFailed("the default context is missing");
                if (string.IsNullOrEmpty(contextId))
                {
                    throw RecoveryFailed("the default context identity is unavailable");
                }

                if (previous is { } prior && !string.Equals(contextId, prior.ContextId, StringComparison.Ordinal))
                {
                    throw RecoveryFailed("reconnectEndpoint returned a different browser");
                }

                if (previous?.Target is not { } original)
                {
                    token.ThrowIfCancellationRequested();
                    return new SessionBinding(browser, context, null, contextId, null);
                }

                foreach (var page in context.Pages)
                {
                    var target = await TargetIdentityAsync(page).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    if (!string.Equals(target.TargetId, original.TargetId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!string.Equals(target.BrowserContextId, original.BrowserContextId, StringComparison.Ordinal))
                    {
                        throw RecoveryFailed("the original page belongs to a different context");
                    }

                    await RequireShadowTrackingAsync(page, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    return new SessionBinding(browser, context, page, contextId, target);
                }

                throw RecoveryFailed("the original page no longer exists");
            }
            catch
            {
                await CloseQuietlyAsync(browser).ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Reads protocol identity, independent of URL, document contents, and tab order.</summary>
        private static async Task<TargetIdentity> TargetIdentityAsync(IPage page)
        {
            var session = await page.Context.NewCDPSessionAsync(page).ConfigureAwait(false);
            try
            {
                var info = (await session.SendAsync("Target.getTargetInfo").ConfigureAwait(false))!.Value.GetProperty("targetInfo");
                return new TargetIdentity(
                    info.GetProperty("targetId").GetString()!,
                    info.TryGetProperty("browserContextId", out var context) ? context.GetString() : null);
            }
            finally
            {
                await DetachQuietlyAsync(session).ConfigureAwait(false);
            }
        }

        /// <summary>A document that changed while disconnected never ran the closed-root hook, which observation needs.</summary>
        private static async Task RequireShadowTrackingAsync(IPage page, CancellationToken token)
        {
            foreach (var frame in page.Frames)
            {
                var tracked = await frame.EvaluateAsync<bool>(PageScript.TracksClosedShadowRoots).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!tracked)
                {
                    throw new EngineException(
                        EngineErrorCodes.EngineFailure,
                        "CDP recovery failed: closed shadow root tracking is unavailable after a document changed while disconnected; locators and secure-field masks need it",
                        retryable: false);
                }
            }
        }

        /// <summary>A failed reconnect must never quietly provision another session.</summary>
        private static EngineException RecoveryFailed(string detail) =>
            new(EngineErrorCodes.EngineFailure, "CDP recovery failed: " + detail, retryable: false);

        private static async Task CloseQuietlyAsync(IBrowser browser)
        {
            try
            {
                await browser.CloseAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The connection is already gone.
            }
        }

        private static async Task DetachQuietlyAsync(ICDPSession session)
        {
            try
            {
                await session.DetachAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The target or connection is already gone.
            }
        }

        private IBrowserContext RequireContext()
        {
            ThrowPending();
            return _context ?? throw new EngineException(EngineErrorCodes.InvalidState, "The browser has no context.");
        }

        /// <summary>
        /// Rethrows, once, the first error a route handler raised since the last
        /// operation. Nothing awaits a route handler, so its error fails the next
        /// operation that reaches the page or the context.
        /// </summary>
        private void ThrowPending()
        {
            if (Interlocked.Exchange(ref _pending, null) is { } pending)
            {
                ExceptionDispatchInfo.Throw(pending);
            }
        }

        /// <summary>
        /// Runs one handler on one request. The error is kept before an undecided
        /// request is aborted, so the step after the page saw the failure reports it.
        /// </summary>
        private async Task HandleRouteAsync(IRoute route, Func<IBrowserRoute, Task> handler)
        {
            var decision = new PlaywrightRoute(route, _siteHeaders);
            try
            {
                await handler(decision).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _pending, ex, null);
                if (!decision.Decided)
                {
                    await PlaywrightRoute.AbortQuietlyAsync(route).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Reads the body of a known response. A body the browser could not read
        /// faults with <c>ACTION_FAILED</c> naming the cause, never an empty string
        /// the server did not send. The browser reports the reason on the request
        /// (<c>net::ERR_CONTENT_LENGTH_MISMATCH</c> for a connection cut short of the
        /// declared length), and the error behind the read is the fallback, as for
        /// a redirect, whose body the browser never keeps.
        /// </summary>
        private static async Task<string> ReadBodyAsync(IResponse response)
        {
            try
            {
                return System.Text.Encoding.UTF8.GetString(await response.BodyAsync().ConfigureAwait(false));
            }
            catch (PlaywrightException ex)
            {
                var reason = response.Request.Failure ?? WebErrors.Message(ex);
                throw new TestException("ACTION_FAILED", "waitForResponse: response body could not be read: " + reason, ex);
            }
        }

        /// <summary>
        /// Sets a checkbox, switch, or radio to <paramref name="checked"/> with one click, as
        /// Playwright's <c>check</c> does, apart from the read after the click: a control
        /// that is gone by then (an app that swaps a picked radio for its selected
        /// view, or navigates on change) took the click, so the action is done, where
        /// Playwright reports it detached as if the click never happened. Whatever
        /// took its place is not read: the next observation or assertion shows it,
        /// as it does after a tap. The reads and the click share one element, so the
        /// state before and after the click is one control's.
        /// </summary>
        private static async Task SetCheckedAsync(IElementHandle element, bool @checked, OperationBudget budget)
        {
            var verb = @checked ? "check" : "uncheck";
            if (await element.IsCheckedAsync().ConfigureAwait(false) == @checked)
            {
                return;
            }

            if (!@checked && await element.EvaluateAsync<bool>(PageScript.IsRadio).ConfigureAwait(false))
            {
                throw new EngineException(EngineErrorCodes.NotActionable, "uncheck cannot clear a radio button; select another radio in its group", retryable: false);
            }

            await element.ClickAsync(new ElementHandleClickOptions { Timeout = budget.PlaywrightTimeout }).ConfigureAwait(false);
            bool after;
            try
            {
                after = await element.IsCheckedAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException ex) when (WebErrors.IsDetached(ex) || WebErrors.IsNavigationRace(ex))
            {
                return;
            }

            if (after != @checked)
            {
                throw new EngineException(EngineErrorCodes.NotActionable, verb + " clicked the control but its checked state did not change", retryable: false);
            }
        }

        private static string Direction(ScrollDirection direction)
        {
            return direction switch
            {
                ScrollDirection.Up => "up",
                ScrollDirection.Left => "left",
                ScrollDirection.Right => "right",
                _ => "down",
            };
        }

        private static SemanticNode ToNode(WebNode dto)
        {
            return new SemanticNode
            {
                Ref = dto.Ref,
                Role = dto.Role,
                Name = dto.Name,
                Text = dto.Text,
                Value = dto.Secure ? null : dto.Value,
                TestId = dto.TestId,
                Placeholder = dto.Placeholder,
                InputPurpose = dto.InputPurpose,
                Level = dto.Level,
                States = new NodeStates
                {
                    Checked = dto.Checked,
                    Disabled = dto.Disabled,
                    Expanded = dto.Expanded,
                    Focused = dto.Focused,
                    Hidden = dto.Hidden,
                    Pressed = dto.Pressed,
                    Secure = dto.Secure,
                    Selected = dto.Selected,
                },
                Attributes = dto.Attributes ?? new Dictionary<string, string>(StringComparer.Ordinal),
                Rect = dto.Rect is { } rect ? new BoundingBox(rect.X, rect.Y, rect.Width, rect.Height) : null,
                Children = dto.Children?.Select(ToNode).ToList() ?? [],
            };
        }
    }

    /// <summary>One intercepted request, decided through Playwright.</summary>
    private sealed class PlaywrightRoute : IBrowserRoute
    {
        private readonly IRoute _route;
        private readonly Func<string, IReadOnlyDictionary<string, string>?> _siteHeaders;

        public PlaywrightRoute(IRoute route, Func<string, IReadOnlyDictionary<string, string>?> siteHeaders)
        {
            _route = route;
            _siteHeaders = siteHeaders;
            var request = route.Request;
            Request = new WebRouteRequest
            {
                Url = request.Url,
                Method = request.Method,
                Headers = request.Headers,
                PostData = request.PostData,
            };
        }

        public WebRouteRequest Request { get; }

        /// <summary>Set the moment a decision starts, before it reaches Playwright.</summary>
        public bool Decided { get; private set; }

        public static async Task AbortQuietlyAsync(IRoute route)
        {
            try
            {
                await route.AbortAsync().ConfigureAwait(false);
            }
            catch (PlaywrightException)
            {
                // The request is already settled, or its page closed.
            }
        }

        public Task FulfillAsync(RouteFulfillResponse response) =>
            DecideAsync("route.fulfill", () => _route.FulfillAsync(new RouteFulfillOptions
            {
                Status = response.Status,
                Headers = response.Headers,
                ContentType = response.ContentType,
                Body = response.Body,
                Path = response.Path,
            }));

        public Task ContinueAsync(RouteContinueOverrides overrides)
        {
            // Continue skips every route registered before this one, the site-header
            // route included, so it merges those headers itself.
            var site = _siteHeaders(overrides.Url ?? Request.Url);
            Dictionary<string, string>? headers = null;
            if (overrides.Headers is not null || site is not null)
            {
                headers = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var (name, value) in overrides.Headers ?? Request.Headers)
                {
                    headers[name.ToLowerInvariant()] = value;
                }

                foreach (var (name, value) in site ?? Enumerable.Empty<KeyValuePair<string, string>>())
                {
                    headers[name] = value;
                }
            }

            return DecideAsync("route.continue", () => _route.ContinueAsync(new RouteContinueOptions
            {
                Url = overrides.Url,
                Method = overrides.Method,
                Headers = headers,
                PostData = overrides.PostData is null ? null : Encoding.UTF8.GetBytes(overrides.PostData),
            }));
        }

        public Task FallbackAsync() => DecideAsync("route.fallback", () => _route.FallbackAsync());

        public Task AbortAsync() => DecideAsync("route.abort", () => _route.AbortAsync());

        // A Playwright call that fails after the decision aborts the request rather
        // than leaving the page's request pending.
        private async Task DecideAsync(string label, Func<Task> decide)
        {
            Decided = true;
            try
            {
                await decide().ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                await AbortQuietlyAsync(_route).ConfigureAwait(false);
                throw WebErrors.Translate(ex, label);
            }
        }
    }

    /// <summary>What tests replace: the clock budgets read, and the CDP attach.</summary>
    private sealed record SessionSeams(TimeProvider Clock, Func<IPlaywright, string, TimeSpan, CancellationToken, Task<IBrowser>> ConnectCdp);

    /// <summary>The <c>connect</c> resolvers of a persistent context, and the default contexts earlier attempts rode.</summary>
    private sealed record PersistentConnect(
        Func<CancellationToken, Task<string>> Provision,
        Func<CancellationToken, Task<string>> Reconnect,
        HashSet<string> UsedContexts)
    {
        /// <summary>Claims a provisioned browser's default context for this attempt; false when an earlier attempt rode it.</summary>
        public bool Claim(string contextId)
        {
            lock (UsedContexts)
            {
                return UsedContexts.Add(contextId);
            }
        }
    }

    private sealed record TargetIdentity(string TargetId, string? BrowserContextId);

    private sealed record SessionBinding(IBrowser Browser, IBrowserContext Context, IPage? Page, string ContextId, TargetIdentity? Target);

    private sealed class FrameWalk
    {
        public int Remaining { get; set; } = ObservationLimits.Nodes;

        public bool Truncated { get; set; }

        public string? Scroll { get; set; }

        public Dictionary<string, IFrame> Frames { get; } = new(StringComparer.Ordinal);
    }

    private sealed class WebObservation
    {
        public int Next { get; set; }

        public int Count { get; set; }

        public bool Truncated { get; set; }

        public string? Scroll { get; set; }

        public List<WebNode>? Roots { get; set; }
    }

    private sealed class WebNode
    {
        public string Ref { get; set; } = "";

        public string? Role { get; set; }

        public string? Name { get; set; }

        public string? Text { get; set; }

        public string? Value { get; set; }

        public string? TestId { get; set; }

        public string? Placeholder { get; set; }

        public string? InputPurpose { get; set; }

        public int? Level { get; set; }

        public bool Disabled { get; set; }

        public bool Checked { get; set; }

        public bool Expanded { get; set; }

        public bool Selected { get; set; }

        public bool Pressed { get; set; }

        public bool Focused { get; set; }

        public bool Hidden { get; set; }

        public bool Secure { get; set; }

        public bool Frame { get; set; }

        public Dictionary<string, string>? Attributes { get; set; }

        public WebRect? Rect { get; set; }

        public List<WebNode>? Children { get; set; }
    }

    private sealed class WebRect
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }
}

/// <summary>The live page and context behind a web session. Each accessor reads the current binding, so it follows a reconnect.</summary>
internal sealed record LiveSurface(Func<IPage> Page, Func<IBrowserContext> Context);

/// <summary>
/// One web operation's budget, as upstream <c>withOperationDeadline</c>: one deadline
/// every page call in the operation shares. A call still pending at the deadline is
/// abandoned as <c>OPERATION_TIMEOUT</c> instead of holding the test until its own
/// timeout.
/// </summary>
internal sealed class OperationBudget
{
    /// <summary>
    /// How much sooner than the deadline Playwright's own timeout fires: its error names
    /// what blocked an action, or that the input was already dispatched, and this lead
    /// lets that answer arrive before the deadline does. The deadline is for the calls
    /// Playwright never answers: an evaluate, which takes no timeout, and input on a page
    /// whose renderer is stuck in a script. A budget shorter than twice the lead splits
    /// in half.
    /// </summary>
    private const double PlaywrightTimeoutLeadMs = 250;

    private readonly long _startedAt;
    private readonly TimeSpan _timeout;
    private readonly string _label;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeProvider _clock;

    // startedAt is when the operation began, on the clock, when engine work such as a reconnect already spent part of its budget.
    public OperationBudget(TimeSpan timeout, string label, CancellationToken cancellationToken, TimeProvider? clock = null, long? startedAt = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _clock = clock ?? TimeProvider.System;
        _startedAt = startedAt ?? _clock.GetTimestamp();
        _timeout = timeout;
        _label = label;
        _cancellationToken = cancellationToken;
    }

    /// <summary>The time left before Playwright's own timeout: the one to pass Playwright.</summary>
    public float PlaywrightTimeout
    {
        get
        {
            var left = Remaining().TotalMilliseconds;
            return (float)Math.Ceiling(left - Math.Min(PlaywrightTimeoutLeadMs, left / 2));
        }
    }

    /// <summary>Waits for <paramref name="call"/> until the deadline, then abandons it as <c>OPERATION_TIMEOUT</c>.</summary>
    public async Task WithinAsync(Task call)
    {
        try
        {
            await call.WaitAsync(Remaining(), _clock, _cancellationToken).ConfigureAwait(false);
        }
        catch (System.TimeoutException ex) when (ex != call.Exception?.InnerException)
        {
            // The deadline ran out, not Playwright's own timeout, which keeps its translation.
            throw Timeout();
        }
    }

    /// <inheritdoc cref="WithinAsync(Task)"/>
    public async Task<T> WithinAsync<T>(Task<T> call)
    {
        await WithinAsync((Task)call).ConfigureAwait(false);
        return await call.ConfigureAwait(false);
    }

    /// <summary>Throws <c>OPERATION_TIMEOUT</c> once the deadline has passed, before a call that takes no timeout of its own.</summary>
    public void ThrowIfExpired() => Remaining();

    private TimeSpan Remaining()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var left = _timeout - _clock.GetElapsedTime(_startedAt);
        return left > TimeSpan.Zero ? left : throw Timeout();
    }

    private EngineException Timeout() => new(EngineErrorCodes.OperationTimeout, _label + " timed out", retryable: false);
}

/// <summary>
/// Maps Playwright failures to stable engine codes, following upstream <c>@e2e-dev/web</c>
/// (<c>classifyActionError</c>, <c>translatePwError</c>, and <c>navigationStaleOr</c>).
/// </summary>
internal static partial class WebErrors
{
    /// <summary>Whether Playwright raised <paramref name="cause"/>. Playwright for .NET reports timeouts as <see cref="System.TimeoutException"/>.</summary>
    public static bool IsPlaywright(Exception cause)
    {
        return cause is PlaywrightException or System.TimeoutException;
    }

    /// <summary>A failure's message without terminal color sequences.</summary>
    public static string Message(Exception cause)
    {
        return AnsiPattern().Replace(cause.Message, "");
    }

    /// <summary>An unexpected failure: a timeout becomes <c>OPERATION_TIMEOUT</c>, anything else <c>ENGINE_FAILURE</c>.</summary>
    public static EngineException Translate(Exception cause, string label)
    {
        var text = label + ": " + Message(cause);
        return cause is System.TimeoutException
            ? new EngineException(EngineErrorCodes.OperationTimeout, text, cause)
            : new EngineException(EngineErrorCodes.EngineFailure, text, cause);
    }

    /// <summary>Whether Playwright's words for <paramref name="cause"/> say the element left the DOM.</summary>
    public static bool IsDetached(Exception cause)
    {
        return DetachedPattern().IsMatch(Message(cause));
    }

    /// <summary>Whether a Playwright failure describes a read that lost its document to a navigation.</summary>
    public static bool IsNavigationRace(Exception cause)
    {
        return NavigationRacePattern().IsMatch(Message(cause));
    }

    /// <summary>A read that lost its document to a navigation is a retryable <c>NODE_STALE</c>, so the caller observes the new document.</summary>
    public static EngineException NavigationStaleOr(Exception cause, string label)
    {
        if (IsNavigationRace(cause))
        {
            return new EngineException(EngineErrorCodes.NodeStale, label + ": " + Message(cause), retryable: true, cause);
        }

        return Translate(cause, label);
    }

    /// <summary>Classifies the failure of an input call with no element behind it: a keystroke or a pointer event.</summary>
    public static EngineException ClassifyInput(Exception cause, string label)
    {
        return InputCutOff(cause, label) ?? Translate(cause, label);
    }

    /// <summary>Classifies a failed locator action. A timeout after the input was dispatched may have committed.</summary>
    public static EngineException ClassifyAction(Exception rawCause, LocatorAction action)
    {
        var kind = action.GetType().Name.ToLowerInvariant();
        if (InputCutOff(rawCause, kind) is { } cut)
        {
            return cut;
        }

        var sensitive = action is LocatorAction.Fill { Sensitive: true };
        var text = Message(rawCause);
        if (action is LocatorAction.Fill fill && sensitive && fill.Value.Length > 0)
        {
            // The message can quote the value in any spelling or cut short.
            text = Redactor.ForValue(fill.Value).RedactFragments(text);
        }

        var cause = sensitive ? null : rawCause;
        // A strict mode violation is a located match that turned ambiguous: stale before any input, so the runner
        // locates again; possibly committed once the call log shows the input went out.
        if (StrictModePattern().IsMatch(text))
        {
            return PostDispatchPattern().IsMatch(text)
                ? new EngineException(EngineErrorCodes.ActionMayHaveCommitted, kind + " matched more than one element after its input was dispatched: " + text, retryable: false, cause)
                : new EngineException(EngineErrorCodes.NodeStale, text, retryable: true, cause);
        }

        if (DetachedPattern().IsMatch(text) || NavigationRacePattern().IsMatch(text))
        {
            return new EngineException(EngineErrorCodes.NodeStale, text, retryable: true, cause);
        }

        if (TimeoutPattern().IsMatch(text) || rawCause is System.TimeoutException)
        {
            if (PostDispatchPattern().IsMatch(text))
            {
                return new EngineException(EngineErrorCodes.ActionMayHaveCommitted, kind + " timed out after its input was dispatched: " + text, retryable: false, cause);
            }

            return new EngineException(EngineErrorCodes.NotActionable, kind + " did not become actionable in time: " + Summary(text), retryable: false, cause);
        }

        if (NotEditablePattern().IsMatch(text))
        {
            return new EngineException(EngineErrorCodes.NotActionable, text, retryable: false, cause);
        }

        return new EngineException(EngineErrorCodes.EngineFailure, text, retryable: false, cause);
    }

    // An input call the operation deadline cut off: Playwright never answered, so the input may have reached the page and must not be repeated blindly.
    private static EngineException? InputCutOff(Exception cause, string label)
    {
        return cause is EngineException { Code: EngineErrorCodes.OperationTimeout }
            ? new EngineException(EngineErrorCodes.ActionMayHaveCommitted, label + " timed out before the page answered; its input may have been dispatched", retryable: false, cause)
            : null;
    }

    // The headline and the last blocker the call log names; a log without a blocker is kept whole.
    private static string Summary(string text)
    {
        var lines = text.Split('\n');
        var blocker = lines.Skip(1)
            .Select(line => line.Trim())
            .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? line[2..] : line)
            .LastOrDefault(line => BlockerPattern().IsMatch(line));
        return blocker is null ? text : lines[0] + " " + blocker;
    }

    [GeneratedRegex(@"\u001b\[\d+(?:;\d+)*m")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex("strict mode violation", RegexOptions.IgnoreCase)]
    private static partial Regex StrictModePattern();

    [GeneratedRegex("element (is |was )?(detached|not attached)", RegexOptions.IgnoreCase)]
    private static partial Regex DetachedPattern();

    [GeneratedRegex("Timeout .*exceeded", RegexOptions.IgnoreCase)]
    private static partial Regex TimeoutPattern();

    // Only whole "- " call log lines count, so a locator or element text quoting these words never does.
    [GeneratedRegex(@"^\s*- (performing \w+ action|[\w ]+ action done|waiting for scheduled navigations to finish)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PostDispatchPattern();

    // A call log line naming what kept a node from being acted on: an element over it, or a state it never reached.
    [GeneratedRegex("^element is (?:not (?:visible|enabled|stable|editable)|outside of the viewport)$| intercepts pointer events$", RegexOptions.IgnoreCase)]
    private static partial Regex BlockerPattern();

    [GeneratedRegex("not an? <?(input|checkbox|radio|select)|not editable|not checkable", RegexOptions.IgnoreCase)]
    private static partial Regex NotEditablePattern();

    [GeneratedRegex("execution context was destroyed|because of a navigation|navigating and changing the content|frame was detached|frame got detached|node is detached from document", RegexOptions.IgnoreCase)]
    private static partial Regex NavigationRacePattern();
}
