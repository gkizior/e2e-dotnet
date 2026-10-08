// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.LocatorBasics;

/// <summary>The list items upstream's locator-basics tests read.</summary>
internal static class Items
{
    public static readonly SemanticNode[] List = [Item("a", "  Item \n Alpha "), Item("b", "Item Beta"), Item("c", "Item Gamma")];

    public static readonly SemanticNode Shown = Item("shown", "Shown");

    public static readonly SemanticNode Hidden = Item("hidden", "Hidden", new NodeStates { Hidden = true });

    public static SemanticNode Item(string id, string? text, NodeStates? states = null) =>
        new() { Ref = id, Role = "listitem", Text = text, States = states ?? new NodeStates() };

    public static async Task<TestException> FailsWith(string code, Func<Task> run)
    {
        var error = await Assert.ThrowsAsync<TestException>(run);
        Assert.Equal(code, error.Code);
        return error;
    }
}
