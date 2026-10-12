// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;
using static E2E.Tests.ProviderHints.HintedBodies;

namespace E2E.Tests.ProviderHints;

public sealed class ProviderHintsForChatCompletionsProvidersTests
{
    /// <summary>
    /// Partial port of "adds nothing to the body of an OpenAI-compatible provider named %s": upstream compares the whole
    /// body, which here carries the port's own fields, so the test checks that the hint fields are absent and nothing else is added.
    /// </summary>
    [Theory]
    [InlineData("openai")]
    [InlineData("openai-proxy")]
    public async Task Adds_no_hint_fields_to_the_body_of_an_OpenAI_compatible_provider_named(string name)
    {
        var body = await ChatAsync(new OpenAiCompatibleModelOptions { Model = "m", Provider = name, ApiKey = "k" });

        Assert.Equal(["model", "messages", "tools", "tool_choice"], body.Select(field => field.Key));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("openai-eu")]
    public async Task Keeps_the_cache_key_on_an_OpenAI_Responses_model_named(string name)
    {
        var body = await ResponsesAsync(new OpenAiResponsesModelOptions { Model = "gpt-6-luna", Provider = name, ApiKey = "k" });

        Assert.Equal((ModelHttp.PromptCacheKey("rules"), false), (body["prompt_cache_key"]!.GetValue<string>(), body["store"]!.GetValue<bool>()));
    }
}
