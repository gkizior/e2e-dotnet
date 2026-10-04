// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.OAuth;

/// <summary>HTTP the logins share.</summary>
internal static class OAuthHttp
{
    /// <summary>The client login flows use when the caller gives none.</summary>
    public static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Sends <paramref name="request"/>; an unreachable host is <see cref="OAuthException.FlowFailed"/> naming the vendor.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, string vendor, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new OAuthException(OAuthException.FlowFailed, vendor + " could not be reached: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException(OAuthException.FlowFailed, vendor + " did not answer in time", ex);
        }
    }

    /// <summary>A JSON object from a GET, or <see cref="OAuthException.FlowFailed"/> naming the vendor.</summary>
    public static async Task<JsonObject> GetJsonAsync(HttpClient http, string url, string vendor, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(http, request, vendor, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthException(OAuthException.FlowFailed, vendor + " did not list its models: " + await TokenEndpoint.DescribeAsync(response, cancellationToken).ConfigureAwait(false));
        }

        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            throw new OAuthException(OAuthException.FlowFailed, vendor + " answered with a body that is not JSON", ex);
        }
    }
}
