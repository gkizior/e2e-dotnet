// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;
using Microsoft.Playwright;

namespace E2E.Engine;

/// <summary>
/// Browser engine for the web target. It drives Chromium through Playwright,
/// reads a semantic tree (role, name, text, test id, state), and performs the
/// same node actions the document engine does. Install browsers once with
/// <c>playwright.ps1 install chromium</c> from this project's build output.
/// </summary>
public sealed class WebEngine : IEngine
{
    public const string EngineVersion = "1.0.0";

    private readonly bool _headless;

    public WebEngine(bool? headless = null)
    {
        if (headless is bool chosen)
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
        | EngineCapabilities.Keyboard;

    public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = _headless }).ConfigureAwait(false);
            var context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            }).ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);
            page.SetDefaultTimeout((float)options.ActionTimeout.TotalMilliseconds);
            return new WebSession(playwright, browser, page, options.ActionTimeout);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal) || ex.Message.Contains("browserType.launch", StringComparison.Ordinal))
        {
            throw new EngineException(
                "ENVIRONMENT_UNAVAILABLE",
                "Chromium is not installed for Playwright. From the build output run: playwright.ps1 install chromium",
                ex);
        }
    }

    private sealed class WebSession : IEngineSession
    {
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly IPage _page;
        private readonly TimeSpan _actionTimeout;
        private int _nextRef = 1;

        public WebSession(IPlaywright playwright, IBrowser browser, IPage page, TimeSpan actionTimeout)
        {
            _playwright = playwright;
            _browser = browser;
            _page = page;
            _actionTimeout = actionTimeout;
        }

        public string Route => Routes.PathOf(_page.Url);

        public async Task OpenAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = (float)_actionTimeout.TotalMilliseconds,
            }).ConfigureAwait(false);
        }

        public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var json = await _page.EvaluateAsync<string>(PageScript.Collect, _nextRef).ConfigureAwait(false);
            var dto = System.Text.Json.JsonSerializer.Deserialize<WebObservation>(json, JsonDefaults.Options);
            _nextRef = dto?.Next ?? _nextRef;
            var roots = (dto?.Roots ?? []).Select(ToNode).ToList();
            return new Observation { Route = Route, Roots = roots };
        }

        public async Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(action);
            var handle = await _page.EvaluateHandleAsync(PageScript.Find, node.Ref).ConfigureAwait(false);
            var element = handle.AsElement();
            if (element is null)
            {
                await handle.DisposeAsync().ConfigureAwait(false);
                throw new EngineException("NOT_FOUND", $"Node {node.Ref} is not on the page.");
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
                    case LocatorAction.Fill fill:
                        await element.FillAsync(fill.Value, new ElementHandleFillOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Press press:
                        await element.PressAsync(press.Key, new ElementHandlePressOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Select select:
                        await element.SelectOptionAsync(select.Value, new ElementHandleSelectOptionOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Check:
                        await element.CheckAsync(new ElementHandleCheckOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Uncheck:
                        await element.UncheckAsync(new ElementHandleUncheckOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    case LocatorAction.Clear:
                        await element.FillAsync("", new ElementHandleFillOptions { Timeout = timeout }).ConfigureAwait(false);
                        break;
                    default:
                        throw new EngineException("UNSUPPORTED_CAPABILITY", "Web engine cannot perform " + action.GetType().Name + ".");
                }
            }
            catch (PlaywrightException ex) when (ex.Message.Contains("not attached", StringComparison.Ordinal))
            {
                throw new EngineException("NOT_FOUND", $"Node {node.Ref} left the page before the action.", ex);
            }
        }

        public Task PressAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _page.Keyboard.PressAsync(key);
        }

        public async ValueTask DisposeAsync()
        {
            await _browser.CloseAsync().ConfigureAwait(false);
            _playwright.Dispose();
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
                    Hidden = dto.Hidden,
                    Secure = dto.Secure,
                },
                Children = dto.Children?.Select(ToNode).ToList() ?? [],
            };
        }
    }

    private sealed class WebObservation
    {
        public int Next { get; set; }

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

        public bool Hidden { get; set; }

        public bool Secure { get; set; }

        public List<WebNode>? Children { get; set; }
    }
}
