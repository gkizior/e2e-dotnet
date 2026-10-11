// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentConfig;

// Upstream's model instance, gateway instance, and model string cases have no port: JSON names a model by id
// (see COMPATIBILITY.md, Config).
public sealed class ModelResolutionTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-agent-config-project");

    [Fact]
    public void Has_no_implicit_model_nothing_configured_and_no_environment_variable_is_read()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["E2E_MODEL"] = "gpt-4.1-mini", ["AI_GATEWAY_API_KEY"] = "x", ["OPENAI_API_KEY"] = "x" };

        var config = E2EConfig.Parse("{}", Root, name => environment.GetValueOrDefault(name));

        Assert.Null(config.Agent.Model);
        Assert.Null(config.Agent.CreateModel());
    }
}
