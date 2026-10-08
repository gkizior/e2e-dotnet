// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E.OAuth;

/// <summary>
/// Subscription logins as agent models, upstream's <c>chatgpt()</c>, <c>copilot()</c>, <c>grok()</c>, and
/// <c>opencodeConsole()</c>. Each reads the login <c>e2e login</c> stored (or <c>E2E_OAUTH_CREDENTIALS</c>), refreshes
/// it as needed, and touches nothing until the first call. Use API keys in CI.
/// </summary>
public static class Subscriptions
{
    /// <summary>A ChatGPT Plus/Pro subscription over the Codex backend. <c>e2e models openai</c> lists the ids the plan serves.</summary>
    public static OpenAiResponsesModel ChatGpt(string model, ICredentialStore? store = null)
    {
        var provider = new CodexProvider();
        return new OpenAiResponsesModel(
            new OpenAiResponsesModelOptions
            {
                Model = model,
                BaseUrl = CodexProvider.DefaultApiUrl[..^"/responses".Length],
                ApiKey = "oauth",
                PromptCacheHints = false,
            },
            Client(provider, store));
    }

    /// <summary>
    /// A GitHub Copilot subscription. Copilot serves most models over chat completions and some only over its
    /// Responses API; the first call reads the plan's model listing to pick. An enterprise login routes to its host.
    /// </summary>
    public static CopilotModel Copilot(string model, ICredentialStore? store = null)
    {
        return new CopilotModel(model, Client(new CopilotProvider(), store));
    }

    /// <summary>A SuperGrok or X Premium+ subscription over the ordinary SpaceXAI API.</summary>
    public static OpenAiCompatibleModel Grok(string model, ICredentialStore? store = null)
    {
        return new OpenAiCompatibleModel(
            new OpenAiCompatibleModelOptions { Model = model, BaseUrl = XaiProvider.ApiUrl, Provider = "xai", ApiKey = "oauth" },
            Client(new XaiProvider(), store));
    }

    /// <summary>
    /// An OpenCode Console workspace: OpenCode Zen for a bare id, OpenCode Go for a <c>go/</c> id. The first call
    /// reads the workspace config for the model's protocol. <c>OPENCODE_API_KEY</c> (a Console service account key)
    /// replaces the stored login when set.
    /// </summary>
    public static OpenCodeConsoleModel OpenCodeConsole(string model, ICredentialStore? store = null)
    {
        return OpenCodeConsole(model, store, new OpenCodeConsoleProvider(), inner: null);
    }

    internal static OpenCodeConsoleModel OpenCodeConsole(string model, ICredentialStore? store, OpenCodeConsoleProvider provider, HttpMessageHandler? inner)
    {
        var apiKey = ModelHttp.Environment("OPENCODE_API_KEY");
        string? hint = null;
        if (apiKey is not null)
        {
            store = new EnvironmentCredentialStore(new JsonObject
            {
                [provider.Id] = new OAuthCredentials { Access = apiKey, Refresh = "", Expires = 0 }.ToJson(),
            }.ToJsonString());
            hint = "check OPENCODE_API_KEY";
        }

        return new OpenCodeConsoleModel(model, provider, Client(provider, store, hint, inner));
    }

    /// <summary>An <see cref="HttpClient"/> that carries <paramref name="provider"/>'s stored login on every request.</summary>
    public static HttpClient Client(IOAuthProvider provider, ICredentialStore? store = null, string? loginHint = null, HttpMessageHandler? inner = null)
    {
        return new HttpClient(new OAuthHandler(provider, store ?? CredentialStores.Default(), loginHint, inner)) { Timeout = ModelHttp.Timeout };
    }
}

/// <summary>The built-in subscription providers by id, and what <c>e2e login</c>, <c>logout</c>, and <c>models</c> do with them.</summary>
public static class OAuthProviders
{
    /// <summary>Upstream's provider ids, in its order.</summary>
    public static IReadOnlyList<string> Ids { get; } = ["openai", "github-copilot", "opencode-console", "spacexai"];

    /// <summary>The provider for <paramref name="id"/>; <see cref="OAuthException.Misconfigured"/> for an unknown id.</summary>
    public static IOAuthProvider Get(string id)
    {
        return id switch
        {
            "openai" => new CodexProvider(),
            "github-copilot" => new CopilotProvider(),
            "opencode-console" => new OpenCodeConsoleProvider(),
            "spacexai" => new XaiProvider(),
            _ => throw new OAuthException(OAuthException.Misconfigured, "unknown provider \"" + id + "\"; expected one of " + string.Join(", ", Ids)),
        };
    }

    /// <summary>A short description of each subscription, for pickers and help.</summary>
    public static string Describe(string id)
    {
        return id switch
        {
            "openai" => "ChatGPT Plus/Pro, the Codex sign-in",
            "github-copilot" => "GitHub Copilot: OpenAI, Anthropic, Google, and SpaceXAI models",
            "opencode-console" => "your OpenCode Console workspace (OpenCode Zen and Go)",
            "spacexai" => "SuperGrok or X Premium+",
            _ => id,
        };
    }

    /// <summary>Signs in to <paramref name="providerId"/> and stores the credentials.</summary>
    public static async Task<OAuthCredentials> LoginAsync(
        string providerId,
        OAuthLoginCallbacks callbacks,
        OAuthLoginOptions? options = null,
        ICredentialStore? store = null,
        CancellationToken cancellationToken = default)
    {
        return await LoginAsync(Get(providerId), callbacks, options, store, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<OAuthCredentials> LoginAsync(
        IOAuthProvider provider,
        OAuthLoginCallbacks callbacks,
        OAuthLoginOptions? options,
        ICredentialStore? store,
        CancellationToken cancellationToken)
    {
        store ??= CredentialStores.Default();
        if (store is EnvironmentCredentialStore environment)
        {
            environment.AssertWritable();
        }

        var credentials = await provider.LoginAsync(callbacks, options ?? new OAuthLoginOptions(), cancellationToken).ConfigureAwait(false);
        await store.SetAsync(provider.Id, credentials, cancellationToken).ConfigureAwait(false);
        return credentials;
    }

    /// <summary>Forgets the stored login. True when one was stored.</summary>
    public static Task<bool> LogoutAsync(string providerId, ICredentialStore? store = null, CancellationToken cancellationToken = default)
    {
        var provider = Get(providerId);
        return (store ?? CredentialStores.Default()).RemoveAsync(provider.Id, cancellationToken);
    }

    /// <summary>The models <paramref name="providerId"/>'s stored login serves; <see cref="OAuthException.NotLoggedIn"/> without one.</summary>
    public static async Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(string providerId, ICredentialStore? store = null, CancellationToken cancellationToken = default)
    {
        var provider = Get(providerId);
        using var http = Subscriptions.Client(provider, store);
        return await provider.ListModelsAsync(http, cancellationToken).ConfigureAwait(false);
    }
}
