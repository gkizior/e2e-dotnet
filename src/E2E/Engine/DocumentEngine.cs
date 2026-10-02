// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Engine;

/// <summary>
/// In-memory page engine for samples and tests that do not need a browser.
/// Each open builds a fresh page from the route mapped on
/// <see cref="DocumentWorld"/>. Actions mutate that page until the next open.
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
        | EngineCapabilities.Keyboard;

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

        public DocumentSession(DocumentWorld world)
        {
            _world = world;
        }

        public string Route => _page?.Path ?? "/";

        public Task OpenAsync(string url, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Routes.PathOf(url);
            _page = _world.Create(path);
            _focused = null;
            _refs = new Dictionary<string, DocumentElement>(StringComparer.Ordinal);
            return Task.CompletedTask;
        }

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
                    if (element.Role == "checkbox" && element.OnTap is null)
                    {
                        element.Checked = !element.Checked;
                    }

                    element.OnTap?.Invoke();
                    if (element.Role == "textbox")
                    {
                        _focused = element;
                    }

                    if (element.NavigateTo is not null)
                    {
                        return OpenAsync(element.NavigateTo, cancellationToken);
                    }

                    break;
                case LocatorAction.Fill fill:
                    if (element.Role != "textbox" && element.Role != "combobox" && element.Role != "searchbox")
                    {
                        throw new EngineException("NOT_ACTIONABLE", $"{Describe(element)} does not accept text.");
                    }

                    element.Value = fill.Value;
                    element.OnFill?.Invoke(fill.Value);
                    _focused = element;
                    break;
                case LocatorAction.Press press:
                    _focused = element;
                    Activate(element, press.Key);
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

        private void Activate(DocumentElement element, string key)
        {
            if (string.Equals(key, "Enter", StringComparison.Ordinal) && element.Role == "button")
            {
                element.OnTap?.Invoke();
                if (element.NavigateTo is not null)
                {
                    _page = _world.Create(Routes.PathOf(element.NavigateTo));
                    _focused = null;
                }
            }
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
                    Secure = secure,
                    Focused = false,
                },
                Children = children,
            };
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

    public bool Secure { get; set; }

    public string? NavigateTo { get; init; }

    public Action? OnTap { get; init; }

    public Action<string>? OnFill { get; init; }

    public List<DocumentElement> Children { get; } = [];
}
