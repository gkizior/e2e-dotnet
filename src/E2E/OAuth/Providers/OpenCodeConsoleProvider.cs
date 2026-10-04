// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.OAuth;

/// <summary>
/// OpenCode Zen and Go through an OpenCode Console sign-in. Console speaks RFC 8628 device authorization;
/// approving the code binds the login to one workspace, and its token is a bearer for both the Console API and
/// inference. Refresh tokens rotate. The workspace config says which models are served and over which protocol:
/// Zen is its <c>opencode</c> provider, Go <c>opencode-go</c>. Go ids take a <c>go/</c> prefix here.
/// </summary>
public sealed class OpenCodeConsoleProvider : IOAuthProvider
{
    /// <summary>Console accepts any client id.</summary>
    private const string ClientId = "e2e";
    private const string ZenProvider = "opencode";
    private const string GoProvider = "opencode-go";

    public const string GoPrefix = "go/";
    public const string DefaultConsoleUrl = "https://opencode.ai/console";
    public const string InferenceUrl = "https://opencode.ai/inference";

    private readonly HttpClient _http;

    /// <param name="http">Sends the login's own requests. Unset, a shared client.</param>
    /// <param name="consoleUrl">Test seam.</param>
    public OpenCodeConsoleProvider(HttpClient? http = null, string consoleUrl = DefaultConsoleUrl)
    {
        _http = http ?? OAuthHttp.Shared;
        ConsoleUrl = consoleUrl;
    }

    public string Id => "opencode-console";

    public string Name => "OpenCode Console";

    internal string ConsoleUrl { get; }

    public async Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        var deviceCodeUrl = ConsoleUrl + "/auth/device/code";

        // Console answers with a relative verification URL.
        var relative = new OAuthLoginCallbacks
        {
            OnPrompt = callbacks.OnPrompt,
            OnProgress = callbacks.OnProgress,
            OnAuth = info =>
            {
                var url = new Uri(new Uri(deviceCodeUrl), info.Url).AbsoluteUri;
                callbacks.OnAuth(info with
                {
                    Url = url,
                    Instructions = "Open " + url + " on any device, pick the workspace to sign in to" + (info.UserCode is null ? "" : ", and confirm the code " + info.UserCode) + ".",
                });
            },
        };
        var tokens = await DeviceFlow.Rfc8628Async(
            _http,
            Name,
            deviceCodeUrl,
            ConsoleUrl + "/auth/device/token",
            ClientId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["supports_org_scope"] = "true" },
            relative,
            cancellationToken).ConfigureAwait(false);
        return ToCredentials(tokens, null);
    }

    public async Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.Refresh.Length == 0)
        {
            throw new OAuthException(OAuthException.LoginRequired, "OpenCode Console has no refresh token for this login");
        }

        var tokens = await TokenEndpoint.RequestAsync(
            _http,
            Name,
            ConsoleUrl + "/auth/device/token",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["grant_type"] = "refresh_token", ["refresh_token"] = credentials.Refresh, ["client_id"] = ClientId },
            OAuthException.LoginRequired,
            cancellationToken).ConfigureAwait(false);
        return ToCredentials(tokens, credentials);
    }

    /// <summary>Names the workspace as each API expects it.</summary>
    public Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.Get("orgId") is { } org)
        {
            var console = request.RequestUri?.AbsoluteUri.StartsWith(ConsoleUrl + "/", StringComparison.Ordinal) == true;
            request.Headers.TryAddWithoutValidation(console ? "x-org-id" : "x-opencode-org-id", org);
        }

        return Task.CompletedTask;
    }

    /// <summary>Leaves out disabled models and Zen's free ones, which only OpenCode's own clients may use.</summary>
    public async Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        var providers = await ReadConfigAsync(http, cancellationToken).ConfigureAwait(false);
        return [.. Listed(providers, ZenProvider, "", "Zen"), .. Listed(providers, GoProvider, GoPrefix, "Go")];
    }

    /// <summary>
    /// How <paramref name="modelId"/> is served: the protocol (<c>openai-compatible</c>, <c>openai</c>,
    /// <c>anthropic</c>, <c>google</c>), the API root, and the vendor's model id. Null when the config could not be
    /// read, so the caller asks again rather than remembering a guess.
    /// </summary>
    internal async Task<OpenCodeRoute?> RouteForAsync(string modelId, HttpClient http, CancellationToken cancellationToken)
    {
        JsonObject providers;
        try
        {
            providers = await ReadConfigAsync(http, cancellationToken).ConfigureAwait(false);
        }
        catch (OAuthException ex) when (ex.Code == OAuthException.FlowFailed)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        var (go, id) = Split(modelId);
        if (providers[go ? GoProvider : ZenProvider] is not JsonObject provider)
        {
            throw new OAuthException(
                OAuthException.Misconfigured,
                go
                    ? "OpenCode Go is not available to this login: the workspace has no Go subscription, or the signed-in user is not its subscriber; sign in to the Go workspace with `e2e login opencode-console`"
                    : "OpenCode Zen is turned off for this OpenCode Console workspace");
        }

        if (provider["models"]?[id] is not JsonObject model || IsTrue(model["disabled"]))
        {
            throw new OAuthException(OAuthException.Misconfigured, "the OpenCode Console workspace does not serve " + modelId + "; `e2e models opencode-console` lists the ids");
        }

        var npm = (model["provider"] is JsonObject own ? TokenEndpoint.Text(own, "npm") : null) ?? TokenEndpoint.Text(provider, "npm");
        var api = (model["provider"] is JsonObject ownApi ? TokenEndpoint.Text(ownApi, "api") : null) ?? TokenEndpoint.Text(provider, "api");
        if (npm is null || api is null)
        {
            return null;
        }

        return new OpenCodeRoute(go, Protocol(npm), api, TokenEndpoint.Text(model, "id") is { Length: > 0 } vendorId ? vendorId : id);
    }

    /// <summary>The plan's chat completions route, used until the config is read.</summary>
    internal static OpenCodeRoute ChatRoute(string modelId)
    {
        var (go, id) = Split(modelId);
        return new OpenCodeRoute(go, "openai-compatible", InferenceUrl + (go ? "/go" : "") + "/openai/v1", id);
    }

    internal static (bool Go, string Id) Split(string modelId)
    {
        return modelId.StartsWith(GoPrefix, StringComparison.Ordinal) ? (true, modelId[GoPrefix.Length..]) : (false, modelId);
    }

    private static string Protocol(string npm)
    {
        return npm switch
        {
            "@ai-sdk/openai" => "openai",
            "@ai-sdk/anthropic" => "anthropic",
            "@ai-sdk/google" => "google",
            _ => "openai-compatible",
        };
    }

    private async Task<JsonObject> ReadConfigAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ConsoleUrl + "/api/config");
        using var response = await OAuthHttp.SendAsync(http, request, Name, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new OAuthException(OAuthException.FlowFailed, "OpenCode Console did not return the workspace config: " + await TokenEndpoint.DescribeAsync(response, cancellationToken).ConfigureAwait(false));
        }

        try
        {
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))?["config"]?["provider"] as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            throw new OAuthException(OAuthException.FlowFailed, "OpenCode Console returned a workspace config that is not JSON", ex);
        }
    }

    private static IEnumerable<SubscriptionModel> Listed(JsonObject providers, string key, string prefix, string plan)
    {
        if (providers[key]?["models"] is not JsonObject models)
        {
            yield break;
        }

        foreach (var (id, node) in models)
        {
            if (node is not JsonObject model || IsTrue(model["disabled"]) || (key == ZenProvider && IsFree(model)))
            {
                continue;
            }

            var detail = new List<string> { plan };
            if (key == ZenProvider && model["cost"] is JsonObject cost && TokenEndpoint.Number(cost, "input") is { } input && TokenEndpoint.Number(cost, "output") is { } output)
            {
                detail.Add("$" + input.ToString(CultureInfo.InvariantCulture) + " in, $" + output.ToString(CultureInfo.InvariantCulture) + " out per 1M");
            }

            if (model["modalities"]?["input"] is JsonArray modalities && modalities.Any(kind => kind?.ToString() == "image"))
            {
                detail.Add("vision");
            }

            yield return new SubscriptionModel(prefix + id, TokenEndpoint.Text(model, "name") is { Length: > 0 } name ? name : null, string.Join(", ", detail));
        }
    }

    private static bool IsFree(JsonObject model)
    {
        return model["cost"] is JsonObject cost && TokenEndpoint.Number(cost, "input") == 0 && TokenEndpoint.Number(cost, "output") == 0;
    }

    private static bool IsTrue(JsonNode? node)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.True;
    }

    /// <summary>A refresh keeps the workspace and the refresh token when the grant omits them.</summary>
    private static OAuthCredentials ToCredentials(TokenResponse tokens, OAuthCredentials? previous)
    {
        var org = TokenEndpoint.Text(tokens.Raw, "org_id") is { Length: > 0 } fresh ? fresh : previous?.Get("orgId");
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (org is not null)
        {
            extra["orgId"] = org;
        }

        return new OAuthCredentials
        {
            Access = tokens.AccessToken,
            Refresh = tokens.RefreshToken ?? previous?.Refresh ?? "",
            Expires = TokenEndpoint.ExpiryFrom(tokens.ExpiresIn),
            Extra = extra,
        };
    }
}

/// <summary>How an OpenCode Console model is served.</summary>
internal sealed record OpenCodeRoute(bool Go, string Protocol, string BaseUrl, string ModelId);
