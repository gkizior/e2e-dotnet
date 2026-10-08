// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace E2E.Internal;

/// <summary>
/// Replaces secret values with their markers in every spelling the value can
/// take in text: any case, JSON escapes, HTML character references, percent
/// encoding, and collapsed inner whitespace. Occurrences are rewritten
/// leftmost first, and the longer value wins where two start together. A
/// known marker is never rewritten, so redacting twice is redacting once.
/// </summary>
internal sealed class Redactor
{
    /// <summary>The fewest consecutive characters of a value that count as a fragment of it.</summary>
    internal const int FragmentLength = 8;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    private static readonly CultureInfo[] CaseCultures =
    [
        CultureInfo.GetCultureInfo("tr"),
        CultureInfo.GetCultureInfo("az"),
        CultureInfo.GetCultureInfo("lt"),
    ];

    private readonly Regex? _pattern;
    private readonly Regex? _known;
    private readonly string[] _markers = [];
    private readonly Dictionary<string, int> _fragments = new(StringComparer.Ordinal);

    private Redactor(IEnumerable<(string Marker, string Value)> values)
    {
        var entries = values
            .Where(entry => entry.Value.Length > 0)
            .Distinct()
            .OrderByDescending(entry => entry.Value.Length)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        _markers = entries.Select(entry => entry.Marker).ToArray();
        var markerAlternatives = string.Join('|', _markers.Distinct(StringComparer.Ordinal).Select(Regex.Escape));
        _known = new Regex("(" + markerAlternatives + ")", RegexOptions.CultureInvariant, MatchTimeout);
        var source = string.Join('|', entries.Select(entry => "(" + ValuePattern(entry.Value) + ")"));
        _pattern = new Regex(source, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
        for (var index = 0; index < entries.Count; index++)
        {
            var key = CaseKey(entries[index].Value);
            for (var start = 0; start + FragmentLength <= key.Length; start++)
            {
                _fragments.TryAdd(key.Substring(start, FragmentLength), index);
            }
        }
    }

    public static Redactor For(IEnumerable<Secret> secrets)
    {
        return new Redactor(secrets.Select(secret => (Marker(secret.Name), secret.Value)));
    }

    /// <summary>A redactor for one value whose name is not known, as an engine sees a sensitive fill.</summary>
    public static Redactor ForValue(string value)
    {
        return new Redactor([("[redacted]", value)]);
    }

    public static string Marker(string name) => "<secret:" + name + ">";

    /// <summary>Rewrites every whole occurrence of a value, in any spelling, with its marker.</summary>
    public string Redact(string text)
    {
        if (_pattern is null || _known is null || text.Length == 0)
        {
            return text;
        }

        return MapPieces(text, Rewrite);
    }

    /// <summary>
    /// <see cref="Redact"/> that also rewrites every run of at least
    /// <see cref="FragmentLength"/> consecutive characters of a value, in any
    /// case. For raw engine text, such as an error that quotes a value cut short.
    /// </summary>
    public string RedactFragments(string text)
    {
        var redacted = Redact(text);
        if (_fragments.Count == 0)
        {
            return redacted;
        }

        return MapPieces(redacted, RewriteFragments);
    }

    private static string ValuePattern(string value)
    {
        var trimmable = new StringInfo(value.Trim()).LengthInTextElements >= Secret.MinLength;
        var pieces = Regex.Split(value, @"(\s+)", RegexOptions.None, MatchTimeout);
        var builder = new StringBuilder();
        for (var index = 0; index < pieces.Length; index++)
        {
            var piece = pieces[index];
            var run = index % 2 == 1;
            if (run && pieces[index - 1].Length > 0 && pieces[index + 1].Length > 0)
            {
                builder.Append(WhitespaceRun(piece));
                continue;
            }

            var written = string.Concat(piece.EnumerateRunes().Select(rune => Character(rune.ToString())));
            builder.Append(run && trimmable ? "(?:" + written + ")?" : written);
        }

        return builder.ToString();
    }

    private static string WhitespaceRun(string run)
    {
        var spelled = new[] { " " }.Concat(run.Select(ch => ch.ToString()))
            .Distinct(StringComparer.Ordinal)
            .SelectMany(Spellings)
            .Where(spelling => !(spelling.Length == 1 && char.IsWhiteSpace(spelling[0])));
        var one = Alternation(new[] { @"\s" }.Concat(Distinct(spelled).Select(Regex.Escape)));
        return one + "{1," + run.Length.ToString(CultureInfo.InvariantCulture) + "}";
    }

    private static string Character(string ch)
    {
        var forms = CaseForms(ch);
        var single = forms.Where(form => form.EnumerateRunes().Count() == 1).SelectMany(Spellings);
        var expanded = forms
            .Where(form => form.EnumerateRunes().Count() > 1)
            .Select(form => string.Concat(form.EnumerateRunes().Select(part => Alternation(Distinct(Spellings(part.ToString())).Select(Regex.Escape)))));
        return Alternation(Distinct(single).Select(Regex.Escape).Concat(expanded));
    }

    private static List<string> CaseForms(string ch)
    {
        var forms = new List<string> { ch, ch.ToUpperInvariant(), ch.ToLowerInvariant() };
        if (ch is "ß" or "ẞ")
        {
            forms.Add("SS");
        }

        foreach (var culture in CaseCultures)
        {
            forms.Add(ch.ToUpper(culture));
            forms.Add(ch.ToLower(culture));
        }

        return forms.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string Alternation(IEnumerable<string> options)
    {
        var sorted = options.OrderByDescending(option => option.Length).ToList();
        return sorted.Count == 1 ? sorted[0] : "(?:" + string.Join('|', sorted) + ")";
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> spellings)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spelling in spellings)
        {
            if (seen.Add(spelling.ToUpperInvariant()))
            {
                yield return spelling;
            }
        }
    }

    /// <summary>
    /// The spellings one character has in captured text: as is, as JSON
    /// escapes it (once and inside another JSON string), as an HTML character
    /// reference, and percent-encoded. ASCII letters and digits have one.
    /// </summary>
    private static List<string> Spellings(string ch)
    {
        if (ch.Length == 1 && char.IsAsciiLetterOrDigit(ch[0]))
        {
            return [ch];
        }

        var inJson = new List<string> { JsonEscape(ch), UnicodeEscape(ch) };
        if (ch == "/")
        {
            inJson.Add("\\/");
        }

        var codePoint = char.ConvertToUtf32(ch, 0);
        var decimalText = codePoint.ToString(CultureInfo.InvariantCulture);
        var spellings = new List<string> { ch };
        spellings.AddRange(inJson);
        spellings.AddRange(inJson.Select(JsonEscape));
        switch (ch)
        {
            case "&":
                spellings.Add("&amp;");
                break;
            case "<":
                spellings.Add("&lt;");
                break;
            case ">":
                spellings.Add("&gt;");
                break;
            case "\"":
                spellings.Add("&quot;");
                spellings.Add("\"\"");
                break;
            case "'":
                spellings.Add("&apos;");
                break;
            case " ":
                spellings.Add("+");
                break;
        }

        spellings.Add("&#" + decimalText + ";");
        spellings.Add("&#" + decimalText.PadLeft(3, '0') + ";");
        spellings.Add("&#x" + codePoint.ToString("x", CultureInfo.InvariantCulture) + ";");
        spellings.Add(string.Concat(Encoding.UTF8.GetBytes(ch).Select(b => "%" + b.ToString("x2", CultureInfo.InvariantCulture))));
        return spellings.Distinct(StringComparer.Ordinal).ToList();
    }

    private static string JsonEscape(string text)
    {
        var builder = new StringBuilder();
        foreach (var ch in text)
        {
            builder.Append(ch switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                < ' ' => "\\u" + ((int)ch).ToString("x4", CultureInfo.InvariantCulture),
                _ => ch.ToString(),
            });
        }

        return builder.ToString();
    }

    private static string UnicodeEscape(string ch)
    {
        return string.Concat(ch.Select(unit => "\\u" + ((int)unit).ToString("x4", CultureInfo.InvariantCulture)));
    }

    /// <summary>Each UTF-16 unit in one case, so a cut or fragment matches in any case. Keeps the length.</summary>
    private static string CaseKey(string text)
    {
        return string.Create(text.Length, text, (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                span[index] = char.ToLowerInvariant(char.ToUpperInvariant(source[index]));
            }
        });
    }

    private string MapPieces(string text, Func<string, string> rewrite)
    {
        var pieces = _known!.Split(text);
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < pieces.Length; index++)
        {
            builder.Append(index % 2 == 1 ? pieces[index] : rewrite(pieces[index]));
        }

        return builder.ToString();
    }

    private string Rewrite(string text)
    {
        return _pattern!.Replace(text, match =>
        {
            for (var group = 1; group < match.Groups.Count && group <= _markers.Length; group++)
            {
                if (match.Groups[group].Success)
                {
                    return _markers[group - 1];
                }
            }

            return "";
        });
    }

    private string RewriteFragments(string text)
    {
        var key = CaseKey(text);
        var builder = new StringBuilder(text.Length);
        var kept = 0;
        var start = 0;
        while (start + FragmentLength <= text.Length)
        {
            if (!_fragments.TryGetValue(key.Substring(start, FragmentLength), out var owner))
            {
                start++;
                continue;
            }

            var end = start + FragmentLength;
            while (end < text.Length && _fragments.ContainsKey(key.Substring(end + 1 - FragmentLength, FragmentLength)))
            {
                end++;
            }

            builder.Append(text, kept, start - kept).Append(_markers[owner]);
            kept = end;
            start = end;
        }

        return builder.Append(text, kept, text.Length - kept).ToString();
    }
}
