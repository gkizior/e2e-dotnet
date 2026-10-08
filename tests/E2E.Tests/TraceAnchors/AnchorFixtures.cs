// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.TraceAnchors;

/// <summary>Nodes and checks the anchor tests share.</summary>
internal static class AnchorFixtures
{
    private static int _refs;

    internal static readonly SemanticNode Heading = Node("heading", "Playbooks");
    internal static readonly SemanticNode EmptyMarker = Node("status", "Marker", text: "empty");
    internal static readonly SemanticNode SavedMarker = Node("status", "Marker", text: "saved");
    internal static readonly SemanticNode Row = Node("link", "PB-Twin-Alpha");
    internal static readonly SemanticNode Toast = Node(text: "Playbook saved");

    internal static SemanticNode Node(
        string? role = null,
        string? name = null,
        string? text = null,
        string? value = null,
        NodeStates? states = null,
        string? testId = null,
        IReadOnlyList<SemanticNode>? children = null)
    {
        return new SemanticNode
        {
            Ref = "e" + Interlocked.Increment(ref _refs),
            Role = role,
            Name = name,
            Text = text,
            Value = value,
            TestId = testId,
            States = states ?? new NodeStates(),
            Children = children ?? [],
        };
    }

    internal static RecordedTarget Anchor(string? role = null, string? name = null, string? text = null, string? value = null, List<string>? states = null, string? testId = null)
    {
        return new RecordedTarget { Role = role, Name = name, Text = text, Value = value, States = states, TestId = testId };
    }

    internal static List<AnchorNode> Project(IEnumerable<SemanticNode> nodes, Func<string, string>? record = null)
    {
        return Anchors.Project(new Observation { Route = "/", Roots = nodes.ToList() }, _ => false, record ?? (value => value), value => value);
    }

    internal static (List<RecordedTarget> Appeared, List<RecordedTarget> Gone) Describe(IEnumerable<SemanticNode> start, IEnumerable<SemanticNode> end, bool routeMoved = false)
    {
        return Anchors.Describe(Project(start), Project(end), routeMoved);
    }

    internal static bool Holds(List<RecordedTarget> appeared, IEnumerable<SemanticNode> live, IEnumerable<SemanticNode>? before = null, List<RecordedTarget>? gone = null)
    {
        return Anchors.Holds(new CacheEntry { Appeared = appeared, Gone = gone ?? [] }, Project(live), Project(before ?? []));
    }

    /// <summary>An anchor as one line: <c>role|name</c>, then <c>~text</c>, <c>#testId</c>, <c>=value</c>, and <c>|states</c> when present.</summary>
    internal static string Shape(RecordedTarget anchor)
    {
        var shape = anchor.Role + "|" + anchor.Name;
        if (anchor.Text is not null)
        {
            shape += "~" + anchor.Text;
        }

        if (anchor.TestId is not null)
        {
            shape += "#" + anchor.TestId;
        }

        if (anchor.Value is not null)
        {
            shape += "=" + anchor.Value;
        }

        if (anchor.States is not null)
        {
            shape += "|" + string.Join(',', anchor.States);
        }

        return shape;
    }

    internal static List<string> Shapes(IEnumerable<RecordedTarget> anchors) => anchors.Select(Shape).ToList();
}
