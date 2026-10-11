// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentConfig;

// Upstream's executor cases have no port: a custom executor is not ported (see COMPATIBILITY.md, Agents).
public sealed class JudgeModelTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-agent-config-project");

    [Fact]
    public void Is_the_model_unless_one_is_configured_so_judgments_always_have_one_rule()
    {
        var withModel = E2EConfig.Parse("""{ "agents": { "default": { "model": "gpt-4.1-mini" } } }""", Root, _ => null);
        var resolved = ResolvedAgent.From("default", withModel.Agent.CreateOptions());
        Assert.Equal("gpt-4.1-mini", resolved.Judge!.Name);
        Assert.Same(resolved.Model, resolved.Judge);

        Assert.Null(ResolvedAgent.From("default", E2EConfig.Parse("{}", Root, _ => null).Agent.CreateOptions()).Judge);
    }

    [Fact]
    public void Resolves_agent_judge_apart_from_agent_model()
    {
        var config = E2EConfig.Parse("""{ "agents": { "default": { "model": "gpt-4.1-mini", "judge": "o4-mini" } } }""", Root, _ => null);
        var resolved = ResolvedAgent.From("default", config.Agent.CreateOptions());

        Assert.Equal("gpt-4.1-mini", resolved.Model!.Name);
        Assert.Equal("o4-mini", resolved.Judge!.Name);
        Assert.Equal("o4-mini", config.Agent.Judge);
    }
}
