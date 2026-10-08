// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.RegularExpressions;

namespace E2E.Internal;

internal static partial class Routes
{
    public static string PathOf(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return "/";
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            return string.IsNullOrEmpty(absolute.AbsolutePath) ? "/" : absolute.AbsolutePath;
        }

        var cut = url.IndexOfAny(['?', '#']);
        var path = cut >= 0 ? url[..cut] : url;
        if (path.Length == 0)
        {
            return "/";
        }

        return path.StartsWith('/') ? path : "/" + path;
    }

    public static string Resolve(string? baseUrl, string? url)
    {
        // No URL opens the base URL itself, as an empty reference does in WHATWG resolution.
        url = url?.Trim() ?? string.Empty;
        Uri? resolved;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // Checked by scheme because on Unix Uri reads "/x" as the absolute file:///x.
            if (!SchemePattern().IsMatch(url))
            {
                throw new TestException("APP_URL_REQUIRED", "Navigation to \"" + url + "\" needs an app URL; the test declares no base URL.");
            }

            Uri.TryCreate(url, UriKind.Absolute, out resolved);
        }
        else
        {
            Uri.TryCreate(new Uri(baseUrl, UriKind.Absolute), url, out resolved);
        }

        if (resolved is null)
        {
            throw new TestException("POLICY_DENIED", "Malformed URL: " + url);
        }

        if (resolved.Scheme is "file" or "data" or "javascript")
        {
            throw new TestException("POLICY_DENIED", "Forbidden URL scheme: " + resolved.Scheme + ":");
        }

        return resolved.AbsoluteUri;
    }

    /// <summary>
    /// Compiles a URL glob with upstream's route pattern grammar: <c>*</c> matches
    /// within one path segment, <c>**</c> crosses <c>/</c>, <c>?</c> matches one
    /// character, <c>\</c> escapes the next character; everything else is literal.
    /// The pattern matches the complete URL.
    /// </summary>
    public static Regex CompilePattern(string pattern)
    {
        var source = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var ch = pattern[i];
            if (ch == '\\')
            {
                i++;
                if (i < pattern.Length)
                {
                    source.Append(Regex.Escape(pattern[i].ToString()));
                }

                continue;
            }

            if (ch == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    source.Append(".*");
                }
                else
                {
                    source.Append("[^/]*");
                }

                continue;
            }

            source.Append(ch == '?' ? "." : Regex.Escape(ch.ToString()));
        }

        return new Regex(source.Append('$').ToString(), RegexOptions.CultureInvariant);
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemePattern();
}
