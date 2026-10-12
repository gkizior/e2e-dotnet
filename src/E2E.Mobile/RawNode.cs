// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile;

/// <summary>
/// One agent-device snapshot node as this engine reads it: the fields agent-device's <c>SnapshotNode</c>
/// carries that the projection uses, every one optional, since the projection tolerates each one missing
/// (a fixture, a producer that does not read it). A flag that is null is unavailable, not false.
/// </summary>
public sealed record RawNode
{
    /// <summary>The agent-device ref, with or without its <c>@</c> prefix.</summary>
    public string? Ref { get; init; }

    public int? Index { get; init; }

    public int? ParentIndex { get; init; }

    public int? Depth { get; init; }

    /// <summary>The platform class: <c>NavigationBar</c> on iOS, <c>android.widget.TextView</c> on Android.</summary>
    public string? Type { get; init; }

    /// <summary>The fallback some platforms send instead of <see cref="Type"/>.</summary>
    public string? Role { get; init; }

    /// <summary>The role agent-device itself presents for the node, when it publishes one.</summary>
    public string? Kind { get; init; }

    public string? Label { get; init; }

    public string? Value { get; init; }

    /// <summary>The Android content description when it is not already the label.</summary>
    public string? ContentDescription { get; init; }

    /// <summary>The accessibility identifier (iOS) or resource id (Android).</summary>
    public string? Identifier { get; init; }

    public Rect? Rect { get; init; }

    public bool? Enabled { get; init; }

    public bool? Selected { get; init; }

    /// <summary>The checked state of a checkable control (switch, checkbox, radio); null means not checkable or unavailable.</summary>
    public bool? Checked { get; init; }

    public bool? Focused { get; init; }

    /// <summary>The accessibility heading flag an app set on the node.</summary>
    public bool? Heading { get; init; }

    /// <summary>The localized role description an app set beside the native class, verbatim (<c>Tab</c>, <c>Tab List</c>, <c>Link</c>).</summary>
    public string? RoleDescription { get; init; }

    public bool? Editable { get; init; }

    public bool? Password { get; init; }

    public bool? HintShowing { get; init; }

    /// <summary>The placeholder text of a text field (the Android hint), whether or not the field is showing it.</summary>
    public string? Placeholder { get; init; }

    public bool? VisibleToUser { get; init; }

    public bool? Hittable { get; init; }

    public string? BundleId { get; init; }

    public string? AppName { get; init; }

    public string? WindowTitle { get; init; }
}

/// <summary>One projected node with what the surface needs to act on it and to answer selector terms.</summary>
public sealed class ProjectedNode
{
    public required string Id { get; init; }

    /// <summary>The agent-device ref without its <c>@</c> prefix; empty for a node the runner cannot act on.</summary>
    public required string Ref { get; init; }

    /// <summary>The platform element type, lower-cased (<c>text-field</c>, <c>cell</c>, <c>navigation-bar</c>).</summary>
    public required string Kind { get; init; }

    public required RawNode Raw { get; init; }

    public ProjectedNode? Parent { get; init; }

    /// <summary>The structural hint for tuned replay policies: an identifier survives relabeling, a label does not anchor. Null without an identifier.</summary>
    public string? Selector { get; init; }

    /// <summary>
    /// The states the platform reported, by name. A state with no entry is unknown, which is not the same
    /// as false: an unchecked switch reports <c>checked: false</c>, a node with no such state reports none.
    /// <see cref="SemanticNode.States"/> cannot tell the two apart.
    /// </summary>
    public IReadOnlyDictionary<string, bool> States { get; init; } = new Dictionary<string, bool>();

    /// <summary>The node as the contract reports it. Set once its children are projected.</summary>
    public SemanticNode Node { get; internal set; } = null!;
}

public sealed class ProjectedSnapshot
{
    /// <summary>The device's top-level elements (windows, the application node); the surface wraps them in one root.</summary>
    public required IReadOnlyList<SemanticNode> Roots { get; init; }

    /// <summary>Every node in document order, parents before children.</summary>
    public required IReadOnlyList<ProjectedNode> Index { get; init; }

    public ViewportSize? Viewport { get; init; }
}
