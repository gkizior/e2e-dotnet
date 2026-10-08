// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Google Gemini API (Generative Language) client. Tool schemas go as <c>parametersJsonSchema</c>, so the agent's
/// JSON Schemas need no conversion. Thought signatures Gemini attaches to a function call are sent back with it.
/// </summary>
public sealed class GoogleModel : IAgentModel, IDisposable
{
    /// <summary>The value Gemini documents for a function call whose signature was not kept.</summary>
    private const string SkipSignature = "skip_thought_signature_validator";

    private readonly GoogleModelOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly ConcurrentDictionary<string, string> _signatures = new(StringComparer.Ordinal);
    private int _calls;

    public GoogleModel(GoogleModelOptions options, HttpClient? httpClient = null)
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
        var model = _options.Model.StartsWith("models/", StringComparison.Ordinal) ? _options.Model : "models/" + _options.Model;
        using var message = new HttpRequestMessage(HttpMethod.Post, ModelEndpoint.Build(_options.BaseUrl, "/" + model + ":generateContent", null));
        var apiKey = _options.ResolveApiKey();
        if (apiKey is not null)
        {
            message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        }

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
        var names = ModelHttp.ToolNames(request.Messages);
        var contents = new JsonArray();
        foreach (var message in request.Messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                var parts = new JsonArray();
                if (!string.IsNullOrEmpty(message.Content))
                {
                    parts.Add(new JsonObject { ["text"] = message.Content });
                }

                foreach (var call in message.ToolCalls)
                {
                    var part = new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = call.Name,
                            ["args"] = JsonNode.Parse(call.Arguments.GetRawText()),
                        },
                    };

                    // Gemini 3 rejects a function call without its signature; a call from elsewhere gets the documented placeholder.
                    if (_signatures.TryGetValue(call.Id, out var signature))
                    {
                        part["thoughtSignature"] = signature;
                    }
                    else if (_options.Model.Contains("gemini-3", StringComparison.OrdinalIgnoreCase))
                    {
                        part["thoughtSignature"] = SkipSignature;
                    }

                    parts.Add(part);
                }

                Append(contents, "model", parts);
            }
            else if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
            {
                var name = message.Name ?? (message.ToolCallId is not null && names.TryGetValue(message.ToolCallId, out var known) ? known : "tool");
                Append(contents, "user", new JsonArray
                {
                    new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = name,
                            ["response"] = new JsonObject { ["content"] = message.Content ?? "" },
                        },
                    },
                });
            }
            else
            {
                var role = string.Equals(message.Role, "assistant", StringComparison.Ordinal) ? "model" : "user";
                Append(contents, role, new JsonArray { new JsonObject { ["text"] = message.Content ?? "" } });
            }
        }

        var declarations = new JsonArray();
        foreach (var tool in request.Tools)
        {
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parametersJsonSchema"] = JsonNode.Parse(tool.Parameters.GetRawText()),
            });
        }

        var payload = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.System } } },
            ["contents"] = contents,
        };

        if (declarations.Count > 0)
        {
            payload["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = declarations } };
            payload["toolConfig"] = new JsonObject { ["functionCallingConfig"] = new JsonObject { ["mode"] = "AUTO" } };
        }

        ModelHttp.MergeProviderOptions(payload, request, _options.Provider, "contents", "tools", "systemInstruction");
        return payload;
    }

    private static void Append(JsonArray contents, string role, JsonArray parts)
    {
        if (contents.Count > 0 && contents[^1]?["role"]?.GetValue<string>() == role && contents[^1]?["parts"] is JsonArray previous)
        {
            foreach (var part in parts.ToArray())
            {
                parts.Remove(part);
                previous.Add(part);
            }

            return;
        }

        contents.Add(new JsonObject { ["role"] = role, ["parts"] = parts });
    }

    private ModelResponse Parse(JsonElement root)
    {
        var text = new List<string>();
        var calls = new List<ModelToolCall>();
        if (root.TryGetProperty("candidates", out var candidates) && candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content) && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("functionCall", out var call))
                {
                    var id = call.TryGetProperty("id", out var given) && given.ValueKind == JsonValueKind.String
                        ? given.GetString()!
                        : "gemini_" + Interlocked.Increment(ref _calls).ToString(CultureInfo.InvariantCulture);
                    if (part.TryGetProperty("thoughtSignature", out var signature) && signature.ValueKind == JsonValueKind.String)
                    {
                        _signatures[id] = signature.GetString()!;
                    }

                    calls.Add(new ModelToolCall
                    {
                        Id = id,
                        Name = call.GetProperty("name").GetString() ?? "",
                        Arguments = call.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object ? args.Clone() : ModelHttp.Arguments(null),
                    });
                }
                else if (part.TryGetProperty("text", out var value) && !(part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True))
                {
                    text.Add(value.GetString() ?? "");
                }
            }
        }

        ModelUsage? usage = null;
        if (root.TryGetProperty("usageMetadata", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ModelUsage
            {
                InputTokens = ModelHttp.Int(usageElement, "promptTokenCount"),
                OutputTokens = ModelHttp.Int(usageElement, "candidatesTokenCount") + ModelHttp.Int(usageElement, "thoughtsTokenCount"),
            };
        }

        return new ModelResponse { Content = text.Count == 0 ? null : string.Concat(text), ToolCalls = calls, Usage = usage };
    }
}

public sealed class GoogleModelOptions
{
    public required string Model { get; init; }

    /// <summary>The API root; requests go to <c>{BaseUrl}/models/{Model}:generateContent</c>.</summary>
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>The key this client reads in <see cref="ModelRequest.ProviderOptions"/>. Its fields (such as <c>generationConfig</c>) are added to the body.</summary>
    public string Provider { get; init; } = "google";

    public string? ApiKey { get; init; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty. Defaults to <c>GOOGLE_GENERATIVE_AI_API_KEY</c>, as the AI SDK.</summary>
    public string ApiKeyEnv { get; init; } = "GOOGLE_GENERATIVE_AI_API_KEY";

    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    public string? ResolveApiKey()
    {
        return string.IsNullOrWhiteSpace(ApiKey) ? ModelHttp.Environment(ApiKeyEnv) : ApiKey;
    }
}
