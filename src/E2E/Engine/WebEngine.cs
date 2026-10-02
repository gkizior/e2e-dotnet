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
            var json = await _page.EvaluateAsync<string>(PageScript.Collect).ConfigureAwait(false);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<List<WebNode>>(json, JsonDefaults.Options) ?? [];
            var next = 1;
            var roots = dtos.Select(dto => ToNode(dto, ref next)).ToList();
            return new Observation { Route = Route, Roots = roots };
        }

        public async Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(action);
            var locator = LocatorFor(node);
            var timeout = (float)_actionTimeout.TotalMilliseconds;
            switch (action)
            {
                case LocatorAction.Tap:
                    await locator.ClickAsync(new LocatorClickOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Fill fill:
                    await locator.FillAsync(fill.Value, new LocatorFillOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Press press:
                    await locator.PressAsync(press.Key, new LocatorPressOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Select select:
                    await locator.SelectOptionAsync(select.Value, new LocatorSelectOptionOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Check:
                    await locator.CheckAsync(new LocatorCheckOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Uncheck:
                    await locator.UncheckAsync(new LocatorUncheckOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                case LocatorAction.Clear:
                    await locator.FillAsync("", new LocatorFillOptions { Timeout = timeout }).ConfigureAwait(false);
                    break;
                default:
                    throw new EngineException("UNSUPPORTED_CAPABILITY", "Web engine cannot perform " + action.GetType().Name + ".");
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

        private ILocator LocatorFor(SemanticNode node)
        {
            if (!string.IsNullOrEmpty(node.TestId))
            {
                return _page.GetByTestId(node.TestId);
            }

            if (node.Role is not null && TryRole(node.Role, out var role))
            {
                if (string.IsNullOrEmpty(node.Name))
                {
                    return _page.GetByRole(role);
                }

                return _page.GetByRole(role, new PageGetByRoleOptions { Name = node.Name, Exact = true });
            }

            if (!string.IsNullOrEmpty(node.Name))
            {
                return _page.GetByText(node.Name, new PageGetByTextOptions { Exact = true });
            }

            if (!string.IsNullOrEmpty(node.Text))
            {
                return _page.GetByText(node.Text, new PageGetByTextOptions { Exact = true });
            }

            throw new EngineException("NOT_FOUND", "Node " + node.Ref + " has no role, name, or test id.");
        }

        private static bool TryRole(string role, out AriaRole aria)
        {
            switch (role.ToLowerInvariant())
            {
                case "button":
                    aria = AriaRole.Button;
                    return true;
                case "link":
                    aria = AriaRole.Link;
                    return true;
                case "textbox":
                case "searchbox":
                    aria = AriaRole.Textbox;
                    return true;
                case "checkbox":
                    aria = AriaRole.Checkbox;
                    return true;
                case "radio":
                    aria = AriaRole.Radio;
                    return true;
                case "heading":
                    aria = AriaRole.Heading;
                    return true;
                case "status":
                    aria = AriaRole.Status;
                    return true;
                case "combobox":
                    aria = AriaRole.Combobox;
                    return true;
                case "listitem":
                    aria = AriaRole.Listitem;
                    return true;
                case "tab":
                    aria = AriaRole.Tab;
                    return true;
                case "image":
                    aria = AriaRole.Img;
                    return true;
                case "navigation":
                    aria = AriaRole.Navigation;
                    return true;
                default:
                    aria = default;
                    return false;
            }
        }

        private static SemanticNode ToNode(WebNode dto, ref int next)
        {
            var id = "e" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            next++;
            var children = new List<SemanticNode>();
            if (dto.Children is not null)
            {
                foreach (var child in dto.Children)
                {
                    children.Add(ToNode(child, ref next));
                }
            }

            return new SemanticNode
            {
                Ref = id,
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
                Children = children,
            };
        }
    }

    private sealed class WebNode
    {
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
