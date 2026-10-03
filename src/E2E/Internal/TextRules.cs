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

    /// <summary>
    /// The text matcher rule. A pattern is tested anywhere in the subject in both modes. A string
    /// equals, or is contained by, the subject, folding case when <paramref name="ignoreCase"/> is true.
    /// <paramref name="normalize"/> collapses whitespace on both sides first.
    /// </summary>
    public static bool Compare(string actual, TextMatch expected, bool contains, bool normalize, bool? ignoreCase)
    {
        var subject = normalize ? Normalize(actual) : actual;
        if (expected.Pattern is not null)
        {
            return expected.Pattern.IsMatch(subject);
        }

        var text = normalize ? Normalize(expected.Text ?? "") : expected.Text ?? "";
        var comparison = ignoreCase == true ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return contains ? subject.Contains(text, comparison) : string.Equals(subject, text, comparison);
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
