// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

internal static class SnapshotText
{
    public static string Render(Observation observation, Redactor redactor)
    {
        var builder = new StringBuilder();
        builder.Append("Screen (").Append(observation.Route).Append("):");
        var any = false;
        foreach (var root in observation.Roots)
        {
            any |= Write(builder, root, 0, redactor);
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

    private static bool Write(StringBuilder builder, SemanticNode node, int depth, Redactor redactor)
    {
        var wrote = false;
        if (!node.States.Hidden)
        {
            builder.AppendLine();
            builder.Append(' ', depth * 2);
            builder.Append("- ");
            builder.Append(node.Role ?? "text");
            // A node with no accessible name, such as one listed for its text
            // or its test id, shows its text.
            var name = redactor.Redact((string.IsNullOrEmpty(node.Name) ? node.Text : node.Name) ?? "");
            if (name.Length > 0)
            {
                builder.Append(" \"").Append(TextRules.Normalize(name)).Append('"');
            }

            builder.Append(" [ref=").Append(node.Ref).Append(']');
            // A checkbox's or radio's value is an app token its checked state already says more than.
            if (!node.States.Secure && !string.IsNullOrEmpty(node.Value) && !LocatorResolver.IsCheckable(node))
            {
                builder.Append(" value=\"").Append(redactor.Redact(node.Value)).Append('"');
            }

            if (node.States.Secure)
            {
                builder.Append(" secure");
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
            wrote |= Write(builder, child, node.States.Hidden ? depth : depth + 1, redactor);
        }

        return wrote;
    }
}
