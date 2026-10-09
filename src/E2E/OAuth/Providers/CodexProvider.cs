// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E.OAuth;

/// <summary>
/// ChatGPT Plus/Pro through the Codex OAuth client: PKCE authorization code against <c>auth.openai.com</c> with a
/// local callback on port 1455, or OpenAI's user-code flow for a machine without a browser. Requests go to the
/// Codex backend, which speaks the Responses API, requires <c>store: false</c>, and answers only as a stream.
/// </summary>
public sealed class CodexProvider : IOAuthProvider
{
    /// <summary>The public OAuth client of the Codex CLI, which every third-party harness signs in through.</summary>
    private const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    private const string CallbackPath = "/auth/callback";
    private const string Scope = "openid profile email offline_access";
    private const string AuthClaim = "https://api.openai.com/auth";

    /// <summary>The backend rejects a request without instructions.</summary>
    private const string RequiredInstructions = "Follow the user request.";

    /// <summary>The model list is served per Codex CLI version; the backend hides models a client is too old for.</summary>
    private const string CodexClientVersion = "0.160.0";

    public const string DefaultApiUrl = "https://chatgpt.com/backend-api/codex/responses";

    private readonly HttpClient _http;
    private readonly string _issuer;
    private readonly string _apiUrl;
    private readonly string _originator;
    private readonly int _callbackPort;
    private readonly TimeSpan _loginTimeout;

    /// <param name="http">Sends the login's own requests. Unset, a shared client.</param>
    /// <param name="originator">The <c>originator</c> the authorization page and requests carry: your product's name.</param>
    /// <param name="issuer">Test seam.</param>
    /// <param name="apiUrl">Test seam.</param>
    /// <param name="callbackPort">Test seam.</param>
    /// <param name="loginTimeout">Test seam.</param>
    public CodexProvider(
        HttpClient? http = null,
        string originator = "e2e",
        string issuer = "https://auth.openai.com",
        string apiUrl = DefaultApiUrl,
        int callbackPort = 1455,
        TimeSpan? loginTimeout = null)
    {
        _http = http ?? OAuthHttp.Shared;
        _originator = originator;
        _issuer = issuer;
        _apiUrl = apiUrl;
        _callbackPort = callbackPort;
        _loginTimeout = loginTimeout ?? TimeSpan.FromMinutes(10);
    }

    public string Id => "openai";

    public string Name => "ChatGPT";

    public async Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        ArgumentNullException.ThrowIfNull(options);
        var tokens = options.Device
            ? await DeviceLoginAsync(callbacks, cancellationToken).ConfigureAwait(false)
            : await BrowserLoginAsync(callbacks, cancellationToken).ConfigureAwait(false);
        return ToCredentials(tokens, null);
    }

    public async Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var tokens = await TokenEndpoint.RequestAsync(
            _http,
            Name,
            _issuer + "/oauth/token",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["grant_type"] = "refresh_token", ["refresh_token"] = credentials.Refresh, ["client_id"] = ClientId },
            OAuthException.LoginRequired,
            cancellationToken).ConfigureAwait(false);
        return ToCredentials(tokens, credentials);
    }

    /// <summary>
    /// Routes a Responses request at the Codex backend with the body the Codex CLI sends: no server-side storage,
    /// encrypted reasoning carried between turns, always streamed. The model folds the stream back.
    /// </summary>
    public async Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        request.Headers.TryAddWithoutValidation("originator", _originator);
        if (credentials.Get("accountId") is { } account)
        {
            request.Headers.TryAddWithoutValidation("chatgpt-account-id", account);
        }

        if (request.RequestUri is null || !request.RequestUri.AbsolutePath.EndsWith("/responses", StringComparison.Ordinal) || request.Content is null)
        {
            return;
        }

        if (credentials.Get("residency") is { } residency)
        {
            request.Headers.TryAddWithoutValidation("x-openai-internal-codex-residency", residency);
        }

        JsonObject body;
        try
        {
            body = JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject
                ?? throw new OAuthException(OAuthException.FlowFailed, "the Responses request body is not a JSON object");
        }
        catch (JsonException ex)
        {
            throw new OAuthException(OAuthException.FlowFailed, "the Responses request body is not JSON", ex);
        }

        body["stream"] = true;
        body["store"] = false;
        if (body["instructions"] is not JsonValue instructions || string.IsNullOrEmpty(instructions.ToString()))
        {
            body["instructions"] = RequiredInstructions;
        }

        var include = body["include"] as JsonArray ?? [];
        if (!include.Any(item => item?.ToString() == "reasoning.encrypted_content"))
        {
            include.Add("reasoning.encrypted_content");
        }

        body["include"] = include.DeepClone();

        // The Codex backend sizes output itself and rejects some caps.
        body.Remove("max_output_tokens");
        request.RequestUri = new Uri(_apiUrl);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }

    /// <summary>The models the Codex backend serves this subscription, <c>/models</c> next to <c>/responses</c>. Hidden ones are listed, marked.</summary>
    public async Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!_apiUrl.EndsWith("/responses", StringComparison.Ordinal))
        {
            throw new OAuthException(OAuthException.Misconfigured, "the Codex API URL must end in /responses to list models next to it; got " + _apiUrl);
        }

        var url = _apiUrl[..^"/responses".Length] + "/models?client_version=" + CodexClientVersion;
        var payload = await OAuthHttp.GetJsonAsync(http, url, Name, cancellationToken).ConfigureAwait(false);
        var models = payload["models"] as JsonArray ?? [];
        return models.OfType<JsonObject>()
            .Where(model => !string.IsNullOrEmpty(TokenEndpoint.Text(model, "slug")))
            .OrderBy(model => TokenEndpoint.Number(model, "priority") ?? 0)
            .Select(model =>
            {
                var levels = (model["supported_reasoning_levels"] as JsonArray ?? [])
                    .Select(level => level is JsonObject entry ? TokenEndpoint.Text(entry, "effort") : level?.ToString())
                    .Where(level => !string.IsNullOrEmpty(level))
                    .ToList();
                var detail = new List<string>();
                if (levels.Count > 0)
                {
                    var fallback = TokenEndpoint.Text(model, "default_reasoning_level");
                    detail.Add("reasoning " + string.Join('/', levels) + (fallback is null ? "" : " (default " + fallback + ")"));
                }

                if (TokenEndpoint.Text(model, "visibility") == "hide")
                {
                    detail.Add("hidden in Codex");
                }

                return new SubscriptionModel(
                    TokenEndpoint.Text(model, "slug")!,
                    TokenEndpoint.Text(model, "display_name") is { Length: > 0 } name ? name : null,
                    detail.Count == 0 ? null : string.Join("; ", detail));
            })
            .ToList();
    }

    /// <summary>The ChatGPT account the requests bill to, from the id token or the access token.</summary>
    internal static string? AccountId(string? idToken, string? accessToken)
    {
        foreach (var claims in new[] { Jwt.Claims(idToken), Jwt.Claims(accessToken) })
        {
            if (claims is null)
            {
                continue;
            }

            var candidate = TokenEndpoint.Text(claims, "chatgpt_account_id")
                ?? (claims[AuthClaim] is JsonObject nested ? TokenEndpoint.Text(nested, "chatgpt_account_id") : null)
                ?? (claims["organizations"] is JsonArray { Count: > 0 } organizations && organizations[0] is JsonObject first ? TokenEndpoint.Text(first, "id") : null);
            if (!string.IsNullOrEmpty(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The code from what the user pasted: a full redirect URL, <c>code#state</c>, a query string, or the bare code.</summary>
    internal static (string? Code, string? State) ParseAuthorizationInput(string input)
    {
        var value = input.Trim();
        if (value.Length == 0)
        {
            return (null, null);
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
        {
            var query = System.Web.HttpUtility.ParseQueryString(url.Query);
            return (query["code"], query["state"]);
        }

        if (value.Contains('#', StringComparison.Ordinal))
        {
            var parts = value.Split('#', 2);
            return (parts[0], parts[1]);
        }

        if (value.Contains("code=", StringComparison.Ordinal))
        {
            var query = System.Web.HttpUtility.ParseQueryString(value);
            return (query["code"], query["state"]);
        }

        return (value, null);
    }

    private static OAuthCredentials ToCredentials(TokenResponse tokens, OAuthCredentials? previous)
    {
        var account = AccountId(tokens.IdToken, tokens.AccessToken) ?? previous?.Get("accountId");
        var claims = Jwt.Claims(tokens.AccessToken);
        var residency = (claims?[AuthClaim] is JsonObject nested ? TokenEndpoint.Text(nested, "chatgpt_compute_residency") : null)
            ?? (claims is null ? null : TokenEndpoint.Text(claims, "chatgpt_compute_residency"));
        if (string.IsNullOrEmpty(residency) || residency == "no_constraint")
        {
            residency = previous?.Get("residency");
        }

        var refresh = tokens.RefreshToken ?? previous?.Refresh
            ?? throw new OAuthException(OAuthException.FlowFailed, "the ChatGPT token response is missing refresh_token");
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        if (account is not null)
        {
            extra["accountId"] = account;
        }

        if (residency is not null)
        {
            extra["residency"] = residency;
        }

        return new OAuthCredentials { Access = tokens.AccessToken, Refresh = refresh, Expires = TokenEndpoint.ExpiryFrom(tokens.ExpiresIn), Extra = extra };
    }

    private async Task<TokenResponse> ExchangeAsync(string code, string redirectUri, string verifier, CancellationToken cancellationToken)
    {
        try
        {
            return await TokenEndpoint.RequestAsync(
                _http,
                Name,
                _issuer + "/oauth/token",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["client_id"] = ClientId,
                    ["code_verifier"] = verifier,
                },
                OAuthException.FlowFailed,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OAuthException(OAuthException.Cancelled, "the login was cancelled", ex);
        }
    }

    private async Task<TokenResponse> BrowserLoginAsync(OAuthLoginCallbacks callbacks, CancellationToken cancellationToken)
    {
        var (verifier, challenge) = Pkce.Generate();
        var state = Pkce.State();
        using var server = CallbackServer.TryStart(_callbackPort, CallbackPath, state, "ChatGPT login");
        var redirectUri = server?.RedirectUri ?? "http://localhost:" + _callbackPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + CallbackPath;
        var query = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["id_token_add_organizations"] = "true",
            ["codex_cli_simplified_flow"] = "true",
            ["state"] = state,
            ["originator"] = _originator,
        };
        var url = _issuer + "/oauth/authorize?" + string.Join('&', query.Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value)));
        callbacks.OnAuth(new OAuthAuthInfo(
            url,
            server is null
                ? "Port " + _callbackPort.ToString(System.Globalization.CultureInfo.InvariantCulture) + " is in use, so the browser cannot return here: sign in, then paste the URL the browser lands on."
                : "Sign in in the browser; this terminal continues when the browser returns, or asks for the URL it landed on."));

        string? code;
        if (server is null)
        {
            code = await PasteCodeAsync(callbacks, state, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            try
            {
                var answer = await server.WaitAsync(_loginTimeout, cancellationToken).ConfigureAwait(false);
                if (answer.Error is not null)
                {
                    throw answer.Error == "access_denied"
                        ? new OAuthException(OAuthException.Cancelled, "the sign-in was declined in the browser")
                        : new OAuthException(OAuthException.FlowFailed, "ChatGPT refused the sign-in: " + answer.Error);
                }

                code = answer.Code;
            }
            catch (OAuthException ex) when (ex.Code == OAuthException.Timeout)
            {
                code = await PasteCodeAsync(callbacks, state, cancellationToken).ConfigureAwait(false);
            }
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new OAuthException(OAuthException.FlowFailed, "no authorization code was received");
        }

        callbacks.OnProgress?.Invoke("Exchanging the code for tokens");
        return await ExchangeAsync(code, redirectUri, verifier, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> PasteCodeAsync(OAuthLoginCallbacks callbacks, string state, CancellationToken cancellationToken)
    {
        var pasted = ParseAuthorizationInput(await callbacks.OnPrompt("Paste the URL the browser landed on (or the code it shows)", cancellationToken).ConfigureAwait(false));
        if (pasted.State is not null && pasted.State != state)
        {
            throw new OAuthException(OAuthException.FlowFailed, "the pasted code belongs to a different login attempt; start over");
        }

        return pasted.Code;
    }

    /// <summary>The Codex CLI's device login: OpenAI's own user-code endpoints, pending until the user enters the code.</summary>
    private Task<TokenResponse> DeviceLoginAsync(OAuthLoginCallbacks callbacks, CancellationToken cancellationToken)
    {
        return DeviceFlow.RunAsync<TokenResponse>(
            async token =>
            {
                using var response = await PostJsonAsync(_issuer + "/api/accounts/deviceauth/usercode", new JsonObject { ["client_id"] = ClientId }, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new OAuthException(OAuthException.FlowFailed, "ChatGPT device login could not start: " + await TokenEndpoint.DescribeAsync(response, token).ConfigureAwait(false));
                }

                var device = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject ?? [];
                return new DeviceAuthorization(
                    TokenEndpoint.Text(device, "device_auth_id") ?? throw new OAuthException(OAuthException.FlowFailed, "the ChatGPT device response is missing device_auth_id"),
                    TokenEndpoint.Text(device, "user_code") ?? throw new OAuthException(OAuthException.FlowFailed, "the ChatGPT device response is missing user_code"),
                    _issuer + "/codex/device",
                    null,
                    _loginTimeout.TotalSeconds,
                    (TokenEndpoint.Number(device, "interval") ?? 5) + 3);
            },
            async (authorization, token) =>
            {
                using var response = await PostJsonAsync(
                    _issuer + "/api/accounts/deviceauth/token",
                    new JsonObject { ["device_auth_id"] = authorization.DeviceCode, ["user_code"] = authorization.UserCode },
                    token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var grant = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject ?? [];
                    var tokens = await ExchangeAsync(
                        TokenEndpoint.Text(grant, "authorization_code") ?? "",
                        _issuer + "/deviceauth/callback",
                        TokenEndpoint.Text(grant, "code_verifier") ?? "",
                        token).ConfigureAwait(false);
                    return new DevicePoll<TokenResponse>(DevicePollStatus.Granted, tokens);
                }

                // Pending shows as 403 or 404 until the user enters the code.
                if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                {
                    return new DevicePoll<TokenResponse>(DevicePollStatus.Pending);
                }

                throw new OAuthException(OAuthException.FlowFailed, "ChatGPT device login failed: " + await TokenEndpoint.DescribeAsync(response, token).ConfigureAwait(false));
            },
            callbacks,
            null,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, JsonObject body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = ModelHttp.Json(body) };
        request.Headers.TryAddWithoutValidation("User-Agent", ModelHttp.UserAgent);
        return await OAuthHttp.SendAsync(_http, request, Name, cancellationToken).ConfigureAwait(false);
    }
}
