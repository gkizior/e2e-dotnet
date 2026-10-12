// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;
using static E2E.Tests.ProviderHints.HintedBodies;

namespace E2E.Tests.ProviderHints;

/// <summary>"does not touch the messages" is not ported: the OpenAI-shaped clients have no message-level hints to leave alone.</summary>
public sealed class ProviderHintsForOpenAiShapedProvidersTests
{
    private static OpenAiCompatibleModelOptions Gateway() => new() { Model = "openai/gpt-5.6-luna-fast", Provider = "gateway", ApiKey = "k" };

    [Fact]
    public async Task Routes_with_a_key_derived_from_the_system_prompt_stores_nothing_and_lets_the_caller_override_both()
    {
        var plain = await ChatAsync(Gateway());
        Assert.Equal(ModelHttp.PromptCacheKey("rules"), plain["prompt_cache_key"]!.GetValue<string>());
        Assert.False(plain["store"]!.GetValue<bool>());

        var effort = await ChatAsync(Gateway(), Request(providerOptions: Options("gateway", """{ "reasoningEffort": "low" }""")));
        Assert.Equal((ModelHttp.PromptCacheKey("rules"), false, "low"), (effort["prompt_cache_key"]!.GetValue<string>(), effort["store"]!.GetValue<bool>(), effort["reasoning_effort"]!.GetValue<string>()));

        var mine = await ChatAsync(Gateway(), Request(providerOptions: Options("gateway", """{ "prompt_cache_key": "mine", "store": true }""")));
        Assert.Equal(("mine", true), (mine["prompt_cache_key"]!.GetValue<string>(), mine["store"]!.GetValue<bool>()));

        var responses = await ResponsesAsync(new OpenAiResponsesModelOptions { Model = "gpt-4o", ApiKey = "k" }, Request(providerOptions: Options("openai", """{ "prompt_cache_key": "mine", "store": true }""")));
        Assert.Equal(("mine", true), (responses["prompt_cache_key"]!.GetValue<string>(), responses["store"]!.GetValue<bool>()));

        Assert.Equal(ModelHttp.PromptCacheKey("rules"), ModelHttp.PromptCacheKey("rules"));
        Assert.NotEqual(ModelHttp.PromptCacheKey("rules"), ModelHttp.PromptCacheKey("other rules"));
    }

    /// <summary>
    /// Partial port of "keys the same options as the AI SDK does: openai directly, azure for Azure OpenAI": the port reads the
    /// options of the provider the client names, and the test checks the Responses client with <c>openai</c> and with <c>azure</c>.
    /// </summary>
    [Fact]
    public async Task Reads_the_provider_options_of_the_provider_it_names_openai_directly_and_azure_for_Azure_OpenAI()
    {
        var direct = await ResponsesAsync(new OpenAiResponsesModelOptions { Model = "gpt-4o", ApiKey = "k" });
        var azure = await ResponsesAsync(
            new OpenAiResponsesModelOptions { Model = "my-deployment", Provider = "azure", ApiKey = "k" },
            Request(providerOptions: Options("azure", """{ "service_tier": "flex" }""")));

        Assert.Equal((ModelHttp.PromptCacheKey("rules"), false), (direct["prompt_cache_key"]!.GetValue<string>(), direct["store"]!.GetValue<bool>()));
        Assert.Equal((ModelHttp.PromptCacheKey("rules"), false, "flex"), (azure["prompt_cache_key"]!.GetValue<string>(), azure["store"]!.GetValue<bool>(), azure["service_tier"]!.GetValue<string>()));
        Assert.Equal("rules", azure["instructions"]!.GetValue<string>());
    }
}
