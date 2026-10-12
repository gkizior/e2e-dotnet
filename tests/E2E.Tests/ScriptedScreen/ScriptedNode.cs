// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ScriptedScreen;

/// <summary>One option a select picks from.</summary>
internal sealed record ScriptedOption(string Label, string Value);

/// <summary>
/// One node of the scripted tree: a mutable <see cref="SemanticNode"/> addressed by id, plus the script's own
/// knobs. Hooks and scheduled mutations edit these objects in place; the scene reads them live on every
/// observation.
/// </summary>
internal sealed class ScriptedNode
{
    public required string Id { get; init; }

    public string? Role { get; init; }

    public string? Name { get; set; }

    public string? Text { get; set; }

    public string? Value { get; set; }

    public string? TestId { get; init; }

    public string? InputPurpose { get; init; }

    public int? Level { get; init; }

    public bool Checked { get; set; }

    public bool Disabled { get; set; }

    public bool Expanded { get; set; }

    public bool Focused { get; set; }

    public bool Hidden { get; set; }

    public bool Secure { get; init; }

    public bool Selected { get; init; }

    public bool Pressed { get; set; }

    public IReadOnlyDictionary<string, string> Attributes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public BoundingBox? Rect { get; init; }

    public List<ScriptedNode> Children { get; init; } = [];

    /// <summary>Options a select picks from, in order.</summary>
    public IReadOnlyList<ScriptedOption> Options { get; init; } = [];

    /// <summary>Reacts to an action the scene performed on the node, after the built-in semantics ran.</summary>
    public Action<LocatorAction, ScriptedNode>? On { get; init; }
}
