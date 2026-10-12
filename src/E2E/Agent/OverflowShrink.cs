// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.RegularExpressions;

namespace E2E;

/// <summary>
/// Shrinks a history the provider refused as too large: every superseded screen is elided, then any text still
/// longer than the clip is cut to its head with a notice, so the model knows to observe again for what it no longer
/// sees. Upstream's <c>shrinkForOverflow</c>.
/// </summary>
internal static partial class OverflowShrink
{
    /// <summary>Longest text a message keeps after an overflow; the head of a screen still names its controls.</summary>
    internal const int TextClipChars = 16_384;

    /// <summary>The newest screens that stay whole. One: a screen replaced by a newer one is dead weight on every later turn.</summary>
    private const int ScreenPreserveCount = 1;

    /// <summary>Every screen the port prints begins a line with this phrase, as upstream's full screens do.</summary>
    [GeneratedRegex(@"^Screen \(", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FullScreenPattern();

    public static List<ModelMessage> ShrinkForOverflow(IReadOnlyList<ModelMessage> messages)
    {
        var stale = messages.Count(message => message.Role is "user" or "tool" && message.Content is { } text && FullScreenPattern().IsMatch(text)) - ScreenPreserveCount;
        var shrunk = new List<ModelMessage>(messages.Count);
        foreach (var message in messages)
        {
            var text = message.Content;
            if (text is not null && message.Role is "user" or "tool")
            {
                if (stale > 0 && FullScreenPattern().IsMatch(text))
                {
                    stale--;
                    text = ElideScreen(text);
                }

                text = Clip(text);
            }

            shrunk.Add(text == message.Content ? message : With(message, text));
        }

        return shrunk;
    }

    /// <summary>Characters of text a history carries, for the account of a shrink.</summary>
    public static int TextChars(IEnumerable<ModelMessage> messages) => messages.Sum(message => message.Content?.Length ?? 0);

    /// <summary>Everything before the screen, then the notice in place of the tree.</summary>
    private static string ElideScreen(string text)
    {
        var at = FullScreenPattern().Match(text).Index;
        var head = at <= 0 ? text.Split('\n', 2)[0] : text[..at].TrimEnd();
        return head + "\n[earlier screen elided; the newest \"Screen\" describes the screen]";
    }

    private static string Clip(string text)
    {
        if (text.Length <= TextClipChars)
        {
            return text;
        }

        // Never end the head on half of a surrogate pair.
        var keep = char.IsHighSurrogate(text[TextClipChars - 1]) ? TextClipChars - 1 : TextClipChars;
        return text[..keep] + "\n[" + (text.Length - keep).ToString(CultureInfo.InvariantCulture)
            + " more characters cut: the request exceeded the model's context window; observe again for what is missing]";
    }

    private static ModelMessage With(ModelMessage message, string? content) => new()
    {
        Role = message.Role,
        Content = content,
        ToolCalls = message.ToolCalls,
        ToolCallId = message.ToolCallId,
        Name = message.Name,
        ReasoningItems = message.ReasoningItems,
    };
}
