// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests;

/// <summary>The steps most surface tests share: open an attempt, look at the screen, find a node by its name.</summary>
internal static class Screens
{
    /// <summary>Boots a worker and launches the pinned app the way a test's <c>app.open()</c> does.</summary>
    public static async Task<DeviceSession> OpenAttempt(Harness h)
    {
        var session = await h.Boot();
        if (h.App.BundleId is not null || h.App.AppPath is not null)
        {
            await session.RestartAsync(CancellationToken.None);
        }

        return session;
    }

    /// <summary>A fresh observation of the screen, as the screen root.</summary>
    public static async Task<SemanticNode> RootOf(DeviceSession session) =>
        (await session.ObserveAsync(CancellationToken.None)).Roots[0];

    /// <summary>The observed node with this name, from a fresh observation.</summary>
    public static async Task<SemanticNode> Observed(DeviceSession session, string name) => Named(await RootOf(session), name);

    public static SemanticNode Named(SemanticNode root, string name) =>
        Walk(root).FirstOrDefault(node => node.Name == name) ?? throw new InvalidOperationException($"no node named {name}");

    /// <summary>Every node under the root, the root excluded: the device's own tree.</summary>
    public static IEnumerable<SemanticNode> Walk(SemanticNode root)
    {
        foreach (var node in root.Children)
        {
            yield return node;
            foreach (var inner in Walk(node))
            {
                yield return inner;
            }
        }
    }

    /// <summary>The button the tests add to the Settings screen to stand for a control that arrives with an action.</summary>
    public static RawNode Submit(string @ref = "@e11", double y = 600, string label = "Submit", string type = "button", string? identifier = null, int index = 10) =>
        RawNodes.N(@ref, index: index, parentIndex: 0, depth: 1, type: type, label: label, identifier: identifier, rect: new Rect(0, y, 390, 44));
}
