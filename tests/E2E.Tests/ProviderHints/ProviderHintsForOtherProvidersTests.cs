// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.ProviderHints.HintedBodies;

namespace E2E.Tests.ProviderHints;

/// <summary>
/// The "azure.chat my-deployment" and "undefined model" rows are not ported: the port has no Azure chat client (Azure uses the
/// Responses API), and no model reference object that can be missing.
/// </summary>
public sealed class ProviderHintsForOtherProvidersTests
{
    /// <summary>
    /// Partial port of "changes nothing for %j": the port has no hints object, so each row checks that its chat client adds
    /// no hint fields and passes the options of its own provider through.
    /// </summary>
    [Theory]
    [InlineData("gateway", "google/gemini-3-flash")]
    [InlineData("openai", "gpt-4o")]
    [InlineData("openai-proxy", "openai/gpt-4o")]
    [InlineData("mock-provider", "mock-model")]
    public async Task Adds_no_hint_fields_and_passes_the_providers_options_through_for(string provider, string model)
    {
        var body = await ChatAsync(
            new OpenAiCompatibleModelOptions { Model = model, Provider = provider, ApiKey = "k" },
            Request(providerOptions: Options(provider, """{ "thinkingConfig": {} }""")));

        Assert.Equal(["model", "messages", "tools", "tool_choice", "thinkingConfig"], body.Select(field => field.Key));
    }
}
