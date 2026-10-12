// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace E2E.Mobile.Limrun;

/// <summary>A Limrun instance as the API reports it: its id, whether it is ready, and the address and token that control it.</summary>
internal sealed record LimrunInstance(string Id, string State, string? ApiUrl, string? Token, string? AdbWebSocketUrl, string? ErrorMessage, string? TerminationReason)
{
    public static LimrunInstance Parse(JsonNode? node) => new(
        Text(node?["metadata"]?["id"]) ?? throw new LimrunApiException(HttpStatusCode.OK, "Limrun returned an instance without an id"),
        Text(node?["status"]?["state"]) ?? "unknown",
        Text(node?["status"]?["apiUrl"]),
        Text(node?["status"]?["token"]),
        Text(node?["status"]?["adbWebSocketUrl"]),
        Text(node?["status"]?["errorMessage"]),
        Text(node?["status"]?["terminationReason"]));

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}

/// <summary>Limrun answered with an error status.</summary>
internal sealed class LimrunApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>
/// The part of Limrun's REST API the provider uses (<c>@limrun/api</c>'s <c>iosInstances</c> and <c>androidInstances</c>):
/// create, get, and delete an instance, with the API key as a bearer token. The SDK retries nothing in the provider's
/// use of it, since a retried create after an ambiguous answer could allocate a second device, and neither does this.
/// </summary>
internal sealed class LimrunApi(HttpClient http, string apiKey, Uri baseUrl)
{
    /// <summary>How long one request may take, as the provider configures the SDK.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private static string Resource(MobilePlatform platform) => platform == MobilePlatform.Ios ? "ios_instances" : "android_instances";

    public async Task<LimrunInstance> CreateAsync(MobilePlatform platform, JsonObject body, CancellationToken cancellationToken) =>
        LimrunInstance.Parse(await SendAsync(HttpMethod.Post, $"/v1/{Resource(platform)}", body, cancellationToken).ConfigureAwait(false));

    public async Task<LimrunInstance> GetAsync(MobilePlatform platform, string id, CancellationToken cancellationToken) =>
        LimrunInstance.Parse(await SendAsync(HttpMethod.Get, $"/v1/{Resource(platform)}/{Uri.EscapeDataString(id)}", null, cancellationToken).ConfigureAwait(false));

    public Task DeleteAsync(MobilePlatform platform, string id, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Delete, $"/v1/{Resource(platform)}/{Uri.EscapeDataString(id)}", null, cancellationToken);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(method, new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + path));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Headers.Add("X-Limrun-Client", "e2e");
        if (body is not null)
        {
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var reply = await http.SendAsync(message, timeout.Token).ConfigureAwait(false);
        var text = await reply.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!reply.IsSuccessStatusCode)
        {
            throw new LimrunApiException(reply.StatusCode, $"Limrun answered {(int)reply.StatusCode} to {method} {path}: {Cut(text)}");
        }

        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }

    private static string Cut(string text) => text.Length <= 1024 ? text : text[..1024] + "...";
}
