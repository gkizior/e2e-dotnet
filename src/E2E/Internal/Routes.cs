// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Internal;

internal static class Routes
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

    public static string Resolve(string? baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "/";
        }

        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return url.StartsWith('/') ? url : "/" + url;
        }

        var left = baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
        var right = url.StartsWith('/') ? url[1..] : url;
        return new Uri(new Uri(left, UriKind.Absolute), right).AbsoluteUri;
    }
}
