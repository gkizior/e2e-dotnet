// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.OAuth;

/// <summary>
/// SuperGrok and X Premium+ through SpaceXAI's OAuth server: RFC 8628 device authorization for the Grok CLI's
/// public client. The token is a plain bearer against the ordinary SpaceXAI API. Refresh tokens rotate.
/// </summary>
public sealed class XaiProvider : IOAuthProvider
{
    /// <summary>The public OAuth client of the Grok CLI, which third-party harnesses sign in through.</summary>
    private const string ClientId = "b1a00492-073a-47ea-816f-4c329264a828";
    private const string Scope = "openid profile email offline_access grok-cli:access api:access";

    public const string ApiUrl = "https://api.x.ai/v1";

    private readonly HttpClient _http;
    private readonly string _issuer;
    private readonly string _referrer;
    private readonly string _modelsUrl;

    /// <param name="http">Sends the login's own requests. Unset, a shared client.</param>
    /// <param name="referrer">The <c>referrer</c> the device request names: your product.</param>
    /// <param name="issuer">Test seam.</param>
    /// <param name="modelsUrl">Test seam.</param>
    public XaiProvider(HttpClient? http = null, string referrer = "e2e", string issuer = "https://auth.x.ai", string modelsUrl = ApiUrl + "/language-models")
    {
        _http = http ?? OAuthHttp.Shared;
        _referrer = referrer;
        _issuer = issuer;
        _modelsUrl = modelsUrl;
    }

    public string Id => "spacexai";

    public string Name => "SpaceXAI";

    public async Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callbacks);
        var tokens = await DeviceFlow.Rfc8628Async(
            _http,
            Name,
            _issuer + "/oauth2/device/code",
            _issuer + "/oauth2/token",
            ClientId,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["scope"] = Scope, ["referrer"] = _referrer },
            callbacks,
            cancellationToken).ConfigureAwait(false);
        return ToCredentials(tokens, "");
    }

    public async Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var tokens = await TokenEndpoint.RequestAsync(
            _http,
            Name,
            _issuer + "/oauth2/token",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["grant_type"] = "refresh_token", ["refresh_token"] = credentials.Refresh, ["client_id"] = ClientId },
            OAuthException.LoginRequired,
            cancellationToken).ConfigureAwait(false);
        return ToCredentials(tokens, credentials.Refresh);
    }

    public Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>The language models the token reaches; aliases are listed with their target.</summary>
    public async Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        var payload = await OAuthHttp.GetJsonAsync(http, _modelsUrl, Name, cancellationToken).ConfigureAwait(false);
        var models = payload["models"] as JsonArray ?? payload["data"] as JsonArray ?? [];
        return models.OfType<JsonObject>()
            .Where(model => !string.IsNullOrEmpty(TokenEndpoint.Text(model, "id")))
            .Select(model =>
            {
                var aliases = (model["aliases"] as JsonArray ?? []).Select(alias => alias?.ToString()).OfType<string>().ToList();
                var vision = (model["input_modalities"] as JsonArray ?? []).Any(kind => kind?.ToString() == "image");
                var detail = new List<string>();
                if (vision)
                {
                    detail.Add("vision");
                }

                if (aliases.Count > 0)
                {
                    detail.Add("also " + string.Join(", ", aliases));
                }

                return new SubscriptionModel(TokenEndpoint.Text(model, "id")!, null, detail.Count == 0 ? null : string.Join("; ", detail));
            })
            .ToList();
    }

    /// <summary>Expiry from the JWT when it carries one, else from <c>expires_in</c>; a refresh without a new refresh token keeps the old one.</summary>
    private static OAuthCredentials ToCredentials(TokenResponse tokens, string previousRefresh)
    {
        var exp = Jwt.Claims(tokens.AccessToken) is { } claims ? TokenEndpoint.Number(claims, "exp") : null;
        var expires = exp is > 0 ? (long)(exp.Value * 1000) : TokenEndpoint.ExpiryFrom(tokens.ExpiresIn);
        return new OAuthCredentials { Access = tokens.AccessToken, Refresh = tokens.RefreshToken ?? previousRefresh, Expires = expires };
    }
}
