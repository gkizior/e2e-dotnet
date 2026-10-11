// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentConfig;

// Upstream's maxObservationBytes and maxInputTokens bounds have no port: the keys are rejected (see COMPATIBILITY.md, Agents).
// Its removed-key hint for agents.<name>.timeout has no port either: an unknown key is rejected as one.
public sealed class AgentConfigDefaultsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-agent-config-project");

    [Fact]
    public void Passes_agent_providerOptions_through_untouched_defaulting_to_none()
    {
        Assert.Null(Parse("{}").Agent.ProviderOptions);

        var config = Parse("""
            { "agents": { "default": { "providerOptions": { "openai": { "reasoningEffort": "low" }, "google": { "thinkingConfig": { "thinkingBudget": 0 } } } } } }
            """);

        Assert.Equal("low", config.Agent.ProviderOptions!["openai"].GetProperty("reasoningEffort").GetString());
        Assert.Equal(0, config.Agent.ProviderOptions["google"].GetProperty("thinkingConfig").GetProperty("thinkingBudget").GetInt32());
        Assert.Same(config.Agent.ProviderOptions, config.Agent.CreateOptions().ProviderOptions);
    }

    [Fact]
    public void Rejects_agent_providerOptions_that_is_not_a_record_of_provider_records()
    {
        Assert.Contains("agents.default.providerOptions must be an object", Invalid("""{ "agents": { "default": { "providerOptions": "low" } } }"""), StringComparison.Ordinal);
        Assert.Contains("agents.default.providerOptions", Invalid("""{ "agents": { "default": { "providerOptions": [] } } }"""), StringComparison.Ordinal);
        Assert.Contains("agents.default.providerOptions.openai must be an object", Invalid("""{ "agents": { "default": { "providerOptions": { "openai": "low" } } } }"""), StringComparison.Ordinal);
    }

    [Fact]
    public void Bounds_maxSteps_and_maxModelCalls_to_1_through_100()
    {
        Assert.Contains("maxSteps", Invalid("""{ "agents": { "default": { "maxSteps": 0 } } }"""), StringComparison.Ordinal);
        Assert.Contains("maxSteps", Invalid("""{ "agents": { "default": { "maxSteps": 101 } } }"""), StringComparison.Ordinal);
        Assert.Contains("maxModelCalls", Invalid("""{ "agents": { "default": { "maxModelCalls": 101 } } }"""), StringComparison.Ordinal);
    }

    [Fact]
    public void Owns_the_judgment_budget_judgmentTimeout_is_per_agent_and_independent_of_actionTimeout()
    {
        var config = Parse("""{ "actionTimeout": 90000, "agents": { "default": {}, "slow": { "judgmentTimeout": 120000 } } }""");

        Assert.Equal(TimeSpan.FromSeconds(30), config.Agent.JudgmentTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), config.Agents["slow"].JudgmentTimeout);
        Assert.Equal(TimeSpan.FromSeconds(90), config.ActionTimeout);
        Assert.Contains("agents.default.judgmentTimeout must be a positive", Invalid("""{ "agents": { "default": { "judgmentTimeout": 0 } } }"""), StringComparison.Ordinal);
        Assert.Contains("agents.default.judgmentTimeout", Invalid("""{ "agents": { "default": { "judgmentTimeout": 1.5 } } }"""), StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_unknown_agent_keys_with_the_nearest_one_including_the_removed_cache_mode()
    {
        Assert.Contains("unknown agents.default key", Invalid("""{ "agents": { "default": { "retries": 2 } } }"""), StringComparison.Ordinal);
        Assert.Contains("unknown agents.default key", Invalid("""{ "agents": { "default": { "cache": "always" } } }"""), StringComparison.Ordinal);
        Assert.Equal(
            "unknown agents.default key \"sytem\"; did you mean \"system\"?",
            Invalid("""{ "agents": { "default": { "sytem": "Be careful." } } }"""));
    }

    [Fact]
    public void Rejects_context_larger_than_the_agent_context_limit()
    {
        var json = "{ \"agents\": { \"default\": { \"context\": \"" + new string('a', 16_385) + "\" } } }";

        Assert.Equal("agents.default.context is 16385 bytes; the maximum is 16384", Invalid(json));
        Assert.Equal(16_384, Parse(json.Replace(new string('a', 16_385), new string('a', 16_384), StringComparison.Ordinal)).Agent.Context!.Length);
        Assert.Equal("be terse", Parse("""{ "agents": { "default": { "context": "be terse" } } }""").Agent.Context);
        Assert.Equal("agents.default.context must be a string", Invalid("""{ "agents": { "default": { "context": 5 } } }"""));
    }

    private static E2EConfig Parse(string json) => E2EConfig.Parse(json, Root, _ => null);

    private static string Invalid(string json)
    {
        var error = Assert.Throws<ConfigurationException>(() => Parse(json));
        Assert.Equal("INVALID_CONFIG", error.Code);
        return error.Message;
    }
}
