// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace E2E;

/// <summary>
/// Chat-completions client for OpenAI and any server that speaks the same tool-call JSON.
/// Set <see cref="OpenAiCompatibleModelOptions.BaseUrl"/> to a local server such as
/// <c>http://127.0.0.1:11434/v1</c>. The API key is optional for servers that do not check one.
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
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
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
        var apiKey = _options.ResolveApiKey();
        var endpoint = _options.BaseUrl.TrimEnd('/') + "/chat/completions";
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        message.Headers.TryAddWithoutValidation("User-Agent", "e2e-dotnet");
        message.Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AgentException("MODEL_PROVIDER_FAILED", "The model request got no response within 120 seconds.");
        }
        catch (HttpRequestException ex)
        {
            throw new AgentException("MODEL_PROVIDER_FAILED", "The model provider could not be reached.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = body.Length > 400 ? body[..400] : body;
                throw new AgentException(
                    "MODEL_PROVIDER_FAILED",
                    "The model provider returned " + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture) + ". " + detail);
            }

            try
            {
                return Parse(body);
            }
            catch (JsonException ex)
            {
                throw new AgentException("MODEL_OUTPUT_INVALID", "The model provider returned JSON the runner could not read.", ex);
            }
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private string BuildBody(ModelRequest request)
    {
        var messages = new List<object>
        {
            new { role = "system", content = request.System },
        };
        foreach (var message in request.Messages)
        {
            if (message.ToolCalls is { Count: > 0 })
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = message.Content ?? "",
                    tool_calls = message.ToolCalls.Select(call => new
                    {
                        id = call.Id,
                        type = "function",
                        function = new
                        {
                            name = call.Name,
                            arguments = call.Arguments.GetRawText(),
                        },
                    }).ToArray(),
                });
            }
            else if (string.Equals(message.Role, "tool", StringComparison.Ordinal))
            {
                messages.Add(new
                {
                    role = "tool",
                    tool_call_id = message.ToolCallId,
                    content = message.Content ?? "",
                });
            }
            else
            {
                messages.Add(new { role = message.Role, content = message.Content ?? "" });
            }
        }

        var tools = request.Tools.Select(tool => new
        {
            type = "function",
            function = new
            {
                name = tool.Name,
                description = tool.Description,
                parameters = tool.Parameters,
            },
        }).ToArray();

        var payload = new
        {
            model = _options.Model,
            messages,
            tools,
            tool_choice = "auto",
        };
        return JsonSerializer.Serialize(payload);
    }

    private static ModelResponse Parse(string body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
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
                var arguments = function.GetProperty("arguments").GetString();
                if (string.IsNullOrWhiteSpace(arguments))
                {
                    arguments = "{}";
                }

                using var parsed = JsonDocument.Parse(arguments);
                calls.Add(new ModelToolCall
                {
                    Id = call.GetProperty("id").GetString() ?? "call",
                    Name = function.GetProperty("name").GetString() ?? "",
                    Arguments = parsed.RootElement.Clone(),
                });
            }
        }

        ModelUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ModelUsage
            {
                InputTokens = usageElement.TryGetProperty("prompt_tokens", out var input) ? input.GetInt32() : 0,
                OutputTokens = usageElement.TryGetProperty("completion_tokens", out var output) ? output.GetInt32() : 0,
            };
        }

        return new ModelResponse { Content = content, ToolCalls = calls, Usage = usage };
    }
}

public sealed class OpenAiCompatibleModelOptions
{
    public required string Model { get; init; }

    public string BaseUrl { get; init; } = "https://api.openai.com/v1";

    public string? ApiKey { get; init; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty. Defaults to <c>OPENAI_API_KEY</c>.</summary>
    public string ApiKeyEnv { get; init; } = "OPENAI_API_KEY";

    public string? ResolveApiKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            return ApiKey;
        }

        var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnv);
        return string.IsNullOrWhiteSpace(fromEnv) ? null : fromEnv;
    }
}
