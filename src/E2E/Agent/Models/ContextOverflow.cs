// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Internal;

/// <summary>
/// Recognizes a context-window overflow in a provider failure. Providers say it in as many ways as there are
/// providers, and a gateway forwards their words; the patterns below are the ones seen in the wild (upstream
/// ported them from the pi agent harness), plus the HTTP 413 some of them answer with instead of a message.
/// </summary>
internal static class ContextOverflow
{
    /// <summary>Longest cause chain walked; a wrapped error rarely nests deeper.</summary>
    private const int MaxCauseDepth = 8;

    private static readonly Regex[] OverflowPatterns =
    [
        Pattern("prompt is too long"), // Anthropic
        Pattern("request_too_large"), // Anthropic (HTTP 413)
        Pattern("input is too long for requested model"), // Amazon Bedrock
        Pattern("exceeds the context window"), // OpenAI
        Pattern(@"exceeds (?:the )?(?:model'?s )?maximum context length(?: of [\d,]+ tokens?|\s*\([\d,]+\))"), // OpenAI-compatible proxies
        Pattern("input token count.*exceeds the maximum"), // Google
        Pattern(@"maximum prompt length is \d+"), // xAI
        Pattern("reduce the length of the messages"), // Groq
        Pattern(@"maximum context length is \d+ tokens"), // OpenRouter
        Pattern(@"exceeds (?:the )?maximum allowed input length of [\d,]+ tokens?"), // OpenRouter/Poolside
        Pattern(@"input \(\d+ tokens\) is longer than the model'?s context length \(\d+ tokens\)"), // Together AI
        Pattern(@"exceeds the limit of \d+"), // GitHub Copilot
        Pattern("exceeds the available context size"), // llama.cpp
        Pattern("greater than the context length"), // LM Studio
        Pattern("context window exceeds limit"), // MiniMax
        Pattern("exceeded model token limit"), // Kimi
        Pattern(@"too large for model with \d+ maximum context length"), // Mistral
        Pattern(@"prompt has [\d,]+ tokens?, but the configured context size is [\d,]+ tokens?"), // DS4
        Pattern("model_context_window_exceeded"), // z.ai
        Pattern(@"prompt too long; exceeded (?:max )?context length"), // Ollama
        Pattern("range of input length should be"), // DashScope / Qwen
        Pattern("context[_ ]length[_ ]exceeded"), // generic
        Pattern("too many tokens"), // generic
        Pattern("token limit exceeded"), // generic
    ];

    /// <summary>Failures that mention tokens without being an overflow: throttling and its cousins.</summary>
    private static readonly Regex[] NonOverflowPatterns =
    [
        Pattern("throttl"), // Amazon Bedrock: "ThrottlingException: Too many tokens, please wait…"
        Pattern("service unavailable"),
        Pattern("rate limit"),
        Pattern("too many requests"),
    ];

    /// <summary>
    /// Whether a provider failure says the request exceeded the model's context window. Reads the message of
    /// the error and of everything it wraps, so a gateway or retry wrapper does not hide the provider's words.
    /// </summary>
    public static bool Describes(Exception? error)
    {
        var current = error;
        for (var depth = 0; depth < MaxCauseDepth && current is not null; depth++)
        {
            if (Describes(null, current.Message, null))
            {
                return true;
            }

            current = current.InnerException;
        }

        return false;
    }

    /// <summary>
    /// One failure, read on its own. HTTP 413 is an overflow whatever the text says. A text counts when it
    /// matches an overflow pattern and no throttling pattern; the veto is per text, so a wrapper's "rate limit"
    /// message cannot hide the provider's "prompt is too long" in the body.
    /// </summary>
    public static bool Describes(int? status, string? message, string? body)
    {
        return status == 413 || Matches(message) || Matches(body);
    }

    private static bool Matches(string? text)
    {
        return text is not null
            && OverflowPatterns.Any(pattern => pattern.IsMatch(text))
            && !NonOverflowPatterns.Any(pattern => pattern.IsMatch(text));
    }

    private static Regex Pattern(string source)
    {
        return new Regex(source, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
