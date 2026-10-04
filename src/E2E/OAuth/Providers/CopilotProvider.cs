// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace E2E.OAuth;

/// <summary>
/// GitHub Copilot. The Copilot API accepts a GitHub sign-in as the bearer, so the login is either the token the
/// GitHub CLI already holds or GitHub's device flow for an OAuth App you register. GitHub user tokens do not
/// expire; a 401 means signing in again.
/// </summary>
public sealed partial class CopilotProvider : IOAuthProvider
{
    public const string ApiUrl = "https://api.githubcopilot.com";

    private readonly HttpClient _http;
    private readonly string? _githubUrl;
    private readonly Func<string?, CancellationToken, Task<string?>> _cliToken;

    /// <param name="http">Sends the login's own requests. Unset, a shared client.</param>
    /// <param name="githubUrl">Test seam: the GitHub host the device flow talks to.</param>
    /// <param name="githubCliToken">Test seam: how the GitHub CLI's token is read for a host.</param>
    public CopilotProvider(HttpClient? http = null, string? githubUrl = null, Func<string?, CancellationToken, Task<string?>>? githubCliToken = null)
    {
        _http = http ?? OAuthHttp.Shared;
        _githubUrl = githubUrl;
        _cliToken = githubCliToken ?? GitHubCliTokenAsync;
    }

    public string Id => "github-copilot";

    public string Name => "GitHub Copilot";

    public async Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(options);
        var enterprise = options.EnterpriseUrl is null ? null : EnterpriseHost(options.EnterpriseUrl);
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (enterprise is not null)
        {
            extra["enterpriseUrl"] = enterprise;
        }

        if (options.ClientId is not null && !options.FromGitHubCli)
        {
            var github = _githubUrl ?? "https://" + (enterprise ?? "github.com");
            var tokens = await DeviceFlow.Rfc8628Async(
                _http,
                "GitHub",
                github + "/login/device/code",
                github + "/login/oauth/access_token",
                options.ClientId,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = "read:user" },
                callbacks,
                cancellationToken).ConfigureAwait(false);
            return new OAuthCredentials { Access = tokens.AccessToken, Refresh = "", Expires = 0, Extra = extra };
        }

        var token = await _cliToken(enterprise, cancellationToken).ConfigureAwait(false)
            ?? throw new OAuthException(
                OAuthException.Misconfigured,
                "GitHub Copilot login needs either the GitHub CLI signed in (gh auth login) or the client id of a GitHub OAuth App with the device flow enabled");
        callbacks.OnProgress?.Invoke("Using the GitHub CLI token");
        return new OAuthCredentials { Access = token, Refresh = "", Expires = 0, Extra = extra };
    }

    public Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        throw new OAuthException(OAuthException.LoginRequired, "GitHub rejected the Copilot token");
    }

    /// <summary>Routes an enterprise login at its host and adds Copilot's headers: who initiated the turn, and that images are present.</summary>
    public async Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.Get("enterpriseUrl") is { Length: > 0 } enterprise && request.RequestUri is { } uri
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), ApiUrl, StringComparison.OrdinalIgnoreCase))
        {
            request.RequestUri = new UriBuilder(uri) { Host = new Uri(BaseUrl(enterprise)).Host }.Uri;
        }

        request.Headers.TryAddWithoutValidation("openai-intent", "conversation-edits");
        var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var (initiator, vision) = Turn(text);
        request.Headers.TryAddWithoutValidation("x-initiator", initiator);
        if (vision)
        {
            request.Headers.TryAddWithoutValidation("copilot-vision-request", "true");
        }
    }

    /// <summary>
    /// The chat models the plan serves. A model it cannot call is listed but marked: one the plan has not enabled,
    /// or one Copilot serves only over APIs these clients do not speak.
    /// </summary>
    public async Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var payload = await OAuthHttp.GetJsonAsync(http, ApiUrl + "/models", Name, cancellationToken).ConfigureAwait(false);
        return (payload["data"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(model => !string.IsNullOrEmpty(TokenEndpoint.Text(model, "id"))
                && (model["capabilities"]?["type"] is null || model["capabilities"]?["type"]?.ToString() == "chat"))
            .Select(model =>
            {
                var supports = model["capabilities"]?["supports"];
                var detail = new List<string?>
                {
                    TokenEndpoint.Text(model, "vendor"),
                    IsTrue(supports?["tool_calls"]) ? "tools" : null,
                    IsTrue(supports?["vision"]) ? "vision" : null,
                    IsTrue(model["preview"]) ? "preview" : null,
                    model["policy"]?["state"] is JsonValue state && state.ToString() != "enabled" ? "not enabled" : null,
                    Protocol(model) is null ? "no chat or responses" : null,
                }.OfType<string>().ToList();
                return new SubscriptionModel(
                    TokenEndpoint.Text(model, "id")!,
                    TokenEndpoint.Text(model, "name") is { Length: > 0 } name ? name : null,
                    detail.Count == 0 ? null : string.Join(", ", detail));
            })
            .ToList();
    }

    /// <summary>
    /// Which endpoint reaches <paramref name="modelId"/>: <c>chat</c> or <c>responses</c>, from the plan's model
    /// listing. Null when the listing could not be read, so the caller asks again next time. A model the listing
    /// does not name is chat; the vendor's own error then explains it.
    /// </summary>
    internal static async Task<string?> ProtocolForAsync(string modelId, HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(ApiUrl + "/models"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))?["data"] is not JsonArray data)
            {
                return null;
            }

            var model = data.OfType<JsonObject>().FirstOrDefault(entry => TokenEndpoint.Text(entry, "id") == modelId);
            return model is null ? "chat" : Protocol(model) ?? "chat";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>The Copilot API base for github.com or an enterprise host.</summary>
    public static string BaseUrl(string? enterpriseUrl)
    {
        return string.IsNullOrEmpty(enterpriseUrl) ? ApiUrl : "https://copilot-api." + EnterpriseHost(enterpriseUrl);
    }

    /// <summary>
    /// The bare host of a GitHub Enterprise URL. The bearer token goes to a host derived from it, so anything but a
    /// plain hostname is refused rather than guessed at.
    /// </summary>
    public static string EnterpriseHost(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var refusal = new OAuthException(OAuthException.Misconfigured, value + " is not a GitHub Enterprise host; pass the hostname, e.g. github.example.com");
        if (!Uri.TryCreate(value.Contains("://", StringComparison.Ordinal) ? value : "https://" + value, UriKind.Absolute, out var url))
        {
            throw refusal;
        }

        var plain = url.Scheme == "https"
            && url.UserInfo.Length == 0
            && url.IsDefaultPort
            && url.AbsolutePath is "/" or ""
            && url.Query.Length == 0
            && url.Fragment.Length == 0
            && HostPattern().IsMatch(url.Host);
        return plain ? url.Host.ToLowerInvariant() : throw refusal;
    }

    /// <summary>A chat completions body carries turns in <c>messages</c>; a Responses body in <c>input</c>.</summary>
    private static (string Initiator, bool Vision) Turn(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ("user", false);
        }

        try
        {
            var body = JsonNode.Parse(text);
            var responses = body?["messages"] is not JsonArray && body?["input"] is JsonArray;
            var turns = (responses ? body?["input"] : body?["messages"]) as JsonArray ?? [];
            var part = responses ? "input_image" : "image_url";
            var last = turns.Count == 0 ? null : turns[^1]?["role"]?.ToString();
            var vision = turns.Any(turn => turn?["content"] is JsonArray content && content.Any(item => item?["type"]?.ToString() == part));
            return (last == "user" ? "user" : "agent", vision);
        }
        catch (JsonException)
        {
            return ("user", false);
        }
    }

    /// <summary>An entry that names no endpoints is chat; chat wins when both are named.</summary>
    private static string? Protocol(JsonObject model)
    {
        if (model["supported_endpoints"] is not JsonArray endpoints)
        {
            return "chat";
        }

        var names = endpoints.Select(endpoint => endpoint?.ToString()).ToList();
        if (names.Contains("/chat/completions"))
        {
            return "chat";
        }

        return names.Contains("/responses") ? "responses" : null;
    }

    private static bool IsTrue(JsonNode? node)
    {
        return node is JsonValue value && value.GetValueKind() == JsonValueKind.True;
    }

    private static async Task<string?> GitHubCliTokenAsync(string? hostname, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("gh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("auth");
            start.ArgumentList.Add("token");
            if (hostname is not null)
            {
                start.ArgumentList.Add("--hostname");
                start.ArgumentList.Add(hostname);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && output.Trim().Length > 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$", RegexOptions.IgnoreCase)]
    private static partial Regex HostPattern();
}
