// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;

namespace E2E.Mobile;

/// <summary>JSON text rules the device engine shares with the agent-device selector grammar.</summary>
internal static class JsonText
{
    /// <summary>
    /// A string as a JSON string literal, the way <c>JSON.stringify</c> writes it: only quotes, backslashes,
    /// control characters, and unpaired surrogates are escaped; every other character stays as it is.
    /// </summary>
    public static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        for (var position = 0; position < value.Length; position++)
        {
            var character = value[position];
            switch (character)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                case < ' ':
                    text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    break;
                default:
                    if (char.IsHighSurrogate(character) && position + 1 < value.Length && char.IsLowSurrogate(value[position + 1]))
                    {
                        text.Append(character).Append(value[++position]);
                    }
                    else if (char.IsSurrogate(character))
                    {
                        text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(character);
                    }

                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
