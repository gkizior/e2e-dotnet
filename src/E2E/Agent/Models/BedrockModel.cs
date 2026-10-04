// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Amazon Bedrock Converse API client. It signs with AWS Signature Version 4 from the standard AWS environment
/// variables, or sends a Bedrock API key (<c>AWS_BEARER_TOKEN_BEDROCK</c>) as a bearer token when one is set.
/// </summary>
public sealed class BedrockModel : IAgentModel, IDisposable
{
    private readonly BedrockModelOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public BedrockModel(BedrockModelOptions options, HttpClient? httpClient = null)
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
        var region = _options.ResolveRegion();
        var baseUrl = _options.BaseUrl ?? "https://bedrock-runtime." + region + ".amazonaws.com";
        var url = new Uri(baseUrl.TrimEnd('/') + "/model/" + Uri.EscapeDataString(_options.Model) + "/converse");
        var payload = BuildBody(request).ToJsonString();
        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8),
        };

        // Signed as is: no charset parameter.
        message.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        var bearer = _options.ResolveApiKey();
        if (bearer is not null)
        {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
        }
        else
        {
            var credentials = _options.ResolveCredentials()
                ?? throw new AgentException(
                    "MODEL_PROVIDER_FAILED",
                    "Amazon Bedrock needs AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY, or a Bedrock API key in " + _options.ApiKeyEnv + ".",
                    blocked: true);
            SigV4.Sign(message, payload, credentials, region, "bedrock", DateTimeOffset.UtcNow);
        }

        var body = await ModelHttp.SendAsync(_http, message, cancellationToken).ConfigureAwait(false);
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
                    content.Add(new JsonObject { ["text"] = message.Content });
                }

                foreach (var call in message.ToolCalls)
                {
                    content.Add(new JsonObject
                    {
                        ["toolUse"] = new JsonObject
                        {
                            ["toolUseId"] = call.Id,
                            ["name"] = call.Name,
                            ["input"] = JsonNode.Parse(call.Arguments.GetRawText()),
                        },
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
                        ["toolResult"] = new JsonObject
                        {
                            ["toolUseId"] = message.ToolCallId,
                            ["content"] = new JsonArray { new JsonObject { ["text"] = string.IsNullOrEmpty(message.Content) ? "(empty)" : message.Content } },
                        },
                    },
                });
            }
            else
            {
                var role = string.Equals(message.Role, "assistant", StringComparison.Ordinal) ? "assistant" : "user";
                Append(messages, role, new JsonArray { new JsonObject { ["text"] = string.IsNullOrEmpty(message.Content) ? "(empty)" : message.Content } });
            }
        }

        var tools = new JsonArray();
        foreach (var tool in request.Tools)
        {
            tools.Add(new JsonObject
            {
                ["toolSpec"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["inputSchema"] = new JsonObject { ["json"] = JsonNode.Parse(tool.Parameters.GetRawText()) },
                },
            });
        }

        var payload = new JsonObject
        {
            ["system"] = new JsonArray { new JsonObject { ["text"] = request.System } },
            ["messages"] = messages,
            ["inferenceConfig"] = new JsonObject { ["maxTokens"] = _options.MaxTokens },
        };

        if (tools.Count > 0)
        {
            payload["toolConfig"] = new JsonObject { ["tools"] = tools };
        }

        ModelHttp.MergeProviderOptions(payload, request, _options.Provider, "messages", "system", "toolConfig");
        return payload;
    }

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
        foreach (var block in root.GetProperty("output").GetProperty("message").GetProperty("content").EnumerateArray())
        {
            if (block.TryGetProperty("text", out var value))
            {
                text.Add(value.GetString() ?? "");
            }
            else if (block.TryGetProperty("toolUse", out var use))
            {
                calls.Add(new ModelToolCall
                {
                    Id = use.GetProperty("toolUseId").GetString() ?? "call_" + calls.Count,
                    Name = use.GetProperty("name").GetString() ?? "",
                    Arguments = use.TryGetProperty("input", out var input) && input.ValueKind == JsonValueKind.Object ? input.Clone() : ModelHttp.Arguments(null),
                });
            }
        }

        ModelUsage? usage = null;
        if (root.TryGetProperty("usage", out var usageElement) && usageElement.ValueKind == JsonValueKind.Object)
        {
            usage = new ModelUsage
            {
                InputTokens = ModelHttp.Int(usageElement, "inputTokens"),
                OutputTokens = ModelHttp.Int(usageElement, "outputTokens"),
            };
        }

        return new ModelResponse { Content = text.Count == 0 ? null : string.Concat(text), ToolCalls = calls, Usage = usage };
    }
}

public sealed class BedrockModelOptions
{
    /// <summary>A Bedrock model id or inference profile, such as <c>us.anthropic.claude-sonnet-4-5-20250929-v1:0</c>.</summary>
    public required string Model { get; init; }

    /// <summary>The AWS region. Unset, it is <c>AWS_REGION</c>, then <c>AWS_DEFAULT_REGION</c>, then <c>us-east-1</c>.</summary>
    public string? Region { get; init; }

    /// <summary>The runtime endpoint. Unset, it is <c>https://bedrock-runtime.{region}.amazonaws.com</c>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>The key this client reads in <see cref="ModelRequest.ProviderOptions"/>. Its fields are added to the body as given.</summary>
    public string Provider { get; init; } = "bedrock";

    /// <summary>A Bedrock API key, sent as a bearer token instead of signing.</summary>
    public string? ApiKey { get; init; }

    /// <summary>Environment variable read when <see cref="ApiKey"/> is empty. Defaults to <c>AWS_BEARER_TOKEN_BEDROCK</c>.</summary>
    public string ApiKeyEnv { get; init; } = "AWS_BEARER_TOKEN_BEDROCK";

    /// <summary>Signing credentials. Unset, they come from <c>AWS_ACCESS_KEY_ID</c>, <c>AWS_SECRET_ACCESS_KEY</c>, and <c>AWS_SESSION_TOKEN</c>.</summary>
    public AwsCredentials? Credentials { get; init; }

    public int MaxTokens { get; init; } = 8192;

    public string? ResolveApiKey()
    {
        return string.IsNullOrWhiteSpace(ApiKey) ? ModelHttp.Environment(ApiKeyEnv) : ApiKey;
    }

    public string ResolveRegion()
    {
        return Region ?? ModelHttp.Environment("AWS_REGION") ?? ModelHttp.Environment("AWS_DEFAULT_REGION") ?? "us-east-1";
    }

    public AwsCredentials? ResolveCredentials()
    {
        if (Credentials is not null)
        {
            return Credentials;
        }

        var accessKey = ModelHttp.Environment("AWS_ACCESS_KEY_ID");
        var secretKey = ModelHttp.Environment("AWS_SECRET_ACCESS_KEY");
        return accessKey is null || secretKey is null
            ? null
            : new AwsCredentials(accessKey, secretKey, ModelHttp.Environment("AWS_SESSION_TOKEN"));
    }
}

/// <summary>AWS access key credentials for Signature Version 4.</summary>
public sealed record AwsCredentials(string AccessKeyId, string SecretAccessKey, string? SessionToken = null);

/// <summary>AWS Signature Version 4 for one JSON request.</summary>
internal static class SigV4
{
    public static void Sign(HttpRequestMessage message, string payload, AwsCredentials credentials, string region, string service, DateTimeOffset now)
    {
        var uri = message.RequestUri!;
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var date = amzDate[..8];
        var payloadHash = Hex(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        var host = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);

        var headers = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = host,
            ["x-amz-date"] = amzDate,
            ["x-amz-content-sha256"] = payloadHash,
            ["content-type"] = "application/json",
        };
        if (credentials.SessionToken is not null)
        {
            headers["x-amz-security-token"] = credentials.SessionToken;
        }

        var signedHeaders = string.Join(';', headers.Keys);
        var canonical = string.Join(
            '\n',
            "POST",
            CanonicalPath(uri.AbsolutePath),
            CanonicalQuery(uri.Query),
            string.Concat(headers.Select(pair => pair.Key + ":" + pair.Value.Trim() + "\n")),
            signedHeaders,
            payloadHash);
        var scope = date + "/" + region + "/" + service + "/aws4_request";
        var toSign = string.Join('\n', "AWS4-HMAC-SHA256", amzDate, scope, Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))));

        var signature = Signature(credentials.SecretAccessKey, date, region, service, toSign);

        message.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        message.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        if (credentials.SessionToken is not null)
        {
            message.Headers.TryAddWithoutValidation("x-amz-security-token", credentials.SessionToken);
        }

        message.Headers.TryAddWithoutValidation(
            "Authorization",
            "AWS4-HMAC-SHA256 Credential=" + credentials.AccessKeyId + "/" + scope + ", SignedHeaders=" + signedHeaders + ", Signature=" + signature);
    }

    /// <summary>The signature of <paramref name="toSign"/> with the key derived for the date, region, and service.</summary>
    internal static string Signature(string secretAccessKey, string date, string region, string service, string toSign)
    {
        var key = Hmac(Encoding.UTF8.GetBytes("AWS4" + secretAccessKey), date);
        key = Hmac(key, region);
        key = Hmac(key, service);
        key = Hmac(key, "aws4_request");
        return Hex(Hmac(key, toSign));
    }

    internal static string Hash(string text) => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Every service but S3 encodes each path segment once more for the canonical request.</summary>
    internal static string CanonicalPath(string path)
    {
        return string.Join('/', path.Split('/').Select(segment => Encode(segment)));
    }

    private static string CanonicalQuery(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            return "";
        }

        return string.Join('&', query.TrimStart('?').Split('&')
            .Select(pair => pair.Split('=', 2))
            .Select(parts => (Key: Encode(Uri.UnescapeDataString(parts[0])), Value: parts.Length > 1 ? Encode(Uri.UnescapeDataString(parts[1])) : ""))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value));
    }

    /// <summary>RFC 3986 encoding: everything but unreserved characters.</summary>
    private static string Encode(string value)
    {
        var builder = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~')
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        return HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    }

    private static string Hex(byte[] bytes)
    {
        return Convert.ToHexStringLower(bytes);
    }
}
