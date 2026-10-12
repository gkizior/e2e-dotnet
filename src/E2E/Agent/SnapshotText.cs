// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

internal static class SnapshotText
{
    /// <summary>Link target UTF-16 units a line shows; a longer target is cut there and ends with <c>…</c>.</summary>
    private const int MaxHrefLength = 256;

    /// <summary>
    /// The screen as the model reads it. A link target on <paramref name="appOrigin"/>, the app base URL's, shows as its
    /// path: the one a navigation to a path resolves against.
    /// </summary>
    public static string Render(Observation observation, Redactor redactor, string? appOrigin = null)
    {
        var builder = new StringBuilder();
        builder.Append("Screen (").Append(redactor.Redact(observation.Route)).Append("):");
        var any = false;
        foreach (var root in observation.Roots)
        {
            any |= Write(builder, root, 0, redactor, appOrigin);
        }

        if (!any)
        {
            builder.AppendLine();
            builder.Append("(empty)");
        }

        if (observation.Truncated)
        {
            builder.AppendLine();
            builder.Append("(More of the page is off screen. Scroll to reach it.)");
        }

        return builder.ToString();
    }

    private static bool Write(StringBuilder builder, SemanticNode node, int depth, Redactor redactor, string? appOrigin)
    {
        var wrote = false;
        if (!(node.States.Hidden || node.States.AriaHidden))
        {
            builder.AppendLine();
            builder.Append(' ', depth * 2);
            builder.Append("- ");
            builder.Append(node.Role ?? "text");
            // A node with no accessible name, such as one listed for its text
            // or its test id, shows the text it owns, so a container does not repeat its children's.
            // A named node shows its text too, when it differs.
            var shown = node.OwnText ?? node.Text;
            var name = TextRules.Collapse(string.IsNullOrEmpty(node.Name) ? AgentObservation.RedactText(shown ?? "", redactor, ObservationLimits.Text) : AgentObservation.RedactText(node.Name, redactor, ObservationLimits.Name));
            if (name.Length > 0)
            {
                builder.Append(" \"").Append(name).Append('"');
            }

            if (!string.IsNullOrEmpty(node.Name))
            {
                var text = TextRules.Collapse(AgentObservation.RedactText(shown ?? "", redactor, ObservationLimits.Text));
                if (text.Length > 0 && text != name)
                {
                    builder.Append(" text=\"").Append(text).Append('"');
                }
            }

            // The disambiguators the model needs when role and name repeat. The tree has already reduced the href to origin and path.
            if (!string.IsNullOrEmpty(node.TestId))
            {
                builder.Append(" testid=\"").Append(TextRules.Collapse(AgentObservation.RedactText(node.TestId, redactor))).Append('"');
            }

            if (node.Attributes.TryGetValue("href", out var href) && href.Length > 0)
            {
                builder.Append(" href=\"").Append(Href(href, redactor, appOrigin)).Append('"');
            }

            if (!string.IsNullOrEmpty(node.Placeholder) && string.IsNullOrEmpty(node.Name))
            {
                builder.Append(" placeholder=\"").Append(TextRules.Collapse(AgentObservation.RedactText(node.Placeholder, redactor))).Append('"');
            }

            builder.Append(" [ref=").Append(node.Ref).Append(']');
            // A checkbox's or radio's value is an app token its checked state already says more than.
            if (!node.States.Secure && !string.IsNullOrEmpty(node.Value) && !LocatorResolver.IsCheckable(node))
            {
                builder.Append(" value=\"").Append(AgentObservation.RedactText(node.Value, redactor, ObservationLimits.Text)).Append('"');
            }

            if (node.States.Secure)
            {
                builder.Append(" secure");
            }

            if (node.InputPurpose is not (null or "" or "none"))
            {
                builder.Append(" purpose=").Append(node.InputPurpose);
            }

            if (node.States.Checked)
            {
                builder.Append(" [checked]");
            }

            if (node.States.Disabled)
            {
                builder.Append(" [disabled]");
            }

            if (node.States.Expanded)
            {
                builder.Append(" [expanded]");
            }

            if (node.States.Selected)
            {
                builder.Append(" [selected]");
            }

            if (node.States.Pressed)
            {
                builder.Append(" [pressed]");
            }

            if (node.States.Focused)
            {
                builder.Append(" [focused]");
            }

            wrote = true;
        }

        foreach (var child in node.Children)
        {
            wrote |= Write(builder, child, node.States.Hidden || node.States.AriaHidden ? depth : depth + 1, redactor, appOrigin);
        }

        return wrote;
    }

    /// <summary>
    /// A link target as a line shows it: a path when it is on the app's origin, else the whole URL, since a path on another
    /// site would send the model back to the app. Redacted first, then cut at <see cref="MaxHrefLength"/> with a trailing
    /// <c>…</c>, so a cut target never reads as whole and no part of a secret survives the cut.
    /// </summary>
    private static string Href(string href, Redactor redactor, string? appOrigin)
    {
        var redacted = AgentObservation.RedactText(href, redactor);
        var shown = appOrigin is not null && redacted.StartsWith(appOrigin + "/", StringComparison.Ordinal) ? redacted[appOrigin.Length..] : redacted;
        if (shown.Length <= MaxHrefLength)
        {
            return shown;
        }

        // The cut never splits a surrogate pair.
        var end = char.IsHighSurrogate(shown[MaxHrefLength - 1]) ? MaxHrefLength - 1 : MaxHrefLength;
        return shown[..end] + "…";
    }
}
