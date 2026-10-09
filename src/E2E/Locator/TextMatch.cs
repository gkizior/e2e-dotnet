// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using E2E.Internal;

namespace E2E;

/// <summary>
/// A string or a regular expression, the upstream <c>TextMatch</c>, used by
/// both screen queries and expectations. In a query, a string matches exactly
/// by default, and <c>exact: false</c> makes it a case-insensitive substring;
/// a <see cref="Regex"/> is tested against the whitespace-normalized text and
/// ignores <c>exact</c>. Converts implicitly from both.
/// </summary>
public sealed class TextMatch
{
    private TextMatch(string? text, Regex? pattern)
    {
        Text = text;
        Pattern = pattern;
    }

    /// <summary>The string to match, or null for a regular expression.</summary>
    public string? Text { get; }

    /// <summary>The regular expression to test, or null for a string.</summary>
    public Regex? Pattern { get; }

    [return: NotNullIfNotNull(nameof(text))]
    public static implicit operator TextMatch?(string? text) => text is null ? null : FromString(text);

    [return: NotNullIfNotNull(nameof(pattern))]
    public static implicit operator TextMatch?(Regex? pattern) => pattern is null ? null : FromRegex(pattern);

    public static TextMatch FromString(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new TextMatch(text, null);
    }

    public static TextMatch FromRegex(Regex pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return new TextMatch(null, pattern);
    }

    public override string ToString()
    {
        if (Pattern is null)
        {
            return "\"" + Text + "\"";
        }

        var flags = (Pattern.Options & RegexOptions.IgnoreCase) != 0 ? "i" : "";
        if ((Pattern.Options & RegexOptions.Multiline) != 0)
        {
            flags += "m";
        }

        if ((Pattern.Options & RegexOptions.Singleline) != 0)
        {
            flags += "s";
        }

        return "/" + Pattern + "/" + flags;
    }

    /// <summary>
    /// The match under <c>ignoreCase</c>: <c>true</c> adds <see cref="RegexOptions.IgnoreCase"/> to a pattern,
    /// <c>false</c> removes it, and null keeps it. A string is returned as it is; the comparison folds its case.
    /// </summary>
    internal TextMatch WithIgnoreCase(bool? ignoreCase)
    {
        if (Pattern is null || ignoreCase is not bool fold)
        {
            return this;
        }

        var options = fold ? Pattern.Options | RegexOptions.IgnoreCase : Pattern.Options & ~RegexOptions.IgnoreCase;
        return options == Pattern.Options ? this : new TextMatch(null, new Regex(Pattern.ToString(), options, Pattern.MatchTimeout));
    }

    /// <summary>The match as an expectation failure prints it.</summary>
    internal string Describe(bool? ignoreCase)
    {
        return Pattern is null && ignoreCase == true ? ToString() + " (ignoring case)" : ToString();
    }

    internal bool Matches(string? actual, bool exact)
    {
        if (Pattern is null)
        {
            return TextRules.Matches(actual, Text!, exact);
        }

        return actual is not null && Pattern.IsMatch(TextRules.Normalize(actual));
    }

    internal void ThrowIfBlank(string paramName)
    {
        if (Pattern is null && string.IsNullOrWhiteSpace(Text))
        {
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", paramName);
        }
    }
}

/// <summary>Options shared by the text-matching queries.</summary>
public class TextMatchOptions
{
    /// <summary>Exact match, the default. <c>false</c> is a case-insensitive substring. A <see cref="Regex"/> ignores it.</summary>
    public bool Exact { get; init; } = true;

    /// <summary>
    /// Drops nodes the platform reports as hidden before the exactly-one rule,
    /// so a visible node with a hidden twin still resolves. On the web, hidden is
    /// what renders: <c>display: none</c>, a <c>visibility</c> other than
    /// <c>visible</c>, and content under <c>content-visibility: hidden</c> or a
    /// closed <c>details</c>; a child that sets <c>visibility: visible</c> under a
    /// hidden parent is visible, and an <c>aria-hidden</c> node that paints is visible.
    /// Role queries never match hidden or <c>aria-hidden</c> nodes, whatever this says.
    /// </summary>
    public bool Visible { get; init; }
}

/// <summary>Role query options. A null state is not checked.</summary>
public sealed class RoleOptions : TextMatchOptions
{
    /// <summary>Accessible name filter.</summary>
    public TextMatch? Name { get; init; }

    /// <summary>Requires the checked state.</summary>
    public bool? Checked { get; init; }

    /// <summary>Requires the disabled state.</summary>
    public bool? Disabled { get; init; }

    /// <summary>Requires the selected state.</summary>
    public bool? Selected { get; init; }

    /// <summary>Requires the expanded state.</summary>
    public bool? Expanded { get; init; }

    /// <summary>Requires the pressed state of a toggle button.</summary>
    public bool? Pressed { get; init; }

    /// <summary>Requires a heading level, 1 through 6.</summary>
    public int? Level { get; init; }
}
