// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentConfig;

// Upstream's --agent flag and configDigest cases have no port: the port picks an agent on each call, and has no digest
// (see COMPATIBILITY.md, Agents). Upstream's "did you mean" hint for an unknown agent has no port either.
public sealed class NamedAgentsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-agent-config-project");

    [Fact]
    public void Always_has_a_default_agent_the_built_in_one_without_a_model_even_when_only_others_are_named()
    {
        var config = E2EConfig.Parse("""{ "agents": { "ux": { "model": "gpt-4.1-mini" } } }""", Root, _ => null);

        Assert.Null(config.Agent.Model);
        Assert.Null(config.Agent.CreateModel());
        Assert.Equal("gpt-4.1-mini", config.Agents["ux"].Model);
    }

    [Fact]
    public async Task Rejects_an_unknown_agent_before_anything_starts_naming_the_configured_ones()
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = new ScriptedModel(_ => throw new InvalidOperationException("not called")),
            BaseUrl = "https://billing.test",
            TestTitle = "named agents",
            Agents = new Dictionary<string, AgentOptions> { ["ux"] = new() },
        });
        await session.App.OpenAsync("/settings/billing");

        var error = await Assert.ThrowsAsync<TestException>(() => session.Agent.ActAsync("open billing", new ActOptions { Agent = "uxx" }));

        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal("unknown agent \"uxx\"; configured: default, ux", error.Message);
        session.Complete(error);
    }

    [Fact]
    public void Rejects_the_removed_agent_key_with_the_replacement_bad_names_and_a_non_object_agents()
    {
        Assert.Contains("unknown config key \"agent\"; agents are named", Invalid("""{ "agent": { "model": "gpt-4.1-mini" } }"""), StringComparison.Ordinal);
        Assert.Contains("agent name \"u x\"", Invalid("""{ "agents": { "u x": {} } }"""), StringComparison.Ordinal);
        // An agent's name is an artifact path segment, so "." and ".." are out.
        Assert.Contains("agent name \"..\"", Invalid("""{ "agents": { "..": {} } }"""), StringComparison.Ordinal);
        Assert.Contains("agent name \".\"", Invalid("""{ "agents": { ".": {} } }"""), StringComparison.Ordinal);
        Assert.True(E2EConfig.Parse("""{ "agents": { "v1.2": {} } }""", Root, _ => null).Agents.ContainsKey("v1.2"));
        Assert.Contains("agents must be an object of agents by name", Invalid("""{ "agents": [] }"""), StringComparison.Ordinal);
    }

    private static string Invalid(string json)
    {
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(json, Root, _ => null));
        Assert.Equal("INVALID_CONFIG", error.Code);
        return error.Message;
    }
}
