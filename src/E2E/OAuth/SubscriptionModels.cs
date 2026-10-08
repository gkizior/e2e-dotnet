// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.OAuth;

/// <summary>
/// A model that settles its protocol on the first call and delegates to the client for it. Only a lookup that
/// succeeded is remembered: when it could not be read, that call goes over the fallback and the next call asks again.
/// </summary>
public abstract class DelegatingModel : IAgentModel, IDisposable
{
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAgentModel? _resolved;

    protected DelegatingModel(string name, HttpClient http)
    {
        Name = name;
        Http = http;
    }

    public string Name { get; }

    protected HttpClient Http { get; }

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var model = _resolved;
        if (model is null)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                model = _resolved;
                if (model is null)
                {
                    using var lookup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    lookup.CancelAfter(LookupTimeout);
                    try
                    {
                        _resolved = await ResolveAsync(lookup.Token).ConfigureAwait(false);
                    }
                    catch (OAuthException ex)
                    {
                        throw new AgentException("MODEL_PROVIDER_FAILED", "The subscription login failed (" + ex.Code + "): " + ex.Message, blocked: true, ex);
                    }

                    model = _resolved ?? Fallback;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        return await model.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Http.Dispose();
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>The model to use until a lookup succeeds.</summary>
    protected abstract IAgentModel Fallback { get; }

    /// <summary>The model for the looked-up protocol, or null when the lookup could not be read.</summary>
    protected abstract Task<IAgentModel?> ResolveAsync(CancellationToken cancellationToken);
}

/// <summary>A GitHub Copilot subscription model: chat completions, or the Responses API when the plan serves the model only there.</summary>
public sealed class CopilotModel : DelegatingModel
{
    private readonly string _model;
    private readonly OpenAiCompatibleModel _chat;

    public CopilotModel(string model, HttpClient http)
        : base(model, http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _model = model;
        _chat = new OpenAiCompatibleModel(
            new OpenAiCompatibleModelOptions { Model = model, BaseUrl = CopilotProvider.ApiUrl, Provider = "github-copilot", ApiKey = "oauth" },
            http);
    }

    protected override IAgentModel Fallback => _chat;

    protected override async Task<IAgentModel?> ResolveAsync(CancellationToken cancellationToken)
    {
        return await CopilotProvider.ProtocolForAsync(_model, Http, cancellationToken).ConfigureAwait(false) switch
        {
            null => null,
            "responses" => new OpenAiResponsesModel(
                new OpenAiResponsesModelOptions { Model = _model, BaseUrl = CopilotProvider.ApiUrl, Provider = "github-copilot", ApiKey = "oauth", PromptCacheHints = false },
                Http),
            _ => _chat,
        };
    }
}

/// <summary>
/// An OpenCode Console model. The first call reads the workspace config and talks to the model over its own
/// protocol: chat completions, OpenAI Responses, Anthropic Messages, or Gemini. Each instance names one session,
/// so its calls stay on one upstream and its cache.
/// </summary>
public sealed class OpenCodeConsoleModel : DelegatingModel
{
    private readonly string _model;
    private readonly OpenCodeConsoleProvider _provider;
    private readonly string _session;
    private readonly Dictionary<string, string> _headers;
    private readonly IAgentModel _chat;

    internal OpenCodeConsoleModel(string model, OpenCodeConsoleProvider provider, HttpClient http)
        : base(model, http)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        _model = model;
        _provider = provider;
        _session = "e2e_" + Guid.NewGuid().ToString("N");
        _headers = new Dictionary<string, string>(StringComparer.Ordinal) { ["x-opencode-session"] = _session };
        _chat = Create(OpenCodeConsoleProvider.ChatRoute(model));
    }

    protected override IAgentModel Fallback => _chat;

    protected override async Task<IAgentModel?> ResolveAsync(CancellationToken cancellationToken)
    {
        var route = await _provider.RouteForAsync(_model, Http, cancellationToken).ConfigureAwait(false);
        return route is null ? null : Create(route);
    }

    private IAgentModel Create(OpenCodeRoute route)
    {
        // Go and Zen both read their provider options under "opencode", as upstream does.
        return route.Protocol switch
        {
            "openai" => new OpenAiResponsesModel(
                new OpenAiResponsesModelOptions { Model = route.ModelId, BaseUrl = route.BaseUrl, Provider = "openai", ApiKey = "oauth", Headers = _headers, SessionId = _session },
                Http),
            "anthropic" => new AnthropicModel(
                new AnthropicModelOptions { Model = route.ModelId, BaseUrl = route.BaseUrl, ApiKey = "oauth", Headers = _headers },
                Http),
            "google" => new GoogleModel(
                new GoogleModelOptions { Model = route.ModelId, BaseUrl = route.BaseUrl, ApiKey = "oauth", Headers = _headers },
                Http),
            _ => new OpenAiCompatibleModel(
                new OpenAiCompatibleModelOptions { Model = route.ModelId, BaseUrl = route.BaseUrl, Provider = "opencode", ApiKey = "oauth", Headers = _headers },
                Http),
        };
    }
}
