// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Internal;

/// <summary>The HTTP every model client shares: one POST, the provider's errors mapped to agent codes.</summary>
internal static class ModelHttp
{
    /// <summary>What every model request identifies as. Never another client's name.</summary>
    public const string UserAgent = "e2e-dotnet";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Sends <paramref name="message"/> and returns the body of a successful answer. A timeout, an unreachable
    /// host, or an error status fails with <c>MODEL_PROVIDER_FAILED</c>.
    /// </summary>
    public static async Task<string> SendAsync(HttpClient http, HttpRequestMessage message, CancellationToken cancellationToken)
    {
        message.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AgentException("MODEL_PROVIDER_FAILED", "The model request got no response within 120 seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new AgentException("MODEL_PROVIDER_FAILED", "The model provider could not be reached.", ex);
        }
        catch (OAuth.OAuthException ex)
        {
            // A missing or rejected subscription login stops the step; signing in again is the remedy.
            throw new AgentException("MODEL_PROVIDER_FAILED", "The subscription login failed (" + ex.Code + "): " + ex.Message, blocked: true, ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = body.Length > 400 ? body[..400] : body;
                throw new AgentException(
                    "MODEL_PROVIDER_FAILED",
                    "The model provider returned " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ". " + detail);
            }

            return body;
        }
    }

    /// <summary>Parses a provider answer, mapping unreadable JSON to <c>MODEL_OUTPUT_INVALID</c>.</summary>
    public static ModelResponse Parse(string body, Func<JsonElement, ModelResponse> parse)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return parse(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            throw new AgentException("MODEL_OUTPUT_INVALID", "The model provider returned JSON that could not be read.", ex);
        }
    }

    public static StringContent Json(JsonNode payload)
    {
        return new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
    }

    /// <summary>Tool arguments as a JSON element; an empty string is an empty object.</summary>
    public static JsonElement Arguments(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            text = "{}";
        }

        using var parsed = JsonDocument.Parse(text);
        return parsed.RootElement.Clone();
    }

    /// <summary>
    /// Adds the agent's provider options under <paramref name="provider"/> to <paramref name="payload"/>, as
    /// given. They cannot replace a field the client itself sets.
    /// </summary>
    public static void MergeProviderOptions(JsonObject payload, ModelRequest request, string provider, params string[] reserved)
    {
        if (request.ProviderOptions is null || !request.ProviderOptions.TryGetValue(provider, out var extra))
        {
            return;
        }

        if (extra.ValueKind != JsonValueKind.Object)
        {
            throw new ConfigurationException("INVALID_CONFIG", "providerOptions." + provider + " must be an object of provider options");
        }

        foreach (var field in extra.EnumerateObject())
        {
            if (reserved.Contains(field.Name, StringComparer.Ordinal))
            {
                throw new ConfigurationException("INVALID_CONFIG", "providerOptions." + provider + "." + field.Name + " cannot replace a field the client sets");
            }

            payload[field.Name] = JsonNode.Parse(field.Value.GetRawText());
        }
    }

    /// <summary>A stable key for one system prompt, so every call sharing the prefix routes to the same provider cache.</summary>
    public static string PromptCacheKey(string system)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(system));
        return "e2e-" + Convert.ToHexStringLower(hash)[..16];
    }

    /// <summary>The value of <paramref name="name"/> in the environment, or null when it is unset or blank.</summary>
    public static string? Environment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var value = System.Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>The tool name each earlier tool call id was made with, for protocols that answer a call by name.</summary>
    public static Dictionary<string, string> ToolNames(IReadOnlyList<ModelMessage> messages)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            foreach (var call in message.ToolCalls ?? [])
            {
                names[call.Id] = call.Name;
            }
        }

        return names;
    }

    public static int Int(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }
}
