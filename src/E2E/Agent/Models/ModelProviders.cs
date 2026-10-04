// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E;

/// <summary>
/// Ready-made models for the providers upstream's <c>e2e init</c> and docs name, each reading the key from the
/// environment variable the AI SDK reads. Subscription logins are in <see cref="OAuth.Subscriptions"/>.
/// </summary>
public static class ModelProviders
{
    /// <summary>App attribution the Vercel AI Gateway and OpenRouter read, and others ignore.</summary>
    internal static readonly IReadOnlyDictionary<string, string> AttributionHeaders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["HTTP-Referer"] = "https://github.com/hardkoded/e2e-dotnet",
        ["X-Title"] = "e2e-dotnet",
    };

    /// <summary>OpenAI over chat completions, reading <c>OPENAI_API_KEY</c>.</summary>
    public static OpenAiCompatibleModel OpenAi(string model, string? apiKey = null)
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = model, ApiKey = apiKey });
    }

    /// <summary>OpenAI over the Responses API, reading <c>OPENAI_API_KEY</c>. The AI SDK's <c>openai(id)</c> calls this API.</summary>
    public static OpenAiResponsesModel OpenAiResponses(string model, string? apiKey = null)
    {
        return new OpenAiResponsesModel(new OpenAiResponsesModelOptions { Model = model, ApiKey = apiKey });
    }

    /// <summary>
    /// An Azure OpenAI deployment over the Responses API (<c>/openai/v1</c>), reading <c>AZURE_API_KEY</c>.
    /// <paramref name="resourceName"/> defaults to <c>AZURE_RESOURCE_NAME</c>.
    /// </summary>
    public static OpenAiResponsesModel AzureOpenAi(string deployment, string? resourceName = null, string? apiKey = null)
    {
        var resource = resourceName ?? ModelHttp.Environment("AZURE_RESOURCE_NAME")
            ?? throw new ConfigurationException("INVALID_CONFIG", "Azure OpenAI needs a resource name: pass it or set AZURE_RESOURCE_NAME");
        return AzureOpenAiAt(deployment, "https://" + resource + ".openai.azure.com/openai/v1", apiKey);
    }

    /// <summary>An Azure OpenAI deployment at an explicit <c>/openai/v1</c> base URL, reading <c>AZURE_API_KEY</c>.</summary>
    public static OpenAiResponsesModel AzureOpenAiAt(string deployment, string baseUrl, string? apiKey = null)
    {
        return new OpenAiResponsesModel(new OpenAiResponsesModelOptions
        {
            Model = deployment,
            BaseUrl = baseUrl,
            Provider = "azure",
            ApiKey = apiKey,
            ApiKeyEnv = "AZURE_API_KEY",
            ApiKeyHeader = "api-key",
            QueryParameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["api-version"] = "v1" },
        });
    }

    /// <summary>Anthropic's Messages API, reading <c>ANTHROPIC_API_KEY</c>.</summary>
    public static AnthropicModel Anthropic(string model, string? apiKey = null)
    {
        return new AnthropicModel(new AnthropicModelOptions { Model = model, ApiKey = apiKey });
    }

    /// <summary>Google's Gemini API, reading <c>GOOGLE_GENERATIVE_AI_API_KEY</c>.</summary>
    public static GoogleModel Google(string model, string? apiKey = null)
    {
        return new GoogleModel(new GoogleModelOptions { Model = model, ApiKey = apiKey });
    }

    /// <summary>Amazon Bedrock's Converse API, signed from the AWS environment or with <c>AWS_BEARER_TOKEN_BEDROCK</c>.</summary>
    public static BedrockModel Bedrock(string model, string? region = null)
    {
        return new BedrockModel(new BedrockModelOptions { Model = model, Region = region });
    }

    /// <summary>The SpaceXAI API, reading <c>XAI_API_KEY</c>.</summary>
    public static OpenAiCompatibleModel Xai(string model, string? apiKey = null)
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions
        {
            Model = model,
            BaseUrl = "https://api.x.ai/v1",
            Provider = "xai",
            ApiKey = apiKey,
            ApiKeyEnv = "XAI_API_KEY",
        });
    }

    /// <summary>OpenRouter, reading <c>OPENROUTER_API_KEY</c>. Model ids name the upstream, such as <c>openai/gpt-6-luna-fast</c>.</summary>
    public static OpenAiCompatibleModel OpenRouter(string model, string? apiKey = null)
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions
        {
            Model = model,
            BaseUrl = "https://openrouter.ai/api/v1",
            Provider = "openrouter",
            ApiKey = apiKey,
            ApiKeyEnv = "OPENROUTER_API_KEY",
            Headers = AttributionHeaders,
        });
    }

    /// <summary>
    /// The Vercel AI Gateway over its OpenAI-compatible API, reading <c>AI_GATEWAY_API_KEY</c>, or without it the
    /// <c>VERCEL_OIDC_TOKEN</c> that <c>vercel env pull</c> writes for a linked project.
    /// </summary>
    public static OpenAiCompatibleModel Gateway(string model, string? apiKey = null)
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions
        {
            Model = model,
            BaseUrl = "https://ai-gateway.vercel.sh/v1",
            Provider = "gateway",
            ApiKey = apiKey,
            ApiKeyEnv = "AI_GATEWAY_API_KEY",
            ApiKeyFallbackEnv = "VERCEL_OIDC_TOKEN",
            Headers = AttributionHeaders,
        });
    }

    /// <summary>A local Ollama server's OpenAI-compatible API, no key.</summary>
    public static OpenAiCompatibleModel Ollama(string model, string baseUrl = "http://127.0.0.1:11434/v1")
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions
        {
            Model = model,
            BaseUrl = baseUrl,
            Provider = "openai-compatible",
            ApiKeyEnv = "LLM_API_KEY",
        });
    }
}
