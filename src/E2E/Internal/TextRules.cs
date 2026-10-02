// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Internal;

/// <summary>
/// Whitespace and string matching used by locators. Exact string matches are
/// case-sensitive after whitespace is collapsed. Inexact matches are
/// case-insensitive substrings, which is how <c>exact: false</c> behaves upstream.
/// </summary>
internal static class TextRules
{
    public static string Normalize(string text)
    {
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts).Trim();
    }

    public static bool Matches(string? actual, string expected, bool exact)
    {
        if (actual is null)
        {
            return false;
        }

        var left = Normalize(actual);
        var right = Normalize(expected);
        if (exact)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }

        return left.Contains(right, StringComparison.OrdinalIgnoreCase);
    }

    public static bool Contains(string? actual, string expected)
    {
        if (actual is null)
        {
            return false;
        }

        return Normalize(actual).Contains(Normalize(expected), StringComparison.Ordinal);
    }

    public static string Truncate(string value, int limit)
    {
        if (value.Length <= limit)
        {
            return value;
        }

        return value[..limit];
    }
}
