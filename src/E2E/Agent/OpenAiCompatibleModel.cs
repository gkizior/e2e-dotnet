// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Chat-completions client for OpenAI and any server that speaks the same tool-call JSON.
/// Set <see cref="OpenAiCompatibleModelOptions.BaseUrl"/> to a local server such as
/// <c>http://127.0.0.1:11434/v1</c>. The API key is optional for servers that do not check one.
/// <see cref="ModelProviders"/> has presets for OpenRouter, the Vercel AI Gateway, SpaceXAI, and Ollama.
/// </summary>
public sealed class OpenAiCompatibleModel : IAgentModel, IDisposable
{
    private readonly OpenAiCompatibleModelOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public OpenAiCompatibleModel(OpenAiCompatibleModelOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        _options = options;
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = ModelHttp.Timeout };
            _ownsClient = true;
        }
        else
        {
            _http = httpClient;
            _ownsClient = false;
        }
    }

    public string Name => _options.Model;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, ModelEndpoint.Build(_options.BaseUrl, "/chat/completions", _options.QueryParameters));
        ModelEndpoint.Authorize(message, _options.ResolveApiKey(), _options.ApiKeyHeader);
        ModelEndpoint.AddHeaders(message, _options.Headers);
        message.Content = ModelHttp.Json(BuildBody(request));
        var body = await ModelHttp.SendAsync(_http, message, request.Redactor, cancellationToken).ConfigureAwait(false);
        return ModelHttp.Parse(body, Parse);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    internal JsonObject BuildBody(ModelRequest request)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.System },
        };
        foreach (var message in request.Messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                var calls = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    calls.Add(new JsonObject
                    {
                        ["id"] = call.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["arguments"] = call.Arguments.GetRawText(),
                        },
                    });
                }

                messages.Add(new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = message.Content ?? "",
                    ["tool_calls"] = calls,
                });
            }
            else if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = message.ToolCallId,
                    ["content"] = message.Content ?? "",
                });
            }
            else
            {
                messages.Add(new JsonObject { ["role"] = message.Role, ["content"] = message.Content ?? "" });
            }
        }

        var tools = new JsonArray();
        foreach (var tool in request.Tools)
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
                },
            });
        }

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["messages"] = messages,
            ["tools"] = tools,
            ["tool_choice"] = "auto",
        };

        // OpenAI caches prompt prefixes on its own and takes a routing key, one per system prompt. Nothing is stored.
        // Upstream sends this to Responses models and to openai/* models on the gateway, not to chat completions.
        if (_options.PromptCacheHints ?? IsGatewayOpenAi())
        {
            payload["prompt_cache_key"] = ModelHttp.PromptCacheKey(request.System);
            payload["store"] = false;
        }

        // Provider options ride the request body as given, so a field such as reasoning_effort reaches the server.
        ModelHttp.MergeProviderOptions(payload, request, _options.Provider, "model", "messages", "tools");

        // The AI SDK spells the effort reasoningEffort and sends it as reasoning_effort.
        if (payload.Remove("reasoningEffort", out var effort))
        {
            payload["reasoning_effort"] = effort;
        }

        return payload;
    }

    private bool IsGatewayOpenAi()
    {
        return string.Equals(_options.Provider, "gateway", StringComparison.Ordinal) && _options.Model.StartsWith("openai/", StringComparison.Ordinal);
    }

    private static ModelResponse Parse(JsonElement root)
    {
        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        string? content = null;
        if (message.TryGetProperty("content", out var contentElement) && contentElement.ValueKind == JsonValueKind.String)
        {
            content = contentElement.GetString();
        }

        var calls = new List<ModelToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                var arguments = function.GetProperty("arguments");
                calls.Add(new ModelToolCall
                {
                    Id = call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() ?? "call" : "call_" + calls.Count,
                    Name = function.GetProperty("name").GetString() ?? "",
                    Arguments = arguments.ValueKind == JsonValueKind.Object ? arguments.Clone() : ModelHttp.Arguments(arguments.GetString()),
                });
            }
        }

        ModelUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ModelUsage
            {
                InputTokens = ModelHttp.Int(usageElement, "prompt_tokens"),
                OutputTokens = ModelHttp.Int(usageElement, "completion_tokens"),
            };
        }

        return new ModelResponse { Content = content, ToolCalls = calls, Usage = usage };
    }
}

public sealed class OpenAiCompatibleModelOptions
{
    public required string Model { get; init; }

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    /// <summary>
    /// The key this client reads in <see cref="ModelRequest.ProviderOptions"/>. Its fields are added
    /// to the chat-completions body as given. Defaults to <c>openai</c>.
    /// </summary>
    public string Provider { get; init; } = "openai";

    public string? ApiKey { get; init; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty. Defaults to <c>OPENAI_API_KEY</c>.</summary>
    public string ApiKeyEnv { get; init; } = "OPENAI_API_KEY";

    /// <summary>A second environment variable read when the first is unset, such as the Vercel OIDC token.</summary>
    public string? ApiKeyFallbackEnv { get; init; }

    /// <summary>
    /// The header that carries the key. Null (the default) sends <c>Authorization: Bearer</c>; Azure OpenAI
    /// takes <c>api-key</c>.
    /// </summary>
    public string? ApiKeyHeader { get; init; }

    /// <summary>Headers added to every request. The identity headers (<c>User-Agent</c>, <c>HTTP-Referer</c>, <c>X-Title</c>) replace the same names set here.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Query parameters added to every request URL, such as Azure's <c>api-version</c>.</summary>
    public IReadOnlyDictionary<string, string>? QueryParameters { get; init; }

    /// <summary>
    /// Whether requests carry a prompt-cache key and <c>store: false</c>. Null (the default) sends them only to
    /// <c>openai/*</c> models on the gateway, as upstream does; chat completions elsewhere get neither. Provider options win over both.
    /// </summary>
    public bool? PromptCacheHints { get; init; }

    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        return ModelHttp.Environment(ApiKeyEnv) ?? ModelHttp.Environment(ApiKeyFallbackEnv);
    }
}

/// <summary>URL and header plumbing the HTTP model clients share.</summary>
internal static class ModelEndpoint
{
    public static Uri Build(string baseUrl, string path, IReadOnlyDictionary<string, string>? query)
    {
        var url = baseUrl.TrimEnd('/') + path;
        if (query is { Count: > 0 })
        {
            url += (url.Contains('?', StringComparison.Ordinal) ? "&" : "?")
                + string.Join('&', query.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
        }

        return new Uri(url);
    }

    public static void Authorize(HttpRequestMessage message, string? apiKey, string? header)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        if (header is null || string.Equals(header, "Authorization", StringComparison.OrdinalIgnoreCase))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        else
        {
            message.Headers.TryAddWithoutValidation(header, apiKey);
        }
    }

    public static void AddHeaders(HttpRequestMessage message, IReadOnlyDictionary<string, string>? headers)
    {
        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            message.Headers.Remove(name);
            message.Headers.TryAddWithoutValidation(name, value);
        }
    }
}
