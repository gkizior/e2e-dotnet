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

    /// <summary>A base64 or base64url run long enough to encode a fragment, padding included.</summary>
    private static readonly Regex EncodedRun = new(@"[A-Za-z0-9+/_-]{8,}={0,2}", RegexOptions.CultureInvariant, MatchTimeout);

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
            var key = new string(ReadingKey(entries[index].Value).Span);
            for (var start = 0; start + FragmentLength <= key.Length; start++)
            {
                _fragments.TryAdd(key.Substring(start, FragmentLength), index);
            }
        }
    }

    /// <summary>A redactor with no values, which leaves every text as it is.</summary>
    public static Redactor None { get; } = new([]);

    public static Redactor For(IEnumerable<Secret> secrets)
    {
        return new Redactor(secrets.Select(secret => (Marker(secret.Name), secret.Value)));
    }

    /// <summary>A redactor over named values of any length, as upstream's <c>SecretLedger</c> takes them.</summary>
    internal static Redactor For(IEnumerable<(string Name, string Value)> values)
    {
        return new Redactor(values.Select(value => (Marker(value.Name), value.Value)));
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
    /// case and with its whitespace runs collapsed, and every base64 run that
    /// decodes to text holding a value or a fragment. For raw engine text, such
    /// as an error that quotes a value cut short.
    /// </summary>
    public string RedactFragments(string text)
    {
        return _pattern is null ? text : MapPieces(RedactPlainFragments(text), RewriteEncoded);
    }

    private string RedactPlainFragments(string text)
    {
        var redacted = Redact(text);
        return _fragments.Count == 0 ? redacted : MapPieces(redacted, RewriteFragments);
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

        if (ch is "Σ" or "σ" or "ς")
        {
            forms.AddRange(["Σ", "σ", "ς"]);
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
            // The IgnoreCase flag does not match a final sigma through its capital, so the two stay apart.
            if (seen.Add(string.Concat(spelling.Select(unit => unit == 'ς' ? unit : char.ToUpperInvariant(unit)))))
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

    /// <summary>
    /// A text as a cut or a fragment of a value is compared: each UTF-16 unit
    /// in one case (<c>ſ</c>, <c>S</c>, and <c>s</c> agree, and so do <c>İ</c>
    /// and <c>i</c>), and each whitespace run read as one space, as an engine
    /// that collapses whitespace shows it. A stretch of the key maps back to
    /// the text through <see cref="Reading.At"/>.
    /// </summary>
    private static Reading ReadingKey(string text)
    {
        var units = new char[text.Length];
        List<int>? breaks = null;
        var length = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var unit = text[index];
            if (!IsWhitespace(unit))
            {
                units[length++] = FoldCase(unit);
                continue;
            }

            var end = index + 1;
            while (end < text.Length && IsWhitespace(text[end]))
            {
                end++;
            }

            units[length++] = ' ';
            if (end - index > 1 || unit != ' ')
            {
                // Where the stretch after a collapsed run starts, in the key and in the text.
                (breaks ??= []).Add(length);
                breaks.Add(end);
            }

            index = end - 1;
        }

        return new Reading(units, length, breaks?.ToArray() ?? []);
    }

    private static char FoldCase(char unit)
    {
        var upper = char.ToUpperInvariant(unit);
        return upper == '\u0130' ? 'i' : char.ToLowerInvariant(upper);
    }

    /// <summary>Whether a UTF-16 unit is whitespace a reader collapses.</summary>
    private static bool IsWhitespace(char unit)
    {
        return unit is ' ' or (>= '\t' and <= '\r') or '\u00a0' or '\u1680' or (>= '\u2000' and <= '\u200a')
            or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000' or '\ufeff';
    }

    private string MapPieces(string text, Func<string, string> rewrite)
    {
        var pieces = _known!.Split(text);
        if (pieces.Length == 1)
        {
            return rewrite(text);
        }

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
        var reading = ReadingKey(text);
        var key = reading.Span;
        var owners = _fragments.GetAlternateLookup<ReadOnlySpan<char>>();
        StringBuilder? builder = null;
        var kept = 0;
        var start = 0;
        while (start + FragmentLength <= key.Length)
        {
            if (!owners.TryGetValue(key.Slice(start, FragmentLength), out var owner))
            {
                start++;
                continue;
            }

            var end = start + FragmentLength;
            while (end < key.Length && owners.ContainsKey(key.Slice(end + 1 - FragmentLength, FragmentLength)))
            {
                end++;
            }

            builder ??= new StringBuilder(text.Length);
            builder.Append(text, kept, reading.At(start) - kept).Append(_markers[owner]);
            kept = reading.At(end);
            start = end;
        }

        return builder?.Append(text, kept, text.Length - kept).ToString() ?? text;
    }

    /// <summary>
    /// Rewrites every base64 run whose decoded text holds a value, whole or a
    /// fragment, as that value's marker. An encoder spreads a value's bytes over
    /// the characters around it, so no plain-text spelling matches the run.
    /// </summary>
    private string RewriteEncoded(string text)
    {
        return EncodedRun.Replace(text, match => EncodedMarker(match.Value) ?? match.Value);
    }

    /// <summary>
    /// The marker of the first value a run decodes to text holding. The run can
    /// start with text the encoding does not (a URL path, a cookie prefix), so
    /// it is decoded from each of the four offsets a base64 group can start at.
    /// A marker the decoded text already holds is page text, not a value, so
    /// only the text between such markers is read.
    /// </summary>
    private string? EncodedMarker(string run)
    {
        var base64 = run.Replace('-', '+').Replace('_', '/').TrimEnd('=');
        for (var offset = 0; offset < 4 && offset < base64.Length; offset++)
        {
            var pieces = _known!.Split(DecodeBase64(base64[offset..]));
            for (var index = 0; index < pieces.Length; index += 2)
            {
                var marker = _known.Match(RedactPlainFragments(pieces[index]));
                if (marker.Success)
                {
                    return marker.Value;
                }
            }
        }

        return null;
    }

    /// <summary>Decodes unpadded base64 as UTF-8, dropping a trailing character that cannot complete a byte.</summary>
    private static string DecodeBase64(string base64)
    {
        var usable = base64.Length - (base64.Length % 4 == 1 ? 1 : 0);
        var padded = base64[..usable].PadRight((usable + 3) / 4 * 4, '=');
        var bytes = new byte[padded.Length / 4 * 3];
        return Convert.TryFromBase64String(padded, bytes, out var written)
            ? Encoding.UTF8.GetString(bytes, 0, written)
            : "";
    }

    /// <summary>A text as <see cref="ReadingKey"/> reads it, and where each of its units starts in the text.</summary>
    private readonly record struct Reading(char[] Key, int Length, int[] Breaks)
    {
        public ReadOnlySpan<char> Span => Key.AsSpan(0, Length);

        /// <summary>The index in the text of the unit of the key at <paramref name="index"/>; the key's length maps to the text's.</summary>
        public int At(int index)
        {
            // The last stretch starting at or before the index; before the first run the key and the text agree.
            int low = -1, high = Breaks.Length / 2 - 1;
            while (low < high)
            {
                var middle = (low + high + 1) >> 1;
                if (Breaks[2 * middle] <= index)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            return low == -1 ? index : Breaks[2 * low + 1] + index - Breaks[2 * low];
        }
    }
}
