// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E;

/// <summary>
/// OpenAI Responses API client: OpenAI itself, Azure OpenAI (<see cref="ModelProviders.AzureOpenAi"/>), and the
/// subscription backends that only speak Responses. Nothing is stored server side, so each turn carries the
/// whole conversation. A server-sent-event answer is folded back into the completed response.
/// </summary>
public sealed class OpenAiResponsesModel : IAgentModel, IDisposable
{
    private readonly OpenAiResponsesModelOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public OpenAiResponsesModel(OpenAiResponsesModelOptions options, HttpClient? httpClient = null)
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
        using var message = new HttpRequestMessage(HttpMethod.Post, ModelEndpoint.Build(_options.BaseUrl, "/responses", _options.QueryParameters));
        ModelEndpoint.Authorize(message, _options.ResolveApiKey(), _options.ApiKeyHeader);
        ModelEndpoint.AddHeaders(message, _options.Headers);
        message.Content = ModelHttp.Json(BuildBody(request));
        var body = await ModelHttp.SendAsync(_http, message, request.Redactor, cancellationToken).ConfigureAwait(false);
        var folded = ResponsesStream.Fold(body);
        if (folded.Error is not null)
        {
            throw new AgentException("MODEL_PROVIDER_FAILED", "The model provider failed the response. " + folded.Error);
        }

        return ModelHttp.Parse(folded.Body, Parse);
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
        var input = new JsonArray();
        foreach (var message in request.Messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    input.Add(Text("assistant", message.Content));
                }

                // No item ids: with storage off the backend cannot resolve them, and the call id is what pairs an output.
                foreach (var call in message.ToolCalls)
                {
                    input.Add(new JsonObject
                    {
                        ["type"] = "function_call",
                        ["call_id"] = call.Id,
                        ["name"] = call.Name,
                        ["arguments"] = call.Arguments.GetRawText(),
                    });
                }
            }
            else if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
            {
                input.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = message.ToolCallId,
                    ["output"] = message.Content ?? "",
                });
            }
            else
            {
                input.Add(Text(message.Role, message.Content ?? ""));
            }
        }

        var tools = new JsonArray();
        foreach (var tool in request.Tools)
        {
            tools.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = JsonNode.Parse(tool.Parameters.GetRawText()),
                ["strict"] = false,
            });
        }

        var payload = new JsonObject
        {
            ["model"] = _options.Model,
            ["instructions"] = request.System,
            ["input"] = input,
            ["tools"] = tools,
            ["tool_choice"] = "auto",
            ["store"] = false,
        };

        if (_options.PromptCacheHints)
        {
            payload["prompt_cache_key"] = request.System.Length == 0 && _options.SessionId is not null
                ? _options.SessionId
                : ModelHttp.PromptCacheKey(request.System);
        }

        ModelHttp.MergeProviderOptions(payload, request, _options.Provider, "model", "input", "tools", "instructions");
        return payload;
    }

    private static JsonObject Text(string role, string text)
    {
        var assistant = string.Equals(role, "assistant", StringComparison.Ordinal);
        return new JsonObject
        {
            ["role"] = assistant ? "assistant" : role,
            ["content"] = new JsonArray
            {
                new JsonObject { ["type"] = assistant ? "output_text" : "input_text", ["text"] = text },
            },
        };
    }

    internal static ModelResponse Parse(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            throw new AgentException("MODEL_PROVIDER_FAILED", "The model provider failed the response. " + error.GetRawText());
        }

        var text = new List<string>();
        var calls = new List<ModelToolCall>();
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            var type = item.TryGetProperty("type", out var kind) ? kind.GetString() : null;
            if (type == "function_call")
            {
                calls.Add(new ModelToolCall
                {
                    Id = item.TryGetProperty("call_id", out var id) ? id.GetString() ?? "call" : "call_" + calls.Count,
                    Name = item.GetProperty("name").GetString() ?? "",
                    Arguments = ModelHttp.Arguments(item.TryGetProperty("arguments", out var arguments) ? arguments.GetString() : null),
                });
            }
            else if (type == "message" && item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var partType) && partType.GetString() == "output_text" && part.TryGetProperty("text", out var value))
                    {
                        text.Add(value.GetString() ?? "");
                    }
                }
            }
        }

        ModelUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ModelUsage
            {
                InputTokens = ModelHttp.Int(usageElement, "input_tokens"),
                OutputTokens = ModelHttp.Int(usageElement, "output_tokens"),
            };
        }

        return new ModelResponse { Content = text.Count == 0 ? null : string.Concat(text), ToolCalls = calls, Usage = usage };
    }
}

public sealed class OpenAiResponsesModelOptions
{
    public required string Model { get; init; }

    /// <summary>The API root; requests go to <c>{BaseUrl}/responses</c>.</summary>
    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    /// <summary>The key this client reads in <see cref="ModelRequest.ProviderOptions"/>: <c>openai</c>, or <c>azure</c> for Azure OpenAI.</summary>
    public string Provider { get; init; } = "openai";

    public string? ApiKey { get; init; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty. Defaults to <c>OPENAI_API_KEY</c>.</summary>
    public string ApiKeyEnv { get; init; } = "OPENAI_API_KEY";

    /// <summary>The header that carries the key. Null sends <c>Authorization: Bearer</c>.</summary>
    public string? ApiKeyHeader { get; init; }

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public IReadOnlyDictionary<string, string>? QueryParameters { get; init; }

    /// <summary>Whether requests carry a prompt-cache routing key, one per system prompt. Upstream sends it to OpenAI and Azure.</summary>
    public bool PromptCacheHints { get; init; } = true;

    /// <summary>The routing key for a request with no system prompt, so promptless calls of one session share a cache and other sessions do not. Null hashes the empty prompt.</summary>
    public string? SessionId { get; init; }

    public string? ResolveApiKey()
    {
        return string.IsNullOrWhiteSpace(ApiKey) ? ModelHttp.Environment(ApiKeyEnv) : ApiKey;
    }
}

/// <summary>
/// Folds an OpenAI Responses event stream into the body the non-streaming endpoint returns. The final
/// <c>response.completed</c> (or <c>response.incomplete</c>) event carries that body; when its output is empty,
/// the items streamed as <c>response.output_item.done</c> are folded back in.
/// </summary>
internal static class ResponsesStream
{
    public static (string Body, string? Error) Fold(string body)
    {
        var trimmed = body.TrimStart();
        if (!trimmed.StartsWith("event:", StringComparison.Ordinal) && !trimmed.StartsWith("data:", StringComparison.Ordinal))
        {
            return (body, null);
        }

        var items = new SortedDictionary<int, JsonNode?>();
        string? failure = null;
        foreach (var data in Events(body))
        {
            if (data == "[DONE]")
            {
                continue;
            }

            JsonNode? payload;
            try
            {
                payload = JsonNode.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }

            if (payload is not JsonObject record)
            {
                continue;
            }

            var type = record["type"]?.GetValue<string>();
            if (type == "response.output_item.done" && record["output_index"] is JsonValue index && index.TryGetValue<int>(out var position))
            {
                items[position] = record["item"]?.DeepClone();
            }
            else if (type is "response.completed" or "response.incomplete" && record["response"] is JsonObject response)
            {
                if (items.Count > 0 && response["output"] is not JsonArray { Count: > 0 })
                {
                    response["output"] = new JsonArray(items.Values.Select(item => item?.DeepClone()).ToArray());
                }

                return (response.ToJsonString(), null);
            }
            else if (type == "response.failed")
            {
                failure = record["response"]?["error"]?.ToJsonString() ?? "the response failed";
            }
            else if (type == "error")
            {
                failure = record["message"]?.ToString() ?? "the response failed";
            }
        }

        return (body, failure ?? "the stream ended without a completed response");
    }

    /// <summary>The data of each event in a text/event-stream body, tolerating either newline convention.</summary>
    public static IEnumerable<string> Events(string text)
    {
        foreach (var block in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split("\n\n"))
        {
            var data = new List<string>();
            foreach (var line in block.Split('\n'))
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    data.Add(line[5..].TrimStart());
                }
            }

            if (data.Count > 0)
            {
                yield return string.Join('\n', data);
            }
        }
    }
}
