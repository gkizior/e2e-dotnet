// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.OAuth;

/// <summary>Why a subscription login or a request made with it failed. <see cref="E2EException.Code"/> is one of the constants.</summary>
public sealed class OAuthException : E2EException
{
    /// <summary>No stored credentials for the provider.</summary>
    public const string NotLoggedIn = "NOT_LOGGED_IN";

    /// <summary>The token or refresh token was rejected; the user signs in again.</summary>
    public const string LoginRequired = "LOGIN_REQUIRED";

    /// <summary>The authorization server or the vendor API answered with an error during a flow.</summary>
    public const string FlowFailed = "FLOW_FAILED";

    /// <summary>The user or the caller cancelled the flow.</summary>
    public const string Cancelled = "CANCELLED";

    /// <summary>The flow ran out of time waiting for the user.</summary>
    public const string Timeout = "TIMEOUT";

    /// <summary>The flow needs an option the caller did not give (a client id, a CLI).</summary>
    public const string Misconfigured = "MISCONFIGURED";

    public OAuthException(string code, string message)
        : base(code, message)
    {
    }

    public OAuthException(string code, string message, Exception inner)
        : base(code, message, inner)
    {
    }
}

/// <summary>
/// What a login returns and a refresh renews. Stored as upstream stores it: <c>access</c>, <c>refresh</c>,
/// <c>expires</c> (epoch milliseconds, 0 when the token does not expire), plus each provider's own fields.
/// </summary>
public sealed class OAuthCredentials
{
    public required string Access { get; init; }

    /// <summary>Empty when the vendor issues non-expiring tokens.</summary>
    public string Refresh { get; init; } = "";

    /// <summary>Epoch milliseconds; 0 when the token does not expire.</summary>
    public long Expires { get; init; }

    /// <summary>Provider fields: <c>accountId</c>, <c>residency</c>, <c>enterpriseUrl</c>, <c>orgId</c>.</summary>
    public IReadOnlyDictionary<string, string> Extra { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public string? Get(string name) => Extra.TryGetValue(name, out var value) ? value : null;

    /// <summary>A copy with <paramref name="name"/> set, or removed when <paramref name="value"/> is null.</summary>
    public OAuthCredentials With(string name, string? value)
    {
        var extra = new Dictionary<string, string>(Extra, StringComparer.Ordinal);
        if (value is null)
        {
            extra.Remove(name);
        }
        else
        {
            extra[name] = value;
        }

        return new OAuthCredentials { Access = Access, Refresh = Refresh, Expires = Expires, Extra = extra };
    }

    internal JsonObject ToJson()
    {
        var json = new JsonObject { ["access"] = Access, ["refresh"] = Refresh, ["expires"] = Expires };
        foreach (var (name, value) in Extra)
        {
            json[name] = value;
        }

        return json;
    }

    /// <summary>An entry of the credentials file, or null when it is damaged.</summary>
    internal static OAuthCredentials? FromJson(JsonNode? node)
    {
        if (node is not JsonObject json
            || json["access"] is not JsonValue access || !access.TryGetValue<string>(out var accessText)
            || json["refresh"] is not JsonValue refresh || !refresh.TryGetValue<string>(out var refreshText)
            || json["expires"] is not JsonValue expires || expires.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }

        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in json)
        {
            if (name is not ("access" or "refresh" or "expires") && value is JsonValue text && text.TryGetValue<string>(out var s))
            {
                extra[name] = s;
            }
        }

        return new OAuthCredentials { Access = accessText, Refresh = refreshText, Expires = (long)expires.GetValue<double>(), Extra = extra };
    }
}
