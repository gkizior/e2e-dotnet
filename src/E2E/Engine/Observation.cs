// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

/// <summary>Limits from the engine contract. Engines cut names and text before they reach the model.</summary>
public static class ObservationLimits
{
    public const int Name = 256;
    public const int Text = 512;

    /// <summary>Most nodes one web observation lists, across every frame of the page.</summary>
    public const int Nodes = 3000;
}

/// <summary>Boolean states a platform can report for one node.</summary>
public sealed class NodeStates
{
    public bool Checked { get; init; }

    public bool Disabled { get; init; }

    public bool Expanded { get; init; }

    public bool Focused { get; init; }

    /// <summary>The node does not render: <c>display: none</c>, <c>visibility</c>, skipped content, a closed <c>details</c>. This is what <c>isVisible</c> reads.</summary>
    public bool Hidden { get; init; }

    /// <summary>The node, or an ancestor, is <c>aria-hidden</c>. It may still paint, so it can be visible, but role queries and the agent skip it.</summary>
    public bool AriaHidden { get; init; }

    public bool Secure { get; init; }

    public bool Selected { get; init; }

    public bool Pressed { get; init; }
}

/// <summary>A viewport-relative rectangle in CSS pixels.</summary>
public sealed record BoundingBox(double X, double Y, double Width, double Height);

/// <summary>One node of an observation tree. <see cref="Ref"/> is valid only for the observation it came from.</summary>
public sealed class SemanticNode
{
    public required string Ref { get; init; }

    public string? Role { get; init; }

    /// <summary>The accessible name, by the accname rules Playwright follows, or null when the node has none.</summary>
    public string? Name { get; init; }

    /// <summary>The rendered text, which locator reads and text queries use.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// The text the node owns, read as a line: its inline words stay in place, but its listed children's text is left out. The snapshot shows it.
    /// Null when the node has no split: <see cref="Text"/> then reads whole.
    /// </summary>
    public string? OwnText { get; init; }

    /// <summary>
    /// The inline elements read in <see cref="Text"/> that have no node of their own, such as the <c>&lt;span&gt;</c> in
    /// <c>Ticket A &lt;span&gt;urgent&lt;/span&gt;</c>. A text query resolves them under this node, so it still finds the
    /// word, as Playwright's text engine does. They are not in <see cref="Children"/>, so the agent does not read them twice.
    /// </summary>
    internal IReadOnlyList<SemanticNode> InlineNodes { get; init; } = [];

    public string? Value { get; init; }

    public string? TestId { get; init; }

    public string? Placeholder { get; init; }

    /// <summary>What a field takes: <c>username</c>, <c>password</c>, <c>one-time-code</c>, <c>generic-secret</c>, or <c>none</c>.</summary>
    public string? InputPurpose { get; init; }

    public int? Level { get; init; }

    public NodeStates States { get; init; } = new();

    /// <summary>
    /// Platform attributes by name, what <c>getAttribute</c> and <c>toHaveAttribute</c> read.
    /// A secure field never reports its value.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The attribute that carries <see cref="TestId"/>. The tree reports the id as a field, so <see cref="Attributes"/> leaves it out.</summary>
    internal string? TestIdAttribute { get; init; }

    /// <summary>Reads one attribute, the test-id attribute included.</summary>
    internal string? AttributeOf(string name) =>
        Attributes.TryGetValue(name, out var value) ? value : name == TestIdAttribute ? TestId : null;

    /// <summary>The node's box in viewport CSS pixels, when the platform measures one.</summary>
    public BoundingBox? Rect { get; init; }

    public IReadOnlyList<SemanticNode> Children { get; init; } = [];
}

/// <summary>A redacted semantic snapshot of the current screen.</summary>
public sealed class Observation
{
    public required string Route { get; init; }

    public required IReadOnlyList<SemanticNode> Roots { get; init; }

    /// <summary>True when the engine left nodes out to stay within its node budget. Scrolling brings them in.</summary>
    public bool Truncated { get; init; }

    /// <summary>
    /// Where the engine's viewport and scrolled elements stand, when it can tell. Paging
    /// compares it to see whether a scroll moved anything the tree does not show.
    /// </summary>
    internal string? ScrollPosition { get; init; }
}
