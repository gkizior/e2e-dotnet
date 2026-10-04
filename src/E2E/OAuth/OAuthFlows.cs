// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.OAuth;

/// <summary>A token endpoint's answer.</summary>
internal sealed record TokenResponse(string AccessToken, string? RefreshToken, string? IdToken, double? ExpiresIn, JsonObject Raw);

/// <summary>The HTTP an OAuth flow is made of: form POSTs to a token endpoint, and the vendor's error text.</summary>
internal static class TokenEndpoint
{
    public static async Task<HttpResponseMessage> PostFormAsync(HttpClient http, string url, IReadOnlyDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", Internal.ModelHttp.UserAgent);
        return await OAuthHttp.SendAsync(http, request, new Uri(url).Host, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Posts a grant and returns the tokens. A 400 or 401 means the grant itself was rejected and maps to
    /// <paramref name="rejected"/>; anything else not OK is <see cref="OAuthException.FlowFailed"/>.
    /// </summary>
    public static async Task<TokenResponse> RequestAsync(HttpClient http, string vendor, string url, IReadOnlyDictionary<string, string> form, string rejected, CancellationToken cancellationToken)
    {
        using var response = await PostFormAsync(http, url, form, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var code = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized ? rejected : OAuthException.FlowFailed;
            throw new OAuthException(code, vendor + " token request failed (" + await DescribeAsync(response, cancellationToken).ConfigureAwait(false) + ")");
        }

        return Read(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), vendor)
            ?? throw new OAuthException(OAuthException.FlowFailed, "the " + vendor + " token response is missing access_token");
    }

    /// <summary>The tokens in a response body, or null when it carries no access token.</summary>
    public static TokenResponse? Read(string body, string vendor)
    {
        JsonObject json;
        try
        {
            json = JsonNode.Parse(body) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            throw new OAuthException(OAuthException.FlowFailed, "the " + vendor + " token response is not JSON", ex);
        }

        var access = Text(json, "access_token");
        if (string.IsNullOrEmpty(access))
        {
            return null;
        }

        return new TokenResponse(access, Text(json, "refresh_token"), Text(json, "id_token"), Number(json, "expires_in"), json);
    }

    public static string? Text(JsonObject json, string name)
    {
        return json[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    public static double? Number(JsonObject json, string name)
    {
        if (json[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    /// <summary>When a token expires in epoch milliseconds, from <c>expires_in</c>; one hour when the server names nothing usable.</summary>
    public static long ExpiryFrom(double? expiresIn)
    {
        var seconds = expiresIn is > 0 and < 1e9 ? expiresIn.Value : 3600;
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (long)(seconds * 1000);
    }

    /// <summary>Reads an error body for a message without assuming JSON.</summary>
    public static async Task<string> DescribeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        string text;
        try
        {
            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            text = "";
        }

        if (text.Length == 0)
        {
            return (status + " " + response.ReasonPhrase).Trim();
        }

        try
        {
            if (JsonNode.Parse(text) is JsonObject json)
            {
                var detail = Text(json, "error_description")
                    ?? (json["error"] is JsonObject error ? Text(error, "message") : null)
                    ?? Text(json, "message")
                    ?? Text(json, "error");
                if (detail is not null)
                {
                    return status + ": " + detail;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON.
        }

        return status + ": " + (text.Length > 200 ? text[..200] : text);
    }
}

/// <summary>The claims of a JWT, read without verifying it: only to find what the vendor put there for the client.</summary>
internal static class Jwt
{
    public static JsonObject? Claims(string? token)
    {
        var parts = token?.Split('.');
        if (parts is not { Length: 3 })
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            return JsonNode.Parse(Convert.FromBase64String(payload)) as JsonObject;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}

/// <summary>PKCE (RFC 7636) verifier and S256 challenge, plus an opaque state.</summary>
internal static class Pkce
{
    public static (string Verifier, string Challenge) Generate()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    public static string State() => Base64Url(RandomNumberGenerator.GetBytes(24));

    public static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>A device authorization: the code the user enters, where, and how often to poll.</summary>
internal sealed record DeviceAuthorization(string DeviceCode, string UserCode, string VerificationUri, string? VerificationUriComplete, double ExpiresIn, double Interval);

/// <summary>One poll of a device flow.</summary>
internal enum DevicePollStatus
{
    Granted,
    Pending,
    SlowDown,
    Denied,
    Expired,
}

internal sealed record DevicePoll<T>(DevicePollStatus Status, T? Value = default, double? IntervalSeconds = null);

/// <summary>
/// RFC 8628 device authorization: the server hands out a user code, the user enters it on any device, and the
/// client polls until the grant lands. <see cref="RunAsync"/> owns the timing for any vendor's shape;
/// <see cref="Rfc8628Async"/> is the standard shape itself.
/// </summary>
internal static class DeviceFlow
{
    /// <summary>Test seam: how the flow waits between polls.</summary>
    internal static Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = Task.Delay;

    public static async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<DeviceAuthorization>> start,
        Func<DeviceAuthorization, CancellationToken, Task<DevicePoll<T>>> poll,
        OAuthLoginCallbacks callbacks,
        Func<DeviceAuthorization, string>? instructions,
        CancellationToken cancellationToken)
    {
        var authorization = await start(cancellationToken).ConfigureAwait(false);
        callbacks.OnAuth(new OAuthAuthInfo(
            authorization.VerificationUriComplete ?? authorization.VerificationUri,
            instructions?.Invoke(authorization) ?? "Open " + authorization.VerificationUri + " on any device and enter the code " + authorization.UserCode + ".",
            authorization.UserCode));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Positive(authorization.ExpiresIn, 15 * 60));
        var interval = TimeSpan.FromSeconds(Math.Max(Positive(authorization.Interval, 5), 1));
        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            try
            {
                await Delay(interval < remaining ? interval : remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                throw new OAuthException(OAuthException.Cancelled, "the login was cancelled", ex);
            }

            var result = await poll(authorization, cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                throw new OAuthException(OAuthException.Cancelled, "the login was cancelled");
            }

            switch (result.Status)
            {
                case DevicePollStatus.Granted:
                    return result.Value!;
                case DevicePollStatus.SlowDown:
                    interval = result.IntervalSeconds is > 0 ? TimeSpan.FromSeconds(result.IntervalSeconds.Value) : interval + TimeSpan.FromSeconds(5);
                    break;
                case DevicePollStatus.Denied:
                    throw new OAuthException(OAuthException.Cancelled, "the authorization was denied");
                case DevicePollStatus.Expired:
                    throw new OAuthException(OAuthException.Timeout, "the device code expired before the login finished; run the login again");
            }
        }

        throw new OAuthException(OAuthException.Timeout, "the device code expired before the login finished; run the login again");
    }

    /// <summary>The standard device flow. The poll reads the body before the status, since vendors disagree on the status for "pending".</summary>
    public static Task<TokenResponse> Rfc8628Async(
        HttpClient http,
        string vendor,
        string deviceCodeUrl,
        string tokenUrl,
        string clientId,
        IReadOnlyDictionary<string, string> request,
        OAuthLoginCallbacks callbacks,
        CancellationToken cancellationToken,
        Func<DeviceAuthorization, string>? instructions = null)
    {
        return RunAsync<TokenResponse>(
            async token =>
            {
                var form = new Dictionary<string, string>(request, StringComparer.Ordinal) { ["client_id"] = clientId };
                using var response = await TokenEndpoint.PostFormAsync(http, deviceCodeUrl, form, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new OAuthException(OAuthException.FlowFailed, vendor + " device login could not start: " + await TokenEndpoint.DescribeAsync(response, token).ConfigureAwait(false));
                }

                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject ?? [];
                var deviceCode = TokenEndpoint.Text(json, "device_code");
                var userCode = TokenEndpoint.Text(json, "user_code");
                var verification = TokenEndpoint.Text(json, "verification_uri");
                if (deviceCode is null || userCode is null || verification is null)
                {
                    throw new OAuthException(OAuthException.FlowFailed, "the " + vendor + " device code response is missing fields");
                }

                return new DeviceAuthorization(
                    deviceCode,
                    userCode,
                    verification,
                    TokenEndpoint.Text(json, "verification_uri_complete"),
                    TokenEndpoint.Number(json, "expires_in") ?? 0,
                    TokenEndpoint.Number(json, "interval") ?? 0);
            },
            async (authorization, token) =>
            {
                var form = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = clientId,
                    ["device_code"] = authorization.DeviceCode,
                };
                using var response = await TokenEndpoint.PostFormAsync(http, tokenUrl, form, token).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                JsonObject json;
                try
                {
                    json = JsonNode.Parse(body) as JsonObject ?? [];
                }
                catch (JsonException)
                {
                    json = [];
                }

                var granted = TokenEndpoint.Read(json.ToJsonString(), vendor);
                if (granted is not null)
                {
                    return new DevicePoll<TokenResponse>(DevicePollStatus.Granted, granted);
                }

                return TokenEndpoint.Text(json, "error") switch
                {
                    "authorization_pending" => new DevicePoll<TokenResponse>(DevicePollStatus.Pending),
                    "slow_down" => new DevicePoll<TokenResponse>(DevicePollStatus.SlowDown, IntervalSeconds: TokenEndpoint.Number(json, "interval")),
                    "access_denied" or "authorization_denied" => new DevicePoll<TokenResponse>(DevicePollStatus.Denied),
                    "expired_token" => new DevicePoll<TokenResponse>(DevicePollStatus.Expired),
                    _ => throw new OAuthException(
                        OAuthException.FlowFailed,
                        vendor + " device login failed: " + (TokenEndpoint.Text(json, "error_description") ?? TokenEndpoint.Text(json, "error") ?? ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture))),
                };
            },
            callbacks,
            instructions,
            cancellationToken);
    }

    private static double Positive(double value, double fallback)
    {
        return double.IsFinite(value) && value > 0 ? value : fallback;
    }
}
