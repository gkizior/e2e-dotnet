// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Engine;

/// <summary>
/// In-memory page engine for samples and tests that do not need a browser.
/// Each open builds a fresh page from the route mapped on
/// <see cref="DocumentWorld"/>. Actions mutate that page until the next open.
/// <c>app.back</c> rebuilds the previous route, and <c>app.restart</c> and
/// <c>app.clearState</c> leave a blank page with no history. The document engine
/// has no browser, so the <see cref="Browser"/> fixture is unsupported.
/// </summary>
public sealed class DocumentEngine : IEngine
{
    public const string EngineVersion = "1.0.0";

    private readonly DocumentWorld _world;

    public DocumentEngine(DocumentWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
    }

    public string Platform => "document";

    public string Version => EngineVersion;

    public EngineCapabilities Capabilities =>
        EngineCapabilities.Observation
        | EngineCapabilities.Actions
        | EngineCapabilities.Location
        | EngineCapabilities.Keyboard
        | EngineCapabilities.Scroll
        | EngineCapabilities.History;

    public Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IEngineSession session = new DocumentSession(_world);
        return Task.FromResult(session);
    }

    private sealed class DocumentSession : IEngineSession
    {
        private readonly DocumentWorld _world;
        private DocumentPage? _page;
        private DocumentElement? _focused;
        private Dictionary<string, DocumentElement> _refs = new(StringComparer.Ordinal);
        private readonly List<string> _history = [];

        public DocumentSession(DocumentWorld world)
        {
            _world = world;
        }

        public string Route => _page?.Path ?? "/";

        public Task OpenAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Navigate(Routes.PathOf(url));
            return Task.CompletedTask;
        }

        public Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequirePage().OnScroll?.Invoke(direction);
            return Task.CompletedTask;
        }

        public Task BackAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_history.Count > 1)
            {
                _history.RemoveAt(_history.Count - 1);
                Show(_world.Create(_history[^1]));
            }

            return Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _history.Clear();
            Show(null);
            return Task.CompletedTask;
        }

        public Task ClearStateAsync(CancellationToken cancellationToken) => RestartAsync(cancellationToken);

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = RequirePage();
            var next = 1;
            var refs = new Dictionary<string, DocumentElement>(StringComparer.Ordinal);
            var roots = new List<SemanticNode>();
            foreach (var element in page.Roots)
            {
                roots.Add(ToNode(element, ref next, refs));
            }

            _refs = refs;
            return Task.FromResult(new Observation { Route = page.Path, Roots = roots });
        }

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(node);
            ArgumentNullException.ThrowIfNull(action);
            if (!_refs.TryGetValue(node.Ref, out var element))
            {
                throw new EngineException("NOT_FOUND", $"Node {node.Ref} is not in the latest observation.");
            }

            if (element.Disabled)
            {
                throw new EngineException("NOT_ACTIONABLE", $"{Describe(element)} is disabled.");
            }

            switch (action)
            {
                case LocatorAction.Tap:
                    Tap(element);
                    return element.NavigateTo is null ? Task.CompletedTask : OpenAsync(element.NavigateTo, cancellationToken);
                case LocatorAction.DoubleTap:
                    // A browser fires two clicks before dblclick. A link navigates on the first.
                    Tap(element);
                    if (element.NavigateTo is not null)
                    {
                        Navigate(Routes.PathOf(element.NavigateTo));
                    }

                    Tap(element);
                    break;
                case LocatorAction.Fill fill:
                    RequireText(element);
                    element.Value = fill.Value;
                    element.OnFill?.Invoke(fill.Value);
                    _focused = element;
                    break;
                case LocatorAction.Press press:
                    _focused = element;
                    Activate(element, press.Key);
                    break;
                case LocatorAction.PressSequentially typed:
                    RequireText(element);
                    _focused = element;
                    foreach (var character in typed.Text)
                    {
                        element.Value = (element.Value ?? "") + character;
                        element.OnFill?.Invoke(element.Value);
                    }

                    break;
                case LocatorAction.Select select:
                    element.Value = select.Value;
                    break;
                case LocatorAction.Check:
                    element.Checked = true;
                    element.OnTap?.Invoke();
                    break;
                case LocatorAction.Uncheck:
                    element.Checked = false;
                    break;
                case LocatorAction.Clear:
                    element.Value = "";
                    _focused = element;
                    break;
                case LocatorAction.Focus:
                    _focused = element;
                    break;
                case LocatorAction.ScrollIntoView:
                    // The document engine has no viewport; every node is already in view.
                    break;
                case LocatorAction.Swipe swipe:
                    element.OnScroll?.Invoke(swipe.Direction);
                    break;
                default:
                    throw new EngineException("UNSUPPORTED_CAPABILITY", $"Document engine cannot perform {action.GetType().Name}.");
            }

            return Task.CompletedTask;
        }

        public Task PressAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_focused is null)
            {
                throw new EngineException("NOT_ACTIONABLE", "Nothing has focus.");
            }

            Activate(_focused, key);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static void RequireText(DocumentElement element)
        {
            if (element.Role != "textbox" && element.Role != "combobox" && element.Role != "searchbox")
            {
                throw new EngineException("NOT_ACTIONABLE", $"{Describe(element)} does not accept text.");
            }
        }

        private void Tap(DocumentElement element)
        {
            if (element.Role == "checkbox" && element.OnTap is null)
            {
                element.Checked = !element.Checked;
            }

            element.OnTap?.Invoke();
            if (element.Role == "textbox")
            {
                _focused = element;
            }
        }

        private void Activate(DocumentElement element, string key)
        {
            if (string.Equals(key, "Enter", StringComparison.Ordinal) && element.Role == "button")
            {
                element.OnTap?.Invoke();
                if (element.NavigateTo is not null)
                {
                    Navigate(Routes.PathOf(element.NavigateTo));
                }
            }
        }

        private void Navigate(string path)
        {
            var page = _world.Create(path);
            _history.Add(path);
            Show(page);
        }

        private void Show(DocumentPage? page)
        {
            _page = page;
            _focused = null;
            _refs = new Dictionary<string, DocumentElement>(StringComparer.Ordinal);
        }

        private DocumentPage RequirePage()
        {
            if (_page is null)
            {
                throw new EngineException("NOT_FOUND", "Open a route before reading the screen.");
            }

            return _page;
        }

        private static SemanticNode ToNode(
            DocumentElement element,
            ref int next,
            Dictionary<string, DocumentElement> refs)
        {
            var id = "e" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
            next++;
            refs[id] = element;
            var children = new List<SemanticNode>();
            foreach (var child in element.Children)
            {
                children.Add(ToNode(child, ref next, refs));
            }

            var secure = element.Secure || element.InputPurpose == "password";
            return new SemanticNode
            {
                Ref = id,
                Role = element.Role,
                Name = Cut(element.Name, ObservationLimits.Name),
                Text = Cut(element.Text, ObservationLimits.Text),
                Value = secure ? null : element.Value,
                TestId = element.TestId,
                Placeholder = element.Placeholder,
                InputPurpose = element.InputPurpose,
                Level = element.Level,
                States = new NodeStates
                {
                    Checked = element.Checked,
                    Disabled = element.Disabled,
                    Hidden = element.Hidden,
                    Selected = element.Selected,
                    Expanded = element.Expanded,
                    Pressed = element.Pressed,
                    Secure = secure,
                    Focused = element.Focused,
                },
                Attributes = AttributesOf(element, secure),
                Rect = element.Rect,
                Children = children,
            };
        }

        private static Dictionary<string, string> AttributesOf(DocumentElement element, bool secure)
        {
            var attributes = new Dictionary<string, string>(element.Attributes, StringComparer.Ordinal);
            if (secure)
            {
                attributes.Remove("value");
            }

            return attributes;
        }

        private static string? Cut(string? value, int limit)
        {
            if (value is null || value.Length <= limit)
            {
                return value;
            }

            return value[..limit];
        }

        private static string Describe(DocumentElement element)
        {
            var role = element.Role ?? "node";
            return element.Name is null ? role : role + " \"" + element.Name + "\"";
        }
    }
}

/// <summary>Routes a <see cref="DocumentEngine"/> can open. Each open builds a new page from the mapped factory.</summary>
public sealed class DocumentWorld
{
    private readonly Dictionary<string, Func<DocumentPage>> _routes = new(StringComparer.Ordinal);

    public DocumentWorld Map(string route, Action<DocumentPage> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        var path = Routes.PathOf(route);
        _routes[path] = () =>
        {
            var page = new DocumentPage(path);
            build(page);
            return page;
        };
        return this;
    }

    internal DocumentPage Create(string route)
    {
        var path = Routes.PathOf(route);
        if (!_routes.TryGetValue(path, out var factory))
        {
            throw new EngineException("NOT_FOUND", $"No document route is mapped for '{path}'.");
        }

        return factory();
    }
}

/// <summary>One in-memory screen. Builder methods append controls in reading order.</summary>
public sealed class DocumentPage
{
    internal DocumentPage(string path)
    {
        Path = path;
    }

    public string Path { get; }

    /// <summary>Runs when the viewport scrolls, for a page that loads more as it is scrolled. The page has no viewport of its own.</summary>
    public Action<ScrollDirection>? OnScroll { get; set; }

    internal List<DocumentElement> Roots { get; } = [];

    public DocumentElement Heading(string name, int level = 1)
    {
        return Add(new DocumentElement { Role = "heading", Name = name, Level = level });
    }

    public DocumentElement Button(string name, Action? onTap = null)
    {
        return Add(new DocumentElement { Role = "button", Name = name, OnTap = onTap });
    }

    public DocumentElement Status(string name, bool hidden = false)
    {
        return Add(new DocumentElement { Role = "status", Name = name, Text = name, Hidden = hidden });
    }

    public DocumentElement Textbox(string name, string? value = null, bool secure = false)
    {
        return Add(new DocumentElement
        {
            Role = "textbox",
            Name = name,
            Value = value,
            Secure = secure,
            InputPurpose = secure ? "password" : null,
        });
    }

    public DocumentElement Paragraph(string text)
    {
        return Add(new DocumentElement { Text = text });
    }

    public DocumentElement Link(string name, string route)
    {
        return Add(new DocumentElement { Role = "link", Name = name, NavigateTo = route });
    }

    public DocumentElement Checkbox(string name, bool isChecked = false)
    {
        return Add(new DocumentElement { Role = "checkbox", Name = name, Checked = isChecked });
    }

    public DocumentElement TestId(string testId, string? role = null, string? name = null)
    {
        return Add(new DocumentElement { Role = role, Name = name, TestId = testId });
    }

    private DocumentElement Add(DocumentElement element)
    {
        Roots.Add(element);
        return element;
    }
}

/// <summary>A mutable control on a <see cref="DocumentPage"/>.</summary>
public sealed class DocumentElement
{
    public string? Role { get; init; }

    public string? Name { get; set; }

    public string? Text { get; set; }

    public string? Value { get; set; }

    public string? TestId { get; init; }

    public string? Placeholder { get; init; }

    public string? InputPurpose { get; init; }

    public int? Level { get; init; }

    public bool Disabled { get; set; }

    public bool Checked { get; set; }

    public bool Hidden { get; set; }

    public bool Selected { get; set; }

    public bool Expanded { get; set; }

    public bool Pressed { get; set; }

    public bool Secure { get; set; }

    public bool Focused { get; set; }

    public string? NavigateTo { get; init; }

    /// <summary>Attributes <c>getAttribute</c> and <c>toHaveAttribute</c> read, such as <c>href</c> or <c>aria-label</c>.</summary>
    public Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);

    /// <summary>The box <c>boundingBox</c> reports. Null when the page does not lay it out.</summary>
    public BoundingBox? Rect { get; set; }

    public Action? OnTap { get; init; }

    public Action<string>? OnFill { get; init; }

    /// <summary>Runs when this element is scrolled with <see cref="LocatorAction.Swipe"/>.</summary>
    public Action<ScrollDirection>? OnScroll { get; set; }

    public List<DocumentElement> Children { get; } = [];
}
