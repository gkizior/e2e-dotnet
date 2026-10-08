// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Anthropic Messages API client. The system prompt and the newest message carry cache breakpoints, as
/// upstream marks them, so each turn of a tool loop reads the previous turn's prefix from the cache.
/// </summary>
public sealed class AnthropicModel : IAgentModel, IDisposable
{
    private static readonly JsonObject Breakpoint = new() { ["type"] = "ephemeral" };

    private readonly AnthropicModelOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public AnthropicModel(AnthropicModelOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        _options = options;
        _http = httpClient ?? new HttpClient { Timeout = ModelHttp.Timeout };
        _ownsClient = httpClient is null;
    }

    public string Name => _options.Model;

    public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var message = new HttpRequestMessage(HttpMethod.Post, ModelEndpoint.Build(_options.BaseUrl, "/messages", null));
        var apiKey = _options.ResolveApiKey();
        if (apiKey is not null)
        {
            message.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        }

        message.Headers.TryAddWithoutValidation("anthropic-version", _options.Version);
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
        var messages = new JsonArray();
        foreach (var message in request.Messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                var content = new JsonArray();
                if (!string.IsNullOrEmpty(message.Content))
                {
                    content.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
                }

                foreach (var call in message.ToolCalls)
                {
                    content.Add(new JsonObject
                    {
                        ["type"] = "tool_use",
                        ["id"] = call.Id,
                        ["name"] = call.Name,
                        ["input"] = JsonNode.Parse(call.Arguments.GetRawText()),
                    });
                }

                Append(messages, "assistant", content);
            }
            else if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
            {
                Append(messages, "user", new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = message.ToolCallId,
                        ["content"] = message.Content ?? "",
                    },
                });
            }
            else
            {
                var role = string.Equals(message.Role, "assistant", StringComparison.Ordinal) ? "assistant" : "user";
                Append(messages, role, new JsonArray { new JsonObject { ["type"] = "text", ["text"] = string.IsNullOrEmpty(message.Content) ? "(empty)" : message.Content } });
            }
        }

        if (_options.CacheBreakpoints && messages.Count > 0 && messages[^1]?["content"] is JsonArray { Count: > 0 } latest)
        {
            latest[^1]!["cache_control"] = Breakpoint.DeepClone();
        }

        var system = new JsonObject { ["type"] = "text", ["text"] = request.System };
        if (_options.CacheBreakpoints)
        {
            system["cache_control"] = Breakpoint.DeepClone();
        }

        var tools = new JsonArray();
        foreach (var tool in request.Tools)
        {
            tools.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = JsonNode.Parse(tool.Parameters.GetRawText()),
            });
        }

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["max_tokens"] = _options.MaxTokens,
            ["system"] = new JsonArray { system },
            ["messages"] = messages,
        };

        if (tools.Count > 0)
        {
            payload["tools"] = tools;
            payload["tool_choice"] = new JsonObject { ["type"] = "auto" };
        }

        ModelHttp.MergeProviderOptions(payload, request, _options.Provider, "model", "messages", "tools", "system");
        return payload;
    }

    /// <summary>Anthropic wants turns to alternate, so consecutive turns of one role share a message.</summary>
    private static void Append(JsonArray messages, string role, JsonArray content)
    {
        if (messages.Count > 0 && messages[^1]?["role"]?.GetValue<string>() == role && messages[^1]?["content"] is JsonArray previous)
        {
            foreach (var part in content.ToArray())
            {
                content.Remove(part);
                previous.Add(part);
            }

            return;
        }

        messages.Add(new JsonObject { ["role"] = role, ["content"] = content });
    }

    internal static ModelResponse Parse(JsonElement root)
    {
        var text = new List<string>();
        var calls = new List<ModelToolCall>();
        foreach (var block in root.GetProperty("content").EnumerateArray())
        {
            switch (block.GetProperty("type").GetString())
            {
                case "text":
                    text.Add(block.GetProperty("text").GetString() ?? "");
                    break;
                case "tool_use":
                    calls.Add(new ModelToolCall
                    {
                        Id = block.GetProperty("id").GetString() ?? "call_" + calls.Count,
                        Name = block.GetProperty("name").GetString() ?? "",
                        Arguments = block.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object ? input.Clone() : ModelHttp.Arguments(null),
                    });
                    break;
            }
        }

        ModelUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ModelUsage
            {
                InputTokens = ModelHttp.Int(usageElement, "input_tokens")
                    + ModelHttp.Int(usageElement, "cache_read_input_tokens")
                    + ModelHttp.Int(usageElement, "cache_creation_input_tokens"),
                OutputTokens = ModelHttp.Int(usageElement, "output_tokens"),
            };
        }

        return new ModelResponse { Content = text.Count == 0 ? null : string.Concat(text), ToolCalls = calls, Usage = usage };
    }
}

public sealed class AnthropicModelOptions
{
    public required string Model { get; init; }

    /// <summary>The API root; requests go to <c>{BaseUrl}/messages</c>.</summary>
    public string BaseUrl { get; init; } = "https://api.anthropic.com/v1";

    /// <summary>The key this client reads in <see cref="ModelRequest.ProviderOptions"/>. Its fields are added to the body as given.</summary>
    public string Provider { get; init; } = "anthropic";

    public string? ApiKey { get; init; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty. Defaults to <c>ANTHROPIC_API_KEY</c>.</summary>
    public string ApiKeyEnv { get; init; } = "ANTHROPIC_API_KEY";

    /// <summary>The <c>anthropic-version</c> header.</summary>
    public string Version { get; init; } = "2023-06-01";

    /// <summary>The output cap each call sends. The Messages API requires one.</summary>
    public int MaxTokens { get; init; } = 8192;

    /// <summary>Marks the system prompt and the newest message as cache breakpoints. On by default, as upstream.</summary>
    public bool CacheBreakpoints { get; init; } = true;

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public string? ResolveApiKey()
    {
        return string.IsNullOrWhiteSpace(ApiKey) ? ModelHttp.Environment(ApiKeyEnv) : ApiKey;
    }
}
