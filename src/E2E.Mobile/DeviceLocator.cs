// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile;

/// <summary>
/// Locator expression evaluation over one projected snapshot: the location capability's query language
/// answered against the accessibility tree the device just reported. One immediate pass, every match in
/// document order; polling, strictness, and staleness stay with the runner.
/// </summary>
internal static class DeviceLocator
{
    /// <summary>Resolves one expression to the nodes it currently matches; <paramref name="platform"/> is the one agent-device selectors match for.</summary>
    public static List<ProjectedNode> ResolveExpression(LocatorQuery expression, IReadOnlyList<ProjectedNode> index, MobilePlatform platform)
    {
        switch (expression.Kind)
        {
            case "filter":
                return Filter(expression, index, platform);
            case "index":
                return Pick(expression, index, platform);
            case "selector":
                return [.. AgentDeviceSelector.Compile(expression.Selector!, platform)(index)];
            case "frame":
                throw new EngineException(
                    EngineErrorCodes.FrameNotFound,
                    "a device surface has no nested documents to scope a query into",
                    retryable: false);
            default:
                return Query(expression, index, platform);
        }
    }

    private static List<ProjectedNode> Filter(LocatorQuery expression, IReadOnlyList<ProjectedNode> index, MobilePlatform platform)
    {
        var source = ResolveExpression(expression.Source!, index, platform);
        return source.Where(entry =>
        {
            if (expression.HasText is not null && !SubtreeHasText(entry, index, expression.HasText))
            {
                return false;
            }

            return expression.Has is null || ResolveExpression(expression.Has, DescendantsOf([entry], index), platform).Count > 0;
        }).ToList();
    }

    private static List<ProjectedNode> Pick(LocatorQuery expression, IReadOnlyList<ProjectedNode> index, MobilePlatform platform)
    {
        var source = ResolveExpression(expression.Source!, index, platform);
        var position = expression.Index == LocatorQuery.LastIndex ? source.Count - 1 : expression.Index!.Value;
        return position >= 0 && position < source.Count ? [source[position]] : [];
    }

    private static List<ProjectedNode> Query(LocatorQuery expression, IReadOnlyList<ProjectedNode> index, MobilePlatform platform)
    {
        var candidates = expression.Parent is null ? index : DescendantsOf(ResolveExpression(expression.Parent, index, platform), index);
        var matches = candidates.Where(entry => MatchesQuery(entry, expression)).ToList();
        return EchoedQueryKinds.Contains(expression.Kind) ? InnermostOnly(matches) : matches;
    }

    /// <summary>The query kinds a device answers with the innermost match, because it echoes what they match on up the tree.</summary>
    private static readonly HashSet<string> EchoedQueryKinds = ["text", "label"];

    /// <summary>
    /// Drops every match that contains another match, the way a browser's text selector answers with the
    /// innermost element. A device tree echoes text and labels upwards: iOS reports a React Native Text host
    /// view and its StaticText child with the same label, a TextInput host view and the text field inside
    /// it with the same label, and a container view inherits its descendants' labels, so without this rule
    /// every <c>GetByText</c> and <c>GetByLabel</c> on such a screen is ambiguous.
    /// </summary>
    private static List<ProjectedNode> InnermostOnly(List<ProjectedNode> matches) =>
        matches.Where(entry => !matches.Any(other => !ReferenceEquals(other, entry) && SnapshotProjection.IsWithin(other, entry))).ToList();

    /// <summary>Strict descendants of any node in <paramref name="ancestors"/>, in document order.</summary>
    private static List<ProjectedNode> DescendantsOf(IReadOnlyList<ProjectedNode> ancestors, IReadOnlyList<ProjectedNode> index)
    {
        if (ancestors.Count == 0)
        {
            return [];
        }

        return index.Where(entry => ancestors.Any(ancestor => SnapshotProjection.IsWithin(entry, ancestor))).ToList();
    }

    private static bool SubtreeHasText(ProjectedNode entry, IReadOnlyList<ProjectedNode> index, TextMatch pattern)
    {
        bool Has(ProjectedNode node) =>
            new[] { node.Node.Name, node.Node.Text, node.Node.Value }.Any(text => text is not null && pattern.Matches(text, exact: false));

        return Has(entry) || DescendantsOf([entry], index).Any(Has);
    }

    /// <summary>
    /// One node against one semantic query. Role queries skip hidden nodes, as a browser's role query does;
    /// the other kinds answer with every node and leave visibility to the action or assertion, unless the
    /// query says <c>visible</c>, which drops hidden nodes for every kind.
    /// </summary>
    private static bool MatchesQuery(ProjectedNode entry, LocatorQuery query)
    {
        var node = entry.Node;
        if (query.Visible && node.States.Hidden)
        {
            return false;
        }

        switch (query.Kind)
        {
            case "role":
                if ((node.Role ?? "") != query.Role)
                {
                    return false;
                }

                if (query.Value is not null && !query.Value.Matches(node.Name ?? "", query.Exact))
                {
                    return false;
                }

                if (node.States.Hidden)
                {
                    return false;
                }

                var states = node.States;
                if (!StateIs(query.Checked, states.Checked)
                    || !StateIs(query.Disabled, states.Disabled)
                    || !StateIs(query.Selected, states.Selected)
                    || !StateIs(query.Expanded, states.Expanded)
                    || !StateIs(query.Pressed, states.Pressed))
                {
                    return false;
                }

                // A device tree reports no heading levels, so a level query matches nothing rather than everything.
                return query.Level is null || node.Level == query.Level;
            case "label":
                return node.Name is not null && query.Value!.Matches(node.Name, query.Exact);
            case "placeholder":
                return node.Placeholder is not null && query.Value!.Matches(node.Placeholder, query.Exact);
            case "text":
                return (node.Name is not null && query.Value!.Matches(node.Name, query.Exact))
                    || (node.Text is not null && query.Value!.Matches(node.Text, query.Exact));
            case "displayValue":
                return node.Value is not null && query.Value!.Matches(node.Value, query.Exact);
            case "testid":
                return node.TestId is not null && query.Value!.Matches(node.TestId, exact: true);
            default:
                return false;
        }
    }

    private static bool StateIs(bool? wanted, bool actual) => wanted is null || wanted.Value == actual;
}
