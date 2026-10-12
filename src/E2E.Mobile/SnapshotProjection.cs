// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>
/// Projection of an agent-device accessibility snapshot onto the contract's <see cref="SemanticNode"/>
/// tree. agent-device hands back a flat list with tree coordinates (<c>index</c>, <c>parentIndex</c>,
/// <c>depth</c>) and platform element types; this class rebuilds the tree, maps the types onto the closed
/// role vocabulary <c>screen.GetByRole</c> and trace relocation speak, and mints the ids the surface owns.
/// </summary>
public static partial class SnapshotProjection
{
    /// <summary>
    /// The id of the root node every observation is reported under. It is the same across observations,
    /// as the contract requires: the viewport swipe is addressed from an earlier observation.
    /// </summary>
    public const string RootId = "root";

    /// <summary>
    /// Platform element types onto the role vocabulary. Anything not listed keeps its lower-cased type as
    /// the role, so no information is lost; the map only makes the common controls answer the same
    /// <c>GetByRole</c> a browser does.
    /// </summary>
    private static readonly Dictionary<string, string> RoleMap = new(StringComparer.Ordinal)
    {
        ["button"] = "button",
        ["link"] = "link",
        ["text-field"] = "textbox",
        ["textfield"] = "textbox",
        ["search-field"] = "textbox",
        ["text-view"] = "textbox",
        ["edit-text"] = "textbox",
        ["secure-text-field"] = "textbox",
        ["securetextfield"] = "textbox",
        ["switch"] = "switch",
        ["toggle"] = "switch",
        ["checkbox"] = "checkbox",
        ["check-box"] = "checkbox",
        ["slider"] = "slider",
        ["image"] = "image",
        ["image-view"] = "image",
        ["static-text"] = "text",
        ["text"] = "text",
        ["cell"] = "listitem",
        ["list-item"] = "listitem",
        ["table-cell"] = "listitem",
        ["collection-cell"] = "listitem",
        ["tab"] = "tab",
        ["tab-bar-item"] = "tab",
        ["tab-bar-button"] = "tab",
        ["tab-bar"] = "tablist",
        ["tab-group"] = "tablist",
        ["segmented-control"] = "tablist",
        ["menu"] = "menu",
        ["menu-bar"] = "menubar",
        ["menu-item"] = "menuitem",
        ["menu-bar-item"] = "menuitem",
        ["menu-button"] = "menuitem",
        ["toolbar"] = "toolbar",
        ["radio-group"] = "radiogroup",
        ["radio-button"] = "radio",
        ["stepper"] = "spinbutton",
        ["table-row"] = "row",
        ["alert"] = "alert",
        ["dialog"] = "dialog",
        ["sheet"] = "dialog",
        ["action-sheet"] = "dialog",
        ["heading"] = "heading",
        ["header"] = "heading",
        ["navigation-bar"] = "navigation",

        // A spinner says something is happening; a bar says how far along it is.
        ["activity-indicator"] = "status",
        ["progress-indicator"] = "progressbar",
    };

    private static readonly HashSet<string> SecureKinds = ["secure-text-field", "securetextfield", "password-field"];

    private static readonly HashSet<string> TextInputRoles = ["textbox", "searchbox", "combobox"];

    private static readonly HashSet<string> CheckableRoles = ["switch", "checkbox"];

    private static readonly HashSet<string> CheckedValues = ["1", "on", "true", "checked", "selected"];

    private static readonly HashSet<string> UncheckedValues = ["0", "off", "false", "unchecked"];

    private static readonly HashSet<string> ScreenKinds = ["application", "app", "window"];

    /// <summary>
    /// The role descriptions React Native writes into an iOS view's accessibility value for the roles
    /// UIKit has no trait for, onto the contract role each stands for. Fabric
    /// (<c>RCTViewComponentView.mm</c>) writes <c>checkbox</c> and <c>radio button</c>; Paper
    /// (<c>RCTView.m</c>) writes the whole list.
    /// </summary>
    private static readonly Dictionary<string, string> ReactNativeRoleDescriptions = new(StringComparer.Ordinal)
    {
        ["alert"] = "alert",
        ["checkbox"] = "checkbox",
        ["combo box"] = "combobox",
        ["menu"] = "menu",
        ["menu bar"] = "menubar",
        ["menu item"] = "menuitem",
        ["progress bar"] = "progressbar",
        ["radio button"] = "radio",
        ["radio group"] = "radiogroup",
        ["scroll bar"] = "scrollbar",
        ["spin button"] = "spinbutton",
        ["switch"] = "switch",
        ["tab"] = "tab",
        ["tab list"] = "tablist",
        ["timer"] = "timer",
        ["tool bar"] = "toolbar",
    };

    /// <summary>The state words React Native writes after the role description, and the state each one sets.</summary>
    private static readonly Dictionary<string, SpelledStates> ReactNativeStateWords = new(StringComparer.Ordinal)
    {
        ["checked"] = new(Checked: true),
        ["unchecked"] = new(Checked: false),
        ["expanded"] = new(Expanded: true),
        ["collapsed"] = new(Expanded: false),
    };

    /// <summary>State words with no contract state (<c>mixed</c>, <c>busy</c>): kept in the value, so they still read.</summary>
    private static readonly HashSet<string> ReactNativeLooseStateWords = ["mixed", "busy"];

    /// <summary>
    /// Android view classes onto the role vocabulary, keyed by the simple class name in kebab case. A
    /// <c>TextView</c> is static text here where an iOS <c>TextView</c> is an editor, which is why the two
    /// platforms keep separate maps.
    /// </summary>
    private static readonly Dictionary<string, string> AndroidRoleMap = new(StringComparer.Ordinal)
    {
        ["text-view"] = "text",
        ["edit-text"] = "textbox",
        ["auto-complete-text-view"] = "textbox",
        ["text-input-edit-text"] = "textbox",
        ["search-view"] = "textbox",
        ["button"] = "button",
        ["image-button"] = "button",
        ["material-button"] = "button",
        ["image-view"] = "image",
        ["switch"] = "switch",
        ["switch-compat"] = "switch",
        ["switch-material"] = "switch",
        ["toggle-button"] = "switch",
        ["check-box"] = "checkbox",
        ["material-check-box"] = "checkbox",
        ["radio-button"] = "radio",
        ["seek-bar"] = "slider",
        ["slider"] = "slider",
        ["spinner"] = "combobox",
        ["progress-bar"] = "progressbar",
        ["radio-group"] = "radiogroup",
        ["number-picker"] = "spinbutton",
        ["toolbar"] = "toolbar",
        ["tab-layout"] = "tablist",
        ["tab-widget"] = "tablist",
        ["bottom-navigation-view"] = "tablist",
        ["tab-layout$tab-view"] = "tab",
        ["bottom-navigation-item-view"] = "tab",
        ["web-view"] = "document",
        ["recycler-view"] = "list",
        ["list-view"] = "list",

        // React Native's `FlatList` and any view with `accessibilityRole="list"`.
        ["abs-list-view"] = "list",
        ["grid-view"] = "list",
        ["scroll-view"] = "group",
        ["view-group"] = "group",
        ["view"] = "group",
        ["compose-view"] = "group",
        ["view-factory-holder"] = "group",
        ["card-view"] = "group",
    };

    /// <summary>Identifier suffixes of the view that carries an Android screen's title.</summary>
    private static readonly string[] AndroidTitleIds = [":id/collapsing_toolbar", ":id/action_bar", ":id/toolbar"];

    /// <summary>iOS containers whose button children a browser would call tabs.</summary>
    private static readonly HashSet<string> TabContainerKinds = ["tab-bar", "segmented-control"];

    /// <summary>The package that owns Android's system bars.</summary>
    private const string SystemUiPackage = "com.android.systemui";

    /// <summary>The share of the screen a bar stays under.</summary>
    private const double SystemBarMaxShare = 0.25;

    private const double SystemBarMinWidthShare = 0.9;

    /// <summary>The screen edge band a status icon sits in.</summary>
    private const double StatusIconBandShare = 0.08;

    /// <summary>The width share a status icon stays under.</summary>
    private const double StatusIconMaxWidthShare = 0.5;

    /// <summary>The classes a status icon is built from: it shows, and offers nothing to act on.</summary>
    private static readonly HashSet<string> StatusIconKinds = ["text-view", "image-view", "frame-layout", "linear-layout", "view", "view-group"];

    /// <summary>One element-type spelling for <c>NavigationBar</c>, <c>navigation-bar</c>, and <c>android.widget.NavigationBar</c> alike.</summary>
    private static string NormalizeKind(string type)
    {
        var simple = type[(type.LastIndexOf('.') + 1)..];
        simple = WordBoundary().Replace(simple, "$1-$2");
        return Separators().Replace(simple, "-").ToLowerInvariant();
    }

    /// <summary>
    /// Projects one snapshot. <paramref name="mintId"/> is called once per node in document order, so the
    /// surface's id space stays unique across observations. Android's system bars are left out with their
    /// children, see <see cref="IsSystemBar"/>.
    /// </summary>
    public static ProjectedSnapshot ProjectSnapshot(IReadOnlyList<RawNode> raw, Func<string> mintId)
    {
        var viewport = ViewportOf(raw);
        var parents = ParentPositions(raw);
        var children = new Dictionary<int, List<int>>();
        var roots = new List<int>();
        for (var position = 0; position < parents.Count; position++)
        {
            if (parents[position] is not { } parent)
            {
                roots.Add(position);
            }
            else if (children.TryGetValue(parent, out var siblings))
            {
                siblings.Add(position);
            }
            else
            {
                children[parent] = [position];
            }
        }

        var index = new List<ProjectedNode>();

        // Whether every node from `position` down is a class that only shows (`StatusIconKinds`).
        bool DisplayOnly(int position)
        {
            var node = raw[position];
            if (node.Editable == true || !StatusIconKinds.Contains(NormalizeKind(node.Type ?? "")))
            {
                return false;
            }

            return !children.TryGetValue(position, out var below) || below.All(DisplayOnly);
        }

        SemanticNode Build(int position, ProjectedNode? parent)
        {
            var source = raw[position];
            var id = mintId();
            var kind = KindOf(source);
            var android = IsAndroidClass(source.Type);

            // The role React Native spelled into the value outranks the platform's `Other`: it is the role
            // the app declared, and iOS had no trait for it. Only an `Other` carries that encoding; a
            // native control's value is its own.
            var spelled = android || kind != "other" ? null : ReactNativeValue(source.Value);
            var role = EditableRole(spelled?.Role ?? DescribedRole(source) ?? RoleOf(kind, android, parent?.Kind), source.Editable);
            var editable = source.Editable == true || (role is not null && TextInputRoles.Contains(role));

            // A field showing its hint is empty: XCTest reports the placeholder as the value of an empty
            // text field, and the bridge flags that with hintShowing.
            var value = source.HintShowing == true ? "" : spelled is null ? source.Value : spelled.Value;

            // iOS names a secure field by class; UIAutomator flags a password `EditText` by attribute, the
            // class being the plain one.
            var secure = SecureKinds.Contains(kind) || source.Password == true;
            var checkedState = spelled?.States.Checked ?? source.Checked ?? CheckedOf(role, value);
            var expanded = spelled?.States.Expanded;
            var reported = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (source.Enabled == false)
            {
                reported["disabled"] = true;
            }

            if (source.Selected == true)
            {
                reported["selected"] = true;
            }

            if (source.Focused == true)
            {
                reported["focused"] = true;
            }

            if (source.VisibleToUser == false)
            {
                reported["hidden"] = true;
            }

            if (secure)
            {
                reported["secure"] = true;
            }

            if (checkedState is { } isChecked)
            {
                reported["checked"] = isChecked;
            }

            if (expanded is { } isExpanded)
            {
                reported["expanded"] = isExpanded;
            }

            var identifier = NonEmpty(source.Identifier);
            var label = NonEmpty(source.Label);
            var name = NonEmpty(source.ContentDescription) ?? label;
            var entry = new ProjectedNode
            {
                Id = id,
                Ref = StripRef(source.Ref),
                Kind = kind,
                Raw = source,
                Parent = parent,
                Selector = identifier is null ? null : $"id={QuoteTerm(identifier)}",
                States = reported,
            };
            index.Add(entry);
            var childNodes = children.TryGetValue(position, out var below) ? below.Select(child => Build(child, entry)).ToList() : [];

            // A secure field's value is never observed; the tree carries that it is secure, not what it
            // holds. Android echoes a text view's label as its value, which would render every line twice,
            // so that echo is dropped; a field keeps a value equal to its label, since iOS names an
            // unlabeled field by its text and the value is what a test reads.
            var showsValue = !(secure || string.IsNullOrEmpty(value) || (android && !editable && value == source.Label));
            var node = new SemanticNode
            {
                Ref = id,
                Role = role,

                // A device label is the node's visible text and, unless the platform carries an accessible
                // name beside it, its name too: Android names a node by its text and reports the content
                // description an app set beside visible text separately, so a labeled text view or a filled
                // field answers `GetByLabel` by that description and `GetByText` by what it shows. The model
                // rendering elides `Text` whenever it equals `Name`.
                Name = name,
                Text = label,
                Value = showsValue ? value : null,
                InputPurpose = secure ? "password" : null,
                States = new NodeStates
                {
                    Disabled = reported.ContainsKey("disabled"),
                    Selected = reported.ContainsKey("selected"),
                    Focused = reported.ContainsKey("focused"),
                    Hidden = reported.ContainsKey("hidden"),
                    Secure = secure,
                    Checked = checkedState == true,
                    Expanded = expanded == true,
                },
                Placeholder = NonEmpty(source.Placeholder),

                // The accessibility identifier (iOS) or resource id (Android) is the node's test id.
                TestId = identifier,
                Rect = source.Rect is { } rect ? new BoundingBox(rect.X, rect.Y, rect.Width, rect.Height) : null,
                Children = childNodes,
            };
            entry.Node = node;
            return node;
        }

        var rootNodes = roots
            .Where(position => !IsSystemBar(raw[position], viewport, () => DisplayOnly(position)))
            .Select(position => Build(position, null))
            .ToList();
        return new ProjectedSnapshot { Roots = rootNodes, Index = index, Viewport = viewport };
    }

    /// <summary>
    /// The screen's logical size, read off the application or window node when the platform emits one,
    /// else the extent of every rect; null for a snapshot with no geometry at all.
    /// </summary>
    public static ViewportSize? ViewportOf(IReadOnlyList<RawNode> raw)
    {
        // The largest window, not the first: a system bar can be a window too, and a 63-pixel screen would
        // keep every bar as "too tall to be a bar".
        ViewportSize? screen = null;
        foreach (var node in raw)
        {
            if (!ScreenKinds.Contains(KindOf(node)) || node.Rect is not { } rect || rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            if (screen is not { } best || rect.Width * rect.Height > best.Width * best.Height)
            {
                screen = new ViewportSize(rect.Width, rect.Height);
            }
        }

        if (screen is not null)
        {
            return screen;
        }

        double width = 0;
        double height = 0;
        foreach (var node in raw)
        {
            if (node.Rect is { } rect)
            {
                width = Math.Max(width, rect.X + rect.Width);
                height = Math.Max(height, rect.Y + rect.Height);
            }
        }

        return width > 0 && height > 0 ? new ViewportSize(width, height) : null;
    }

    /// <summary>
    /// The one root the contract wants over a device's several top-level elements. Its box is the
    /// viewport; it carries no name of its own, the foreground app being reported as the snapshot's
    /// location.
    /// </summary>
    public static SemanticNode ScreenRoot(IReadOnlyList<SemanticNode> roots, ViewportSize viewport) => new()
    {
        Ref = RootId,
        Role = "screen",
        Rect = new BoundingBox(0, 0, viewport.Width, viewport.Height),
        Children = roots,
    };

    /// <summary>
    /// The visible screen's title: the navigation bar's (iOS) or toolbar's (Android) own label, else the
    /// first text inside it, else its identifier (UIKit names the bar after its title). Null when the
    /// screen has no title bar, which is the honest answer for a launch screen, a full-screen sheet, or
    /// Android's Settings home.
    /// </summary>
    public static string? ScreenTitle(ProjectedSnapshot snapshot)
    {
        var bar = snapshot.Index.FirstOrDefault(entry => entry.Kind == "navigation-bar")
            ?? snapshot.Index.FirstOrDefault(entry => AndroidTitleIds.Any(suffix => entry.Raw.Identifier?.EndsWith(suffix, StringComparison.Ordinal) == true));
        if (bar is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(bar.Raw.Label))
        {
            return bar.Raw.Label;
        }

        var text = snapshot.Index.FirstOrDefault(entry =>
            entry.Node.Role == "text" && !string.IsNullOrWhiteSpace(entry.Raw.Label) && IsWithin(entry, bar));
        if (text is not null)
        {
            return text.Raw.Label;
        }

        return string.IsNullOrWhiteSpace(bar.Raw.Identifier) ? null : bar.Raw.Identifier;
    }

    /// <summary>True when <paramref name="entry"/> is a strict descendant of <paramref name="ancestor"/>.</summary>
    public static bool IsWithin(ProjectedNode entry, ProjectedNode ancestor)
    {
        for (var current = entry.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// React Native has no UIKit trait for <c>checkbox</c>, <c>radio</c>, and the other roles above, so on
    /// iOS it spells the role and the checked or expanded state into the view's accessibility value,
    /// comma-separated and before any value the app set: <c>checkbox, unchecked</c>,
    /// <c>radio button, checked</c>. XCTest then reports an <c>Other</c> whose value says what it is. This
    /// reads that shape back: the role from the leading description, the states from the words after it,
    /// and what follows as the value proper. Null for a value that does not start with a description,
    /// which is every other value.
    /// </summary>
    private static SpelledValue? ReactNativeValue(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var parts = value.Split(", ");
        if (!ReactNativeRoleDescriptions.TryGetValue(parts[0].ToLowerInvariant(), out var role))
        {
            return null;
        }

        var checkedState = default(bool?);
        var expanded = default(bool?);
        var rest = new List<string>();
        var position = 1;
        for (; position < parts.Length; position++)
        {
            var word = parts[position].ToLowerInvariant();
            if (ReactNativeStateWords.TryGetValue(word, out var state))
            {
                checkedState = state.Checked ?? checkedState;
                expanded = state.Expanded ?? expanded;
                continue;
            }

            if (ReactNativeLooseStateWords.Contains(word))
            {
                rest.Add(parts[position]);
                continue;
            }

            break;
        }

        rest.AddRange(parts.Skip(position));
        return new SpelledValue(role, new SpelledStates(checkedState, expanded), rest.Count == 0 ? null : string.Join(", ", rest));
    }

    /// <summary>
    /// Platform element type of one raw node as a kebab-case token: XCTest sends <c>NavigationBar</c> and
    /// <c>StaticText</c>, Android sends <c>android.widget.TextView</c>; both read as one vocabulary here,
    /// the Android package prefix dropped. <c>Role</c> is the fallback some platforms send instead.
    /// </summary>
    private static string KindOf(RawNode raw) => NormalizeKind(raw.Type ?? raw.Role ?? "");

    /// <summary>True when a raw type is a qualified Android class name.</summary>
    private static bool IsAndroidClass(string? type) => type is not null && type.Contains('.', StringComparison.Ordinal);

    /// <summary>
    /// Contract role for one platform element type. XCTest reports the items of a tab bar or segmented
    /// control as plain buttons; the parent says what they are, so a <c>tablist</c> scope finds its
    /// <c>tab</c> children as it does on the web.
    /// </summary>
    private static string? RoleOf(string kind, bool android, string? parentKind)
    {
        if (kind.Length == 0)
        {
            return null;
        }

        if (android)
        {
            return AndroidRoleMap.GetValueOrDefault(kind) ?? (kind.EndsWith("layout", StringComparison.Ordinal) ? "group" : kind);
        }

        if (kind == "button" && parentKind is not null && TabContainerKinds.Contains(parentKind))
        {
            return "tab";
        }

        return RoleMap.GetValueOrDefault(kind) ?? kind;
    }

    /// <summary>
    /// The role an app declared beside the platform's class: Android's heading flag (React Native's
    /// <c>accessibilityRole="header"</c>), else the localized role description React Native writes for the
    /// roles Android has no class for (<c>Tab</c>, <c>Tab List</c>, <c>Radio Group</c>), read through the
    /// words it spells into an iOS value. Null when neither names a contract role.
    /// </summary>
    private static string? DescribedRole(RawNode source)
    {
        if (source.Heading == true)
        {
            return "heading";
        }

        return source.RoleDescription is null ? null : ReactNativeRoleDescriptions.GetValueOrDefault(source.RoleDescription.ToLowerInvariant());
    }

    /// <summary>The string itself, or null for an absent or empty one.</summary>
    private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// A node the platform reports as editable takes typed text whatever its class, so a custom input view,
    /// or a class the maps do not know, is a <c>textbox</c> rather than its verbatim kind; a role that
    /// already takes text is kept. The flag outranks a control role too: Android sets it from
    /// <c>TextView.isTextEditable()</c>, so a <c>Button</c> reporting it is a text view configured for
    /// input, and typed text and secrets have to reach it.
    /// </summary>
    private static string? EditableRole(string? role, bool? editable)
    {
        if (editable != true)
        {
            return role;
        }

        return role is not null && TextInputRoles.Contains(role) ? role : "textbox";
    }

    private static bool? CheckedOf(string? role, string? value)
    {
        if (role is null || !CheckableRoles.Contains(role) || value is null)
        {
            return null;
        }

        var lowered = value.Trim().ToLowerInvariant();
        if (CheckedValues.Contains(lowered))
        {
            return true;
        }

        return UncheckedValues.Contains(lowered) ? false : null;
    }

    private static string StripRef(string? reference) =>
        reference is null ? "" : reference.StartsWith('@') ? reference[1..] : reference;

    /// <summary>
    /// Parent position of every raw node. <c>parentIndex</c> is authoritative when it names a node in the
    /// list; otherwise <c>depth</c> reconstructs nesting from the document order agent-device emits; a node
    /// with neither is a root.
    /// </summary>
    private static List<int?> ParentPositions(IReadOnlyList<RawNode> raw)
    {
        var byIndex = new Dictionary<int, int>();
        for (var position = 0; position < raw.Count; position++)
        {
            if (raw[position].Index is { } index)
            {
                byIndex[index] = position;
            }
        }

        var lastAtDepth = new List<int?>();
        var parents = new List<int?>(raw.Count);
        for (var position = 0; position < raw.Count; position++)
        {
            var node = raw[position];
            int? parent = null;
            if (node.ParentIndex is >= 0 and var parentIndex && byIndex.TryGetValue(parentIndex, out var resolved) && resolved < position)
            {
                parent = resolved;
            }

            if (parent is null && node.Depth is > 0 and var above)
            {
                parent = above - 1 < lastAtDepth.Count ? lastAtDepth[above - 1] : null;
            }

            if (node.Depth is { } depth)
            {
                while (lastAtDepth.Count <= depth)
                {
                    lastAtDepth.Add(null);
                }

                lastAtDepth[depth] = position;
                lastAtDepth.RemoveRange(depth + 1, lastAtDepth.Count - depth - 1);
            }

            parents.Add(parent);
        }

        return parents;
    }

    /// <summary>
    /// Whether a top-level element is one of Android's system bars: the status bar along the top edge
    /// (clock, signal, battery) or the three-button navigation bar along the bottom, each a window of
    /// <c>com.android.systemui</c>. Neither belongs to the app under test, and what they show follows the
    /// device's clock and radio: an end anchor on "T-Mobile, three bars" handed every replay of a recording
    /// off to the model once the emulator read "signal full". The notification shade, quick settings, and a
    /// system dialog are systemui windows too, but they cover the screen instead of hugging an edge, and
    /// stay. So does a heads-up notification or any other compact systemui popup: a bar spans the screen
    /// edge to edge, a popup sits inset from the sides, and its controls are worth acting on.
    /// <para>
    /// agent-device 0.21.15 reports the status bar's icons (clock, signal, battery, a notification icon) as
    /// top-level nodes of their own rather than children of one bar, so a narrow systemui node lying wholly
    /// inside the edge band, built only of text, images, and layouts, is the bar too. A popup is wide or
    /// holds a control, and stays.
    /// </para>
    /// </summary>
    private static bool IsSystemBar(RawNode node, ViewportSize? screen, Func<bool> displayOnly)
    {
        if (node.BundleId != SystemUiPackage || node.Rect is not { } rect || screen is not { } size)
        {
            return false;
        }

        var band = size.Height * StatusIconBandShare;
        var inEdgeBand = rect.Y + rect.Height <= band || rect.Y >= size.Height - band;
        if (inEdgeBand && rect.Width < size.Width * StatusIconMaxWidthShare && displayOnly())
        {
            return true;
        }

        if (rect.Height >= size.Height * SystemBarMaxShare || rect.Width < size.Width * SystemBarMinWidthShare)
        {
            return false;
        }

        return rect.Y <= 0 || rect.Y + rect.Height >= size.Height;
    }

    /// <summary>Quotes one selector term value the way agent-device's parser reads it back.</summary>
    private static string QuoteTerm(string value) => QuotedTermCharacters().IsMatch(value) ? JsonText.Quote(value) : value;

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex WordBoundary();

    [GeneratedRegex(@"[\s_]+")]
    private static partial Regex Separators();

    [GeneratedRegex("[\\s\"]")]
    private static partial Regex QuotedTermCharacters();

    /// <summary>The states React Native spelled into an iOS accessibility value.</summary>
    private sealed record SpelledStates(bool? Checked = null, bool? Expanded = null);

    /// <summary>What React Native spelled into an iOS accessibility value.</summary>
    private sealed record SpelledValue(string Role, SpelledStates States, string? Value);
}
