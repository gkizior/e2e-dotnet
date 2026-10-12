// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests;

/// <summary>Builders for the snapshot nodes the tests project, and the projection they run through.</summary>
internal static class RawNodes
{
    /// <summary>One raw node, from the fields a test names.</summary>
    public static RawNode N(
        string? @ref = null,
        int? index = null,
        int? parentIndex = null,
        int? depth = null,
        string? type = null,
        string? label = null,
        string? value = null,
        string? contentDescription = null,
        string? identifier = null,
        Rect? rect = null,
        bool? enabled = null,
        bool? selected = null,
        bool? @checked = null,
        bool? focused = null,
        bool? heading = null,
        string? roleDescription = null,
        bool? editable = null,
        bool? password = null,
        bool? hintShowing = null,
        string? placeholder = null,
        bool? visibleToUser = null,
        bool? hittable = null,
        string? bundleId = null) => new()
        {
            Ref = @ref,
            Index = index,
            ParentIndex = parentIndex,
            Depth = depth,
            Type = type,
            Label = label,
            Value = value,
            ContentDescription = contentDescription,
            Identifier = identifier,
            Rect = rect,
            Enabled = enabled,
            Selected = selected,
            Checked = @checked,
            Focused = focused,
            Heading = heading,
            RoleDescription = roleDescription,
            Editable = editable,
            Password = password,
            HintShowing = hintShowing,
            Placeholder = placeholder,
            VisibleToUser = visibleToUser,
            Hittable = hittable,
            BundleId = bundleId,
        };

    public static Rect R(double x, double y, double width, double height) => new(x, y, width, height);

    /// <summary>Projects nodes the way the surface does, minting <c>n1</c>, <c>n2</c>, ... in document order.</summary>
    public static ProjectedSnapshot Project(params RawNode[] nodes)
    {
        var counter = 0;
        return SnapshotProjection.ProjectSnapshot(nodes, () => $"n{++counter}");
    }

    /// <summary>The states a node reported, as a comparable value.</summary>
    public static IReadOnlyDictionary<string, bool> S(params (string State, bool Value)[] states) =>
        states.ToDictionary(state => state.State, state => state.Value);

    /// <summary>An iOS Settings-like screen: app root, navigation bar, a cell, a switch, two fields, a hidden button.</summary>
    public static readonly RawNode[] SettingsNodes =
    [
        N("@e1", index: 0, depth: 0, type: "application", label: "Settings", rect: R(0, 0, 390, 844)),
        N("@e2", index: 1, parentIndex: 0, depth: 1, type: "navigation-bar", label: "General", rect: Row(47, 44)),
        N("@e3", index: 2, parentIndex: 1, depth: 2, type: "button", label: "Back", identifier: "BackButton", rect: Row(47)),
        N("@e4", index: 3, parentIndex: 0, depth: 1, type: "cell", label: "About", identifier: "ABOUT", rect: Row(120)),
        N("@e5", index: 4, parentIndex: 3, depth: 2, type: "static-text", label: "About", rect: Row(120)),
        N("@e6", index: 5, parentIndex: 0, depth: 1, type: "switch", label: "Airplane Mode", value: "0", rect: Row(170)),
        N("@e7", index: 6, parentIndex: 0, depth: 1, type: "text-field", label: "Search", value: "wifi", rect: Row(220)),
        N("@e8", index: 7, parentIndex: 0, depth: 1, type: "secure-text-field", label: "Password", value: "hunter2", rect: Row(270)),
        N("@e9", index: 8, parentIndex: 0, depth: 1, type: "button", label: "Hidden", visibleToUser: false, enabled: false, rect: Row(900)),
        N("@e10", index: 9, parentIndex: 0, depth: 1, type: "cell", label: "Scroller", rect: Row(320, 400)),
    ];

    private static Rect Row(double y, double height = 44) => new(0, y, 390, height);
}
