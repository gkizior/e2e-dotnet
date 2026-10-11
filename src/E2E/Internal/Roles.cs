// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Internal;

/// <summary>The role sets the agent reads a node against, as upstream's <c>internal/roles.ts</c>.</summary>
internal static class Roles
{
    /// <summary>Roles that take typed text: what a secret may fill.</summary>
    private static readonly HashSet<string> EditableRoles = new(["textbox", "searchbox", "combobox"], StringComparer.Ordinal);

    /// <summary>True for a node of an editable role.</summary>
    public static bool IsEditable(SemanticNode node) => EditableRoles.Contains(node.Role ?? "");
}
