// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Internal;

/// <summary>
/// Argument validation for a route handler's decision. Every option is checked
/// before the decision is taken. A continue URL goes through the navigation
/// rule, the same <see cref="Routes.Resolve"/> <c>app.open</c> uses.
/// </summary>
internal static partial class RouteOptions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Validates <c>route.fulfill(response)</c>. <c>Path</c> resolves with
    /// <paramref name="resolvePath"/> and must name a file now, and <c>Json</c> is
    /// serialized into the body. A <c>content-type</c> header becomes
    /// <c>ContentType</c> when none is given, since the engine overwrites the
    /// header with the type it derives for a file.
    /// </summary>
    public static RouteFulfillResponse ParseFulfill(RouteFulfillResponse? response, Func<string, string> resolvePath)
    {
        if (response is null)
        {
            throw Invalid("route.fulfill takes an options object");
        }

        var sources = new[] { ("json", response.Json is not null), ("body", response.Body is not null), ("path", response.Path is not null) }
            .Where(source => source.Item2)
            .Select(source => source.Item1)
            .ToList();
        if (sources.Count > 1)
        {
            throw Invalid("route.fulfill takes one of json, body, or path; got " + string.Join(" and ", sources));
        }

        if (response.Status is < 100 or > 599)
        {
            throw Invalid("route.fulfill status must be an integer from 100 to 599");
        }

        var body = response.Json is null ? response.Body : SerializeJson(response.Json);
        var file = response.Path is null ? null : resolvePath(RequireString("route.fulfill", "path", response.Path, nonempty: true));
        if (file is not null && !File.Exists(file))
        {
            throw Invalid("route.fulfill path is not a readable file: " + file);
        }

        var headers = RequireHeaders("route.fulfill", response.Headers) ?? new Dictionary<string, string>();
        var type = response.ContentType is null
            ? headers.FirstOrDefault(header => string.Equals(header.Key, "content-type", StringComparison.OrdinalIgnoreCase)).Value
            : RequireString("route.fulfill", "contentType", response.ContentType, nonempty: true);
        return new RouteFulfillResponse
        {
            Status = response.Status ?? 200,
            Headers = headers,
            ContentType = type ?? (response.Json is null ? null : "application/json"),
            Body = file is null ? body ?? "" : null,
            Path = file,
        };
    }

    /// <summary>
    /// Validates <c>route.continue(overrides)</c>. A <c>Url</c> resolves against the
    /// base URL through <paramref name="resolveUrl"/>, which denies what navigation
    /// denies, and keeps the request's scheme, as the browser requires of a
    /// rewritten request.
    /// </summary>
    public static RouteContinueOverrides ParseContinue(RouteContinueOverrides? overrides, string requestUrl, Func<string, string> resolveUrl)
    {
        overrides ??= new RouteContinueOverrides();
        string? target = null;
        if (overrides.Url is not null)
        {
            target = resolveUrl(RequireString("route.continue", "url", overrides.Url, nonempty: true));
            var from = Protocol(requestUrl);
            var to = Protocol(target);
            if (from != to)
            {
                throw Invalid("route.continue url must keep the request's " + from + " scheme; got " + to);
            }
        }

        return new RouteContinueOverrides
        {
            Url = target,
            Method = overrides.Method is null ? null : RequireString("route.continue", "method", overrides.Method, nonempty: true),
            Headers = RequireHeaders("route.continue", overrides.Headers),
            PostData = overrides.PostData,
        };
    }

    /// <summary>
    /// Why the browser could not send this header, or null when it can: a name
    /// outside the token grammar, or a value carrying a control character (a line
    /// break is a header-injection vector).
    /// </summary>
    public static string? HeaderProblem(string name, string? value)
    {
        if (!HeaderName().IsMatch(name))
        {
            return "has an invalid header name: \"" + name + "\"";
        }

        if (value is null)
        {
            // JavaScript's typeof null, the word upstream reports.
            return "header \"" + name + "\" must be a string, got object";
        }

        return FieldValueControl().IsMatch(value) ? "header \"" + name + "\" must not contain a control character" : null;
    }

    private static string SerializeJson(object json)
    {
        try
        {
            return JsonSerializer.Serialize(json, Json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new TestException("INVALID_ARGUMENT", "route.fulfill json is not a JSON value: " + ex.Message, ex);
        }
    }

    private static Dictionary<string, string>? RequireHeaders(string api, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        foreach (var (name, value) in headers)
        {
            if (HeaderProblem(name, value) is { } problem)
            {
                throw Invalid(api + " " + problem);
            }
        }

        return new Dictionary<string, string>(headers, StringComparer.Ordinal);
    }

    private static string RequireString(string api, string key, string value, bool nonempty)
    {
        if (nonempty && value.Length == 0)
        {
            throw Invalid(api + " " + key + " must be a nonempty string");
        }

        return value;
    }

    /// <summary>The scheme with its colon, lower-cased, as WHATWG <c>URL.protocol</c> reads it.</summary>
    private static string Protocol(string url) => new Uri(url, UriKind.Absolute).Scheme.ToLowerInvariant() + ":";

    private static TestException Invalid(string message) => new("INVALID_ARGUMENT", message);

    /// <summary>An HTTP header field name: one or more <c>token</c> characters (RFC 9110).</summary>
    [GeneratedRegex("^[!#$%&'*+.^_`|~0-9A-Za-z-]+$")]
    private static partial Regex HeaderName();

    /// <summary>A control character no HTTP field value may carry; a horizontal tab is the one the grammar allows.</summary>
    [GeneratedRegex("[\\u0000-\\u0008\\u000A-\\u001F\\u007F]")]
    private static partial Regex FieldValueControl();
}
