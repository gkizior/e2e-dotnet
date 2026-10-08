// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.OpencodeConsole;

/// <summary>A local Console and inference: the v1 config at its path, and one canned answer per protocol.</summary>
internal static class OpencodeConsoleWorkspace
{
    public const string ConsoleUrl = "https://opencode.ai/console";

    public static readonly OAuthCredentials Login = new()
    {
        Access = "st_tok",
        Refresh = "rt_1",
        Expires = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000,
        Extra = new Dictionary<string, string> { ["orgId"] = "org_1" },
    };

    /// <summary>The v1 Console config, trimmed to what routing and the listing read.</summary>
    private const string Config = """
        { "config": { "provider": {
          "opencode": {
            "npm": "@ai-sdk/openai-compatible",
            "api": "https://opencode.ai/inference/openai/v1",
            "models": {
              "claude-sonnet-5": { "name": "Claude Sonnet 5", "cost": { "input": 2, "output": 10 }, "modalities": { "input": ["text", "image"] }, "provider": { "npm": "@ai-sdk/anthropic", "api": "https://opencode.ai/inference/anthropic/v1" } },
              "gemini-3.1-pro": { "cost": { "input": 2, "output": 12 }, "provider": { "npm": "@ai-sdk/google", "api": "https://opencode.ai/inference/google/v1beta" } },
              "gpt-5-nano": { "cost": { "input": 0.05, "output": 0.4 }, "provider": { "npm": "@ai-sdk/openai" } },
              "glm-5.3": { "cost": { "input": 1.4, "output": 4.4 } },
              "glm-5.1": { "cost": { "input": 1.4, "output": 4.4 }, "disabled": true },
              "big-pickle": { "cost": { "input": 0, "output": 0 } }
            }
          },
          "opencode-go": {
            "npm": "@ai-sdk/openai-compatible",
            "api": "https://opencode.ai/inference/go/openai/v1",
            "models": { "deepseek-v4.1-flash": { "name": "DeepSeek V4.1 Flash", "modalities": { "input": ["text", "image"] } } }
          },
          "console-anthropic": { "npm": "@ai-sdk/anthropic", "api": "https://opencode.ai/inference/custom/conn_1", "models": { "claude-opus-5-5": {} } }
        } } }
        """;

    /// <summary>The config with only the Zen provider, for a login that reaches no Go subscription.</summary>
    public static string ZenOnlyConfig => Config[..Config.IndexOf("\"opencode-go\"", StringComparison.Ordinal)].TrimEnd().TrimEnd(',') + " } } }";

    public const string ChatCompletion = """{ "id": "c1", "object": "chat.completion", "created": 1, "model": "m", "choices": [{ "index": 0, "finish_reason": "stop", "message": { "role": "assistant", "content": "hello" } }], "usage": { "prompt_tokens": 3, "completion_tokens": 1, "total_tokens": 4 } }""";

    public const string AnthropicMessage = """{ "id": "msg_1", "type": "message", "role": "assistant", "model": "claude-sonnet-5", "content": [{ "type": "text", "text": "hello" }], "stop_reason": "end_turn", "usage": { "input_tokens": 3, "output_tokens": 1 } }""";

    public static FakeApi Serve(string? config = null)
    {
        return new FakeApi(request =>
        {
            var path = request.Uri.AbsolutePath;
            if (path == "/console/api/config")
            {
                return (HttpStatusCode.OK, config ?? Config);
            }

            if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, ChatCompletion);
            }

            if (path.EndsWith("/messages", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, AnthropicMessage);
            }

            if (path.Contains(":generateContent", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, """{ "candidates": [{ "content": { "role": "model", "parts": [{ "text": "hello" }] }, "finishReason": "STOP" }], "usageMetadata": { "promptTokenCount": 3, "candidatesTokenCount": 1, "totalTokenCount": 4 } }""");
            }

            if (path.EndsWith("/responses", StringComparison.Ordinal))
            {
                return (HttpStatusCode.OK, """{ "id": "resp_1", "object": "response", "created_at": 1, "status": "completed", "model": "gpt-5-nano", "output": [{ "type": "message", "id": "msg_1", "role": "assistant", "status": "completed", "content": [{ "type": "output_text", "text": "hello", "annotations": [] }] }], "usage": { "input_tokens": 3, "output_tokens": 1, "total_tokens": 4 } }""");
            }

            return (HttpStatusCode.NotFound, """{ "error": "not found" }""");
        });
    }

    /// <summary>The requests that reached inference, leaving out the config reads.</summary>
    public static List<FakeApi.Received> Inference(FakeApi api) =>
        [.. api.Requests.Where(request => request.Uri.AbsolutePath.StartsWith("/inference/", StringComparison.Ordinal))];

    public static ModelRequest Prompt(string text, string system = "") =>
        new() { System = system, Tools = [], Messages = [new ModelMessage { Role = "user", Content = text }] };
}
