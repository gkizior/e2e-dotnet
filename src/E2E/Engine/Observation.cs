// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

/// <summary>Limits from the engine contract. Engines cut names and text before they reach the model.</summary>
public static class ObservationLimits
{
    public const int Name = 256;
    public const int Text = 512;
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

/// <summary>One node of an observation tree. <see cref="Ref"/> is valid only for the observation it came from.</summary>
public sealed class SemanticNode
{
    public required string Ref { get; init; }

    public string? Role { get; init; }

    public string? Name { get; init; }

    public string? Text { get; init; }

    public string? Value { get; init; }

    public string? TestId { get; init; }

    public string? Placeholder { get; init; }

    /// <summary>What a field takes: <c>username</c>, <c>password</c>, <c>one-time-code</c>, <c>generic-secret</c>, or <c>none</c>.</summary>
    public string? InputPurpose { get; init; }

    public int? Level { get; init; }

    public NodeStates States { get; init; } = new();

    public IReadOnlyList<SemanticNode> Children { get; init; } = [];
}

/// <summary>A redacted semantic snapshot of the current screen.</summary>
public sealed class Observation
{
    public required string Route { get; init; }

    public required IReadOnlyList<SemanticNode> Roots { get; init; }
}
