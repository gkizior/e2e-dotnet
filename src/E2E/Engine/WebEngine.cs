// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

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

/// <summary>Attach to a remote Chromium over CDP instead of launching one.</summary>
public sealed class WebConnectOptions
{
    /// <summary>
    /// Resolves the CDP endpoint (a <c>ws://</c>, <c>wss://</c>, or <c>http://</c> DevTools URL).
    /// Called when each attempt starts, so a hosted endpoint provisioned per run can be used.
    /// </summary>
    public required Func<CancellationToken, Task<string>> CdpEndpoint { get; init; }
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

    public WebEngine(bool? headless = null)
        : this(new WebEngineOptions { Headless = headless })
    {
    }

    public WebEngine(WebEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);
        _options = options;
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
            if (_options.Connect is null)
            {
                await EnsureChromiumAsync(!_headless, cancellationToken).ConfigureAwait(false);
            }

            var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            IBrowser browser;
            if (_options.Connect is { } connect)
            {
                var endpoint = await connect.CdpEndpoint(cancellationToken).ConfigureAwait(false);
                browser = await playwright.Chromium.ConnectOverCDPAsync(endpoint).ConfigureAwait(false);
            }
            else
            {
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _headless }).ConfigureAwait(false);
            }

            var session = new WebSession(playwright, browser, options.ActionTimeout, _options, context => InstallSiteHeadersAsync(context, options.BaseUrl));
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

    private static async Task EnsureChromiumAsync(bool headed, CancellationToken cancellationToken)
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
    }

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

    private async Task InstallSiteHeadersAsync(IBrowserContext context, string? baseUrl)
    {
        if (_options.Headers is not { Count: > 0 } headers || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var app))
        {
            return;
        }

        var lowered = headers.ToDictionary(pair => pair.Key.ToLowerInvariant(), pair => pair.Value, StringComparer.Ordinal);
        await context.RouteAsync(url => IsAppRequest(url, app), async route =>
        {
            var merged = new Dictionary<string, string>(route.Request.Headers, StringComparer.Ordinal);
            foreach (var (name, value) in lowered)
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
        private readonly IBrowser _browser;
        private readonly TimeSpan _actionTimeout;
        private readonly string _testIdAttribute;
        private readonly WebEngineOptions _options;
        private readonly Func<IBrowserContext, Task> _setUpContext;
        private IBrowserContext? _context;
        private IPage? _page;
        private ViewportSize? _viewport;
        private Dictionary<string, IFrame> _frames = new(StringComparer.Ordinal);
        private int _nextRef = 1;

        public WebSession(IPlaywright playwright, IBrowser browser, TimeSpan actionTimeout, WebEngineOptions options, Func<IBrowserContext, Task> setUpContext)
        {
            _playwright = playwright;
            _browser = browser;
            _options = options;
            _setUpContext = setUpContext;
            _testIdAttribute = options.TestIdAttribute;
            _viewport = options.Viewport is { } viewport ? new ViewportSize { Width = viewport.Width, Height = viewport.Height } : null;
            _actionTimeout = actionTimeout;
        }

        private IPage Page => _page ?? throw new EngineException(EngineErrorCodes.InvalidState, "The browser has no open page.");

        private float ActionMs => (float)_actionTimeout.TotalMilliseconds;

        public string Route => Routes.PathOf(Page.Url);

        public async Task OpenAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.Load,
                    Timeout = (float)_actionTimeout.TotalMilliseconds,
                }).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "navigate to " + url);
            }
        }

        public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var walk = new FrameWalk();
            List<WebNode> roots;
            try
            {
                roots = await CollectAsync(Page.MainFrame, walk, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.NavigationStaleOr(ex, "observe");
            }

            _frames = walk.Frames;
            return new Observation { Route = Route, Roots = roots.Select(ToNode).ToList(), Truncated = walk.Truncated, ScrollPosition = walk.Scroll };
        }

        public async Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(action);
            var frame = _frames.GetValueOrDefault(node.Ref) ?? Page.MainFrame;
            if (frame.IsDetached)
            {
                throw new EngineException(EngineErrorCodes.NodeStale, $"Node {node.Ref} is not on the page. Observe again.", retryable: true);
            }

            IJSHandle handle;
            try
            {
                handle = await frame.EvaluateHandleAsync(PageScript.Find, node.Ref).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.NavigationStaleOr(ex, "locate " + node.Ref);
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
            var timeout = (float)_actionTimeout.TotalMilliseconds;
            try
            {
                switch (action)
                {
                    case LocatorAction.Tap:
                        await element.ClickAsync(new ElementHandleClickOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.DoubleTap:
                        await element.DblClickAsync(new ElementHandleDblClickOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Fill fill:
                        await element.FillAsync(fill.Value, new ElementHandleFillOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Press press:
                        await element.PressAsync(press.Key, new ElementHandlePressOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.PressSequentially typed:
                        await element.FocusAsync().ConfigureAwait(false);
                        await Page.Keyboard.TypeAsync(typed.Text, new KeyboardTypeOptions
                        {
                            Delay = typed.Delay is TimeSpan delay ? (float)delay.TotalMilliseconds : null,
                        }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Select select:
                        await element.SelectOptionAsync(select.Value, new ElementHandleSelectOptionOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Check:
                        await SetCheckedAsync(element, true, timeout).ConfigureAwait(false);
                        break;
                    case LocatorAction.Uncheck:
                        await SetCheckedAsync(element, false, timeout).ConfigureAwait(false);
                        break;
                    case LocatorAction.Clear:
                        await element.FillAsync("", new ElementHandleFillOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Focus:
                        await element.FocusAsync().ConfigureAwait(false);
                        break;
                    case LocatorAction.ScrollIntoView:
                        await element.ScrollIntoViewIfNeededAsync(new ElementHandleScrollIntoViewIfNeededOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Swipe swipe:
                        await element.EvaluateAsync(PageScript.ScrollElement, Direction(swipe.Direction)).ConfigureAwait(false);
                        break;
                    default:
                        throw new EngineException(EngineErrorCodes.UnsupportedCapability, "Web engine cannot perform " + action.GetType().Name + ".");
                }
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.ClassifyAction(ex, action);
            }
        }

        public async Task PressAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Page.Keyboard.PressAsync(key).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "press " + key);
            }
        }

        public async Task BackAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Page.GoBackAsync(new PageGoBackOptions { WaitUntil = WaitUntilState.Load, Timeout = ActionMs }).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "back");
            }
        }

        public async Task ForwardAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Page.GoForwardAsync(new PageGoForwardOptions { WaitUntil = WaitUntilState.Load, Timeout = ActionMs }).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "forward");
            }
        }

        public async Task ReloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = ActionMs }).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.Translate(ex, "reload");
            }
        }

        public async Task RestartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = RequireContext();
            _page = null;
            foreach (var page in context.Pages.ToList())
            {
                await page.CloseAsync().ConfigureAwait(false);
            }

            await NewPageAsync(context).ConfigureAwait(false);
        }

        public async Task ClearStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = _context;
            _context = null;
            _page = null;
            if (context is not null)
            {
                await context.CloseAsync().ConfigureAwait(false);
            }

            await NewContextAsync().ConfigureAwait(false);
        }

        public Task<string> GetUrlAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Page.Url);
        }

        public Task<string> GetTitleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Page.TitleAsync();
        }

        public async Task<System.Text.Json.JsonElement?> EvaluateAsync(string expression, object? arg, bool hasArg, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return hasArg
                    ? await Page.EvaluateAsync<System.Text.Json.JsonElement?>(expression, arg).ConfigureAwait(false)
                    : await Page.EvaluateAsync<System.Text.Json.JsonElement?>(expression).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                throw new TestException("EVALUATE_FAILED", ex.Message, ex);
            }
        }

        public async Task<BrowserResponse> WaitForResponseAsync(Func<string, bool> matches, TimeSpan timeout, CancellationToken cancellationToken)
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
        }

        public async Task<IReadOnlyList<BrowserCookie>> GetCookiesAsync(CancellationToken cancellationToken)
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
        }

        public Task SetCookiesAsync(IReadOnlyList<BrowserCookie> cookies, CancellationToken cancellationToken)
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
        }

        public async Task SetViewportAsync(int width, int height, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _viewport = new ViewportSize { Width = width, Height = height };
            await Page.SetViewportSizeAsync(width, height).ConfigureAwait(false);
        }

        public Task KeyboardTypeAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Page.Keyboard.TypeAsync(text);
        }

        public Task MouseMoveAsync(float x, float y, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Page.Mouse.MoveAsync(x, y);
        }

        public Task MouseWheelAsync(float deltaX, float deltaY, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Page.Mouse.WheelAsync(deltaX, deltaY);
        }

        public Task MouseDownAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Page.Mouse.DownAsync();
        }

        public Task MouseUpAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Page.Mouse.UpAsync();
        }

        /// <summary>Opens a clean context and its first page at the current viewport.</summary>
        public async Task NewContextAsync()
        {
            var context = await _browser.NewContextAsync(ContextOptions(_options, _viewport)).ConfigureAwait(false);
            await context.AddInitScriptAsync(PageScript.RecordClosedShadowRoots).ConfigureAwait(false);
            await _setUpContext(context).ConfigureAwait(false);
            _context = context;
            await NewPageAsync(context).ConfigureAwait(false);
        }

        public async Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Page.EvaluateAsync(PageScript.ScrollViewport, Direction(direction)).ConfigureAwait(false);
            }
            catch (Exception ex) when (WebErrors.IsPlaywright(ex))
            {
                throw WebErrors.NavigationStaleOr(ex, "scroll");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_context is not null)
                {
                    await _context.CloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                await _browser.CloseAsync().ConfigureAwait(false);
                _playwright.Dispose();
            }
        }

        /// <summary>
        /// Reads one frame's document, then each child frame's into the iframe
        /// node that owns it, sharing one node budget across all of them. A
        /// frame that detaches or navigates mid-read is left empty.
        /// </summary>
        private async Task<List<WebNode>> CollectAsync(IFrame frame, FrameWalk walk, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (walk.Remaining <= 0)
            {
                walk.Truncated = true;
                return [];
            }

            string json;
            try
            {
                json = await frame.EvaluateAsync<string>(PageScript.Collect, new { seed = _nextRef, max = walk.Remaining, testIdAttribute = _testIdAttribute }).ConfigureAwait(false);
            }
            catch (PlaywrightException) when (frame != Page.MainFrame)
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
                var owner = await OwnerOfAsync(child).ConfigureAwait(false);
                if (owner is not null && owners.TryGetValue(owner, out var node))
                {
                    node.Children = await CollectAsync(child, walk, cancellationToken).ConfigureAwait(false);
                }
            }

            return roots;
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
            var page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout(ActionMs);
            if (_viewport is { } viewport)
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height).ConfigureAwait(false);
            }

            _page = page;
        }

        private IBrowserContext RequireContext() =>
            _context ?? throw new EngineException(EngineErrorCodes.InvalidState, "The browser has no context.");

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
        private static async Task SetCheckedAsync(IElementHandle element, bool @checked, float timeout)
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

            await element.ClickAsync(new ElementHandleClickOptions { Timeout = timeout }).ConfigureAwait(false);
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

    /// <summary>Classifies a failed locator action. A timeout after the input was dispatched may have committed.</summary>
    public static EngineException ClassifyAction(Exception rawCause, LocatorAction action)
    {
        var sensitive = action is LocatorAction.Fill { Sensitive: true };
        var text = Message(rawCause);
        if (action is LocatorAction.Fill fill && sensitive && fill.Value.Length > 0)
        {
            // The message can quote the value in any spelling or cut short.
            text = Redactor.ForValue(fill.Value).RedactFragments(text);
        }

        var cause = sensitive ? null : rawCause;
        var kind = action.GetType().Name.ToLowerInvariant();
        if (StrictModePattern().IsMatch(text))
        {
            return new EngineException(EngineErrorCodes.EngineFailure, text, retryable: false, cause);
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

    // The headline and the last call log line, which names what blocked the action.
    private static string Summary(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return text;
        }

        var last = lines[^1].TrimStart('-', ' ');
        return lines.Length > 1 && !string.Equals(last, "Call log:", StringComparison.Ordinal)
            ? lines[0] + " Last: " + last
            : lines[0];
    }

    [GeneratedRegex(@"\u001b\[\d+(?:;\d+)*m")]
    private static partial Regex AnsiPattern();

    [GeneratedRegex("strict mode violation", RegexOptions.IgnoreCase)]
    private static partial Regex StrictModePattern();

    [GeneratedRegex("element (is |was )?(detached|not attached)", RegexOptions.IgnoreCase)]
    private static partial Regex DetachedPattern();

    [GeneratedRegex("Timeout .*exceeded", RegexOptions.IgnoreCase)]
    private static partial Regex TimeoutPattern();

    [GeneratedRegex(@"performing \w+ action|\w+ action done|waiting for scheduled navigations to finish", RegexOptions.IgnoreCase)]
    private static partial Regex PostDispatchPattern();

    [GeneratedRegex("not an? <?(input|checkbox|radio|select)|not editable|not checkable", RegexOptions.IgnoreCase)]
    private static partial Regex NotEditablePattern();

    [GeneratedRegex("execution context was destroyed|because of a navigation|navigating and changing the content|frame was detached|frame got detached|node is detached from document", RegexOptions.IgnoreCase)]
    private static partial Regex NavigationRacePattern();
}
