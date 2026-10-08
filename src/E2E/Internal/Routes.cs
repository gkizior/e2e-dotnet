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

        // Checked by scheme because on Unix Uri reads "/x?y" as the absolute file:///x%3Fy.
        if (SchemePattern().IsMatch(url) && Uri.TryCreate(url, UriKind.Absolute, out var absolute))
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
        var resolved = Absolute(baseUrl, url);

        // An allowlist, because a browser wraps and nests schemes (view-source:file:, blob:, filesystem:)
        // and a list of forbidden ones misses the wrapper. The exact about:blank loads nothing.
        if (resolved.Scheme is not ("http" or "https") && resolved.AbsoluteUri != "about:blank")
        {
            throw new TestException("POLICY_DENIED", "forbidden URL scheme: " + resolved.Scheme + ":");
        }

        return resolved.AbsoluteUri;
    }

    /// <summary>Resolves a URL against the base URL without the navigation scheme rule, for comparing URLs.</summary>
    public static Uri Absolute(string? baseUrl, string? url)
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
            throw new TestException("POLICY_DENIED", "malformed URL: " + url);
        }

        return resolved;
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

    /// <summary>The URL test for a route pattern, a glob string (<see cref="CompilePattern"/>, compiled once) or a <see cref="Regex"/>.</summary>
    public static Func<string, bool> Matcher(object pattern) =>
        pattern is Regex regex ? regex.IsMatch : CompilePattern((string)pattern).IsMatch;

    /// <summary>Structural equality for route patterns, used by unroute: the same glob, or the same regex source and options.</summary>
    public static bool PatternsEqual(object a, object b) => (a, b) switch
    {
        (string x, string y) => string.Equals(x, y, StringComparison.Ordinal),
        (Regex x, Regex y) => string.Equals(x.ToString(), y.ToString(), StringComparison.Ordinal) && x.Options == y.Options,
        _ => false,
    };

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemePattern();
}
