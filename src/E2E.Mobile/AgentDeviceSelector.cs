// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>Picks the nodes one compiled selector matches, in document order.</summary>
public delegate IReadOnlyList<ProjectedNode> CompiledSelector(IReadOnlyList<ProjectedNode> entries);

/// <summary>
/// agent-device selector support for the <c>selector</c> locator expression:
/// <c>screen.Locator("id=save role=button")</c> and the structural hint on every observed node. The
/// parsing and term rules are agent-device's own (<c>parseSelectorChain</c>,
/// <c>listSelectorChainMatches</c>); matching runs against the projected snapshot, one immediate pass,
/// every match returned, and strictness stays with the runner.
/// </summary>
public static partial class AgentDeviceSelector
{
    private static readonly HashSet<string> TextKeys = ["id", "role", "text", "label", "value", "appname", "windowtitle"];

    private static readonly HashSet<string> FlagKeys = ["visible", "hidden", "editable", "selected", "focused", "enabled", "hittable"];

    /// <summary>
    /// Compiles one selector string for a platform. Alternatives (agent-device's fallback chain) are tried
    /// in order and the first that matches anything wins, with every node it matches returned in snapshot
    /// order.
    /// </summary>
    public static CompiledSelector Compile(string raw, MobilePlatform platform)
    {
        List<List<Term>> chain;
        try
        {
            chain = ParseChain(raw);
        }
        catch (AgentDeviceError cause)
        {
            throw new EngineException(
                EngineErrorCodes.EngineFailure,
                $"invalid agent-device selector {JsonText.Quote(raw)}: {DeviceErrors.Message(cause)}",
                retryable: false,
                cause);
        }

        return entries =>
        {
            foreach (var alternative in chain)
            {
                var matches = entries.Where(entry => alternative.All(term => MatchesTerm(entry.Raw, term, platform))).ToList();
                if (matches.Count > 0)
                {
                    return matches;
                }
            }

            return [];
        };
    }

    private static List<List<Term>> ParseChain(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            throw Invalid("Selector expression cannot be empty");
        }

        return SplitFallbacks(text).Select(ParseSegment).ToList();
    }

    /// <summary>Splits on <c>||</c> outside quotes.</summary>
    private static List<string> SplitFallbacks(string text)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];
            if (character is '"' or '\'' && !IsEscaped(text, position))
            {
                quote = ToggleQuote(quote, character);
                current.Append(character);
                continue;
            }

            if (quote is null && character == '|' && position + 1 < text.Length && text[position + 1] == '|')
            {
                var part = current.ToString().Trim();
                if (part.Length == 0)
                {
                    throw Invalid($"Invalid selector fallback expression: {text}");
                }

                parts.Add(part);
                current.Clear();
                position++;
                continue;
            }

            current.Append(character);
        }

        var last = current.ToString().Trim();
        if (last.Length == 0)
        {
            throw Invalid($"Invalid selector fallback expression: {text}");
        }

        parts.Add(last);
        return parts;
    }

    private static List<Term> ParseSegment(string segment)
    {
        var text = segment.Trim();
        if (text.Length == 0)
        {
            throw Invalid("Selector segment cannot be empty");
        }

        var tokens = SplitTerms(text);
        if (tokens.Count == 0)
        {
            throw Invalid($"Invalid selector segment: {segment}");
        }

        return tokens.Select(ParseTerm).ToList();
    }

    /// <summary>Splits on whitespace outside quotes.</summary>
    private static List<string> SplitTerms(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        char? quote = null;
        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];
            if (character is '"' or '\'' && !IsEscaped(text, position))
            {
                quote = ToggleQuote(quote, character);
                current.Append(character);
                continue;
            }

            if (quote is null && char.IsWhiteSpace(character))
            {
                if (current.ToString().Trim().Length > 0)
                {
                    tokens.Add(current.ToString().Trim());
                }

                current.Clear();
                continue;
            }

            current.Append(character);
        }

        if (quote is not null)
        {
            throw Invalid($"Unclosed quote in selector: {text}");
        }

        if (current.ToString().Trim().Length > 0)
        {
            tokens.Add(current.ToString().Trim());
        }

        return tokens;
    }

    private static Term ParseTerm(string raw)
    {
        var text = raw.Trim();
        if (text.Length == 0)
        {
            throw Invalid("Empty selector term");
        }

        var equals = text.IndexOf('=', StringComparison.Ordinal);
        if (equals == -1)
        {
            var bare = text.ToLowerInvariant();
            return FlagKeys.Contains(bare) ? new Term(bare, "true", true) : throw Invalid($"Invalid selector term \"{raw}\", expected key=value");
        }

        var key = text[..equals].Trim().ToLowerInvariant();
        var value = text[(equals + 1)..].Trim();
        if (!TextKeys.Contains(key) && !FlagKeys.Contains(key))
        {
            throw Invalid($"Unknown selector key: {key}");
        }

        if (value.Length == 0)
        {
            throw Invalid($"Missing selector value for key: {key}");
        }

        if (FlagKeys.Contains(key))
        {
            return Unquote(value).ToLowerInvariant() switch
            {
                "true" => new Term(key, "true", true),
                "false" => new Term(key, "false", false),
                _ => throw Invalid($"Invalid boolean value for {key}: {value}"),
            };
        }

        return new Term(key, Unquote(value), null);
    }

    /// <summary>A term value without its surrounding quotes, with the escapes inside them read.</summary>
    private static string Unquote(string raw)
    {
        var text = raw.Trim();
        var quoted = text.Length > 0 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\''));
        if (!quoted)
        {
            return text;
        }

        return text.Length < 2 ? "" : Unescape(text[1..^1]);
    }

    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        var position = 0;
        while (position < text.Length)
        {
            var character = text[position];
            if (character != '\\' || position + 1 >= text.Length)
            {
                result.Append(character);
                position++;
                continue;
            }

            var next = text[position + 1];
            var simple = next switch
            {
                '"' => '"',
                '\'' => '\'',
                '\\' => '\\',
                '/' => '/',
                'b' => '\b',
                'f' => '\f',
                'n' => '\n',
                'r' => '\r',
                't' => '\t',
                _ => default(char?),
            };
            if (simple is { } plain)
            {
                result.Append(plain);
                position += 2;
                continue;
            }

            if (next == 'u' && position + 6 <= text.Length && HexQuad().IsMatch(text.AsSpan(position + 2, 4)))
            {
                result.Append((char)int.Parse(text.AsSpan(position + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                position += 6;
                continue;
            }

            result.Append(character);
            position++;
        }

        return result.ToString();
    }

    private static char? ToggleQuote(char? current, char character) =>
        current is null ? character : current == character ? null : current;

    /// <summary>True when an odd number of backslashes precede the character.</summary>
    private static bool IsEscaped(string text, int position)
    {
        var slashes = 0;
        for (var before = position - 1; before >= 0 && text[before] == '\\'; before--)
        {
            slashes++;
        }

        return slashes % 2 == 1;
    }

    private static AgentDeviceError Invalid(string message) => new("INVALID_ARGS", message);

    private static readonly string[] PackagePrefixes =
        ["android.widget.", "android.view.", "android.webkit.", "androidx.", "com.google.android.", "com.android."];

    /// <summary>agent-device's presented role for each platform type, the <c>kind</c> it publishes on a node.</summary>
    private static readonly Dictionary<string, string> PresentedKinds = new(StringComparer.Ordinal)
    {
        ["application"] = "application",
        ["navigationbar"] = "navigation-bar",
        ["tabbar"] = "tab-bar",
        ["button"] = "button",
        ["imagebutton"] = "button",
        ["link"] = "link",
        ["cell"] = "cell",
        ["statictext"] = "text",
        ["checkedtextview"] = "text",
        ["textbox"] = "text-field",
        ["textfield"] = "text-field",
        ["edittext"] = "text-field",
        ["textarea"] = "text-view",
        ["switch"] = "switch",
        ["slider"] = "slider",
        ["image"] = "image",
        ["imageview"] = "image",
        ["webview"] = "webview",
        ["framelayout"] = "group",
        ["linearlayout"] = "group",
        ["relativelayout"] = "group",
        ["constraintlayout"] = "group",
        ["viewgroup"] = "group",
        ["view"] = "group",
        ["listview"] = "list",
        ["recyclerview"] = "list",
        ["collectionview"] = "collection",
        ["searchfield"] = "search",
        ["heading"] = "heading",
        ["activityindicator"] = "activity-indicator",
        ["progressindicator"] = "progress-indicator",
        ["segmentedcontrol"] = "segmented-control",
        ["group"] = "group",
        ["window"] = "window",
        ["checkbox"] = "checkbox",
        ["radio"] = "radio",
        ["menuitem"] = "menu-item",
        ["toolbar"] = "toolbar",
        ["scrollarea"] = "scroll-area",
        ["scrollview"] = "scroll-area",
        ["nestedscrollview"] = "scroll-area",
        ["table"] = "table",
    };

    /// <summary>agent-device's text comparison: trimmed, lower-cased, whitespace collapsed.</summary>
    private static string Normalize(string text) => Whitespace().Replace(text.Trim().ToLowerInvariant(), " ");

    private static bool TextEquals(string? actual, string expected) => Normalize(actual ?? "") == Normalize(expected);

    private static bool MatchesTerm(RawNode node, Term term, MobilePlatform platform)
    {
        var flag = term.Flag == true;
        switch (term.Key)
        {
            case "id":
                return TextEquals(node.Identifier, term.Value);
            case "role":
                return Kinds(node).Any(kind => Normalize(kind) == Normalize(term.Value));
            case "label":
                return TextEquals(node.Label, term.Value);
            case "value":
                return TextEquals(node.Value, term.Value);
            case "text":
                return TextEquals(FirstText(node), term.Value);
            case "appname":
                return TextEquals(node.AppName, term.Value);
            case "windowtitle":
                return TextEquals(node.WindowTitle, term.Value);
            case "visible":
                return IsVisible(node) == flag;
            case "hidden":
                return !IsVisible(node) == flag;
            case "editable":
                return (IsFillableType(node.Type ?? "", platform) && node.Enabled != false) == flag;
            case "selected":
                return (node.Selected == true) == flag;
            case "focused":
                return (node.Focused == true) == flag;
            case "enabled":
                return (node.Enabled != false) == flag;
            case "hittable":
                return (node.Hittable == true) == flag;
            default:
                return false;
        }
    }

    /// <summary>The first non-empty of label, value, and identifier, trimmed.</summary>
    private static string FirstText(RawNode node) =>
        new[] { node.Label, node.Value, node.Identifier }.Select(text => text?.Trim() ?? "").FirstOrDefault(text => text.Length > 0) ?? "";

    private static bool IsVisible(RawNode node) =>
        node.Hittable == true || (node.Rect is { } rect && rect.Width > 0 && rect.Height > 0);

    /// <summary>The kinds a <c>role</c> term matches: the node's kind, and its platform type when that differs.</summary>
    private static List<string> Kinds(RawNode node)
    {
        var kind = node.Kind ?? TypeKind(node.Type ?? "Element");
        var type = NormalizeType(node.Type ?? "");
        return type.Length > 0 && type != kind ? [kind, type] : [kind];
    }

    /// <summary>agent-device's lower-cased platform type: no <c>XCUIElementType</c> or <c>AX</c> prefix, no package.</summary>
    private static string NormalizeType(string type)
    {
        var text = XcuiPrefix().Replace(type.Trim(), "");
        text = AxPrefix().Replace(text, "").ToLowerInvariant();
        var cut = Math.Max(text.LastIndexOf('.'), text.LastIndexOf('/'));
        return cut == -1 ? text : text[(cut + 1)..];
    }

    /// <summary>Whether a platform type takes typed text.</summary>
    private static bool IsFillableType(string type, MobilePlatform platform)
    {
        var text = NormalizeType(type);
        if (text.Length == 0)
        {
            return true;
        }

        return platform == MobilePlatform.Android
            ? text.Contains("edittext", StringComparison.Ordinal) || text.Contains("autocompletetextview", StringComparison.Ordinal)
            : text.Contains("textfield", StringComparison.Ordinal)
                || text.Contains("searchfield", StringComparison.Ordinal)
                || text.Contains("textview", StringComparison.Ordinal)
                || text.Contains("textarea", StringComparison.Ordinal)
                || text == "search";
    }

    /// <summary>The role agent-device presents for a platform type, when the node carries no <c>kind</c> of its own.</summary>
    private static string TypeKind(string type)
    {
        var android = type.Contains('.', StringComparison.Ordinal)
            && (type.StartsWith("android.", StringComparison.Ordinal) || type.StartsWith("androidx.", StringComparison.Ordinal) || type.StartsWith("com.", StringComparison.Ordinal));
        var text = XcuiPrefix().Replace(type, "").ToLowerInvariant();
        if (text.Contains('.', StringComparison.Ordinal))
        {
            foreach (var prefix in PackagePrefixes)
            {
                if (text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    text = text[prefix.Length..];
                    break;
                }
            }

            if (android && text.Contains('.', StringComparison.Ordinal))
            {
                text = text[(text.LastIndexOf('.') + 1)..];
            }
        }

        if (text == "textview")
        {
            return android ? "text" : "text-view";
        }

        return PresentedKinds.TryGetValue(text, out var kind) ? kind : text.Length > 0 ? text : "element";
    }

    [GeneratedRegex("XCUIElementType", RegexOptions.IgnoreCase)]
    private static partial Regex XcuiPrefix();

    [GeneratedRegex("^AX")]
    private static partial Regex AxPrefix();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("^[0-9a-fA-F]{4}$")]
    private static partial Regex HexQuad();

    /// <summary>One selector term. <see cref="Flag"/> is set for the keys that take a boolean.</summary>
    private sealed record Term(string Key, string Value, bool? Flag);
}
