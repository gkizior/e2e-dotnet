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

    public bool Hidden { get; init; }

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

    public string? Text { get; init; }

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
