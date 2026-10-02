// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

internal static class SnapshotText
{
    public static string Render(Observation observation, IReadOnlyList<Secret> secrets)
    {
        var builder = new StringBuilder();
        builder.Append("Screen (").Append(observation.Route).Append("):");
        var any = false;
        foreach (var root in observation.Roots)
        {
            any |= Write(builder, root, 0, secrets);
        }

        if (!any)
        {
            builder.AppendLine();
            builder.Append("(empty)");
        }

        return builder.ToString();
    }

    public static string Redact(string value, IReadOnlyList<Secret> secrets)
    {
        var text = value;
        foreach (var secret in secrets.OrderByDescending(item => item.Value.Length))
        {
            if (secret.Value.Length == 0)
            {
                continue;
            }

            text = text.Replace(secret.Value, "<secret:" + secret.Name + ">", StringComparison.Ordinal);
        }

        return text;
    }

    private static bool Write(StringBuilder builder, SemanticNode node, int depth, IReadOnlyList<Secret> secrets)
    {
        var wrote = false;
        if (!node.States.Hidden)
        {
            builder.AppendLine();
            builder.Append(' ', depth * 2);
            builder.Append("- ");
            builder.Append(node.Role ?? "text");
            var name = Redact(node.Name ?? node.Text ?? "", secrets);
            if (name.Length > 0)
            {
                builder.Append(" \"").Append(TextRules.Normalize(name)).Append('"');
            }

            builder.Append(" [ref=").Append(node.Ref).Append(']');
            if (!node.States.Secure && !string.IsNullOrEmpty(node.Value))
            {
                builder.Append(" value=\"").Append(Redact(node.Value, secrets)).Append('"');
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

            wrote = true;
        }

        foreach (var child in node.Children)
        {
            wrote |= Write(builder, child, node.States.Hidden ? depth : depth + 1, secrets);
        }

        return wrote;
    }
}
