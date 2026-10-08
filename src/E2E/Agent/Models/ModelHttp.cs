// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Internal;

/// <summary>The HTTP every model client shares: one POST, the provider's errors mapped to agent codes.</summary>
internal static class ModelHttp
{
    /// <summary><c>e2e-dotnet/&lt;version&gt; (&lt;platform&gt;; &lt;arch&gt;)</c>: what every vendor request identifies as. Never another client's name.</summary>
    public static readonly string UserAgent = "e2e-dotnet/" + PackageVersion() + " (" + Platform() + "; " + Arch() + ")";

    /// <summary>
    /// Sent with every model call, whichever provider serves it, replacing the same headers set on the client.
    /// The .NET runtime follows our user agent, as the AI SDK appends its own upstream; <c>HTTP-Referer</c> and
    /// <c>X-Title</c> are the app attribution the Vercel AI Gateway and OpenRouter read, and others ignore.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RequestHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["User-Agent"] = UserAgent + " runtime/dotnet/" + System.Environment.Version,
        ["HTTP-Referer"] = "https://github.com/hardkoded/e2e-dotnet",
        ["X-Title"] = "e2e-dotnet",
    };

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Sends <paramref name="message"/> and returns the body of a successful answer. A timeout, an unreachable
    /// host, or an error status fails with <c>MODEL_PROVIDER_FAILED</c>.
    /// </summary>
    public static async Task<string> SendAsync(HttpClient http, HttpRequestMessage message, CancellationToken cancellationToken)
    {
        ModelEndpoint.AddHeaders(message, RequestHeaders);
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

    /// <summary>The package version MinVer stamps, without its build metadata.</summary>
    internal static string PackageVersion()
    {
        var version = typeof(ModelHttp).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        return version.Split('+')[0];
    }

    /// <summary>The operating system by Node's <c>process.platform</c> name, as upstream reports it.</summary>
    private static string Platform()
    {
        return OperatingSystem.IsMacOS() ? "darwin"
            : OperatingSystem.IsWindows() ? "win32"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsFreeBSD() ? "freebsd"
            : "unknown";
    }

    /// <summary>The process architecture by Node's <c>process.arch</c> name.</summary>
    private static string Arch()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "ia32",
            Architecture.LoongArch64 => "loong64",
            Architecture.Ppc64le => "ppc64",
            var other => other.ToString().ToLowerInvariant(),
        };
    }

    public static int Int(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;
    }
}
