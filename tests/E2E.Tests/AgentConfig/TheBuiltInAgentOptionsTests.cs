// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentConfig;

// Upstream's defineTool cases have no port: project tools are not ported (see COMPATIBILITY.md, Agents). Neither are
// the tool names it reserves, a model instance as the entry, or the digest of tools.
public sealed class TheBuiltInAgentOptionsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-agent-config-project");

    [Fact]
    public void Keeps_system_and_rejects_one_that_is_not_a_string()
    {
        var config = E2EConfig.Parse("""{ "agents": { "default": { "system": "Verify every total." } } }""", Root, _ => null);

        Assert.Equal("Verify every total.", config.Agent.System);
        Assert.Equal("Verify every total.", config.Agent.CreateOptions().System);
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "agents": { "default": { "system": 5 } } }""", Root, _ => null));
        Assert.Equal("agents.default.system must be a string", error.Message);
    }
}
