// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace E2E.Mobile;

/// <summary>One parsed <c>press</c> key: its modifiers, and either a named key or a single character.</summary>
internal sealed record ParsedKey(IReadOnlyList<string> Modifiers, string? Name, string? Character);

/// <summary>
/// The key grammar of <c>press</c>, shared by every engine so a test's key names are portable: zero or more modifiers
/// and one key, joined by <c>+</c>. A modifier is <c>Shift</c>, <c>Control</c>, <c>Alt</c>, <c>Meta</c>, or
/// <c>ControlOrMeta</c>. A named key is one of <see cref="Names"/>. Any single printable character is typed as itself.
/// A platform without a key refuses it. It never reinterprets the spelling.
/// </summary>
internal static class DeviceKeys
{
    public static IReadOnlyList<string> Modifiers { get; } = ["Shift", "Control", "Alt", "Meta", "ControlOrMeta"];

    public static IReadOnlyList<string> Names { get; } =
    [
        "Enter", "Escape", "Tab", "Backspace", "Delete", "Insert", "Space", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
        "Home", "End", "PageUp", "PageDown", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
    ];

    /// <summary>
    /// Parses a <c>press</c> key against the grammar. Null when it does not conform: an unknown name, a repeated
    /// modifier, a bare modifier, or several characters. A literal <c>+</c> is the character after the last separator,
    /// so <c>Shift++</c> presses <c>+</c> with Shift held.
    /// </summary>
    public static ParsedKey? Parse(string key)
    {
        if (key.Length == 0)
        {
            return null;
        }

        if (key == "+")
        {
            return new ParsedKey([], null, "+");
        }

        // A trailing + is the key itself, so the separator before it (the last two characters) is cut and + appended as the final part.
        string[] parts = key.EndsWith('+') && key.Length > 1 ? [.. key[..^2].Split('+'), "+"] : key.Split('+');
        var last = parts[^1];
        if (last.Length == 0)
        {
            return null;
        }

        var modifiers = new List<string>();
        foreach (var part in parts[..^1])
        {
            if (!Modifiers.Contains(part) || modifiers.Contains(part))
            {
                return null;
            }

            modifiers.Add(part);
        }

        if (Names.Contains(last))
        {
            return new ParsedKey(modifiers, last, null);
        }

        return new StringInfo(last).LengthInTextElements == 1 && last.EnumerateRunes().Count() == 1 && IsPrintable(last)
            ? new ParsedKey(modifiers, null, last)
            : null;
    }

    /// <summary>Control, format, surrogate, private-use, unassigned, and line or paragraph separators are never a printable key.</summary>
    private static bool IsPrintable(string character) =>
        !character.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
            or UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
}
