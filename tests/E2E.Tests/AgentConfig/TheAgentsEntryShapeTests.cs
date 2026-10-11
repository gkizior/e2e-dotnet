// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentConfig;

// Upstream's StepExecutor and createToolLoopExecutor cases have no port: a custom executor is not ported (see COMPATIBILITY.md, Agents).
public sealed class TheAgentsEntryShapeTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-agent-config-project");

    [Fact]
    public void Rejects_an_entry_that_is_not_an_options_object()
    {
        foreach (var json in new[] { """{ "agents": { "ux": "gpt" } }""", """{ "agents": { "ux": [] } }""" })
        {
            var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(json, Root, _ => null));

            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Equal("agents.ux must be an object", error.Message);
        }
    }

    [Fact]
    public void Gives_every_agent_the_built_in_defaults_never_another_agents_values()
    {
        var config = E2EConfig.Parse(
            """
            { "agents": { "default": { "model": "gpt-4.1-mini", "judge": "o4-mini", "system": "Be careful.", "context": "Plans are tiers.", "maxSteps": 5, "judgmentTimeout": 90000 }, "ux": {} } }
            """,
            Root,
            _ => null);

        var ux = config.Agents["ux"];
        Assert.Null(ux.Model);
        Assert.Null(ux.Judge);
        Assert.Null(ux.System);
        Assert.Null(ux.Context);
        Assert.Equal(E2EDefaults.MaxSteps, ux.MaxSteps);
        Assert.Equal(E2EDefaults.JudgmentTimeout, ux.JudgmentTimeout);
        Assert.Equal(5, config.Agent.MaxSteps);
    }
}
