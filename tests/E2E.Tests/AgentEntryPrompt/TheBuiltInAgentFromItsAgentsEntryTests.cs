// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentEntryPrompt;

// Upstream's "joins the context with the test-level agentContext" has no port: the port's AgentContext replaces
// agents.default.context instead of joining it.
public sealed class TheBuiltInAgentFromItsAgentsEntryTests
{
    [Fact]
    public async Task Puts_system_and_context_in_the_act_prompt()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("passed", "done"));
        await using var session = await StartAsync(model, system: "Be careful.", context: "Plans are called tiers.");

        await session.Agent.ActAsync("open billing");

        var system = model.Requests[0].System;
        Assert.Contains("Be careful.", system, StringComparison.Ordinal);
        Assert.Contains("Project context:\nPlans are called tiers.", system, StringComparison.Ordinal);
        session.Complete();
    }

    [Fact]
    public async Task Builds_each_agent_from_its_own_entry_another_agents_system_never_leaks_in()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("passed", "done"));
        await using var session = await StartAsync(
            model,
            system: "Be careful.",
            agents: new Dictionary<string, AgentOptions> { ["ux"] = new() { Model = model, System = "Review the layout." } });

        await session.Agent.ActAsync("open billing", new ActOptions { Agent = "ux" });

        Assert.Contains("Review the layout.", model.Requests[0].System, StringComparison.Ordinal);
        Assert.DoesNotContain("Be careful.", model.Requests[0].System, StringComparison.Ordinal);
        session.Complete();
    }

    private static async Task<E2ESession> StartAsync(ScriptedModel model, string? system = null, string? context = null, Dictionary<string, AgentOptions>? agents = null)
    {
        var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            AgentSystem = system,
            AgentContext = context,
            Agents = agents,
            BaseUrl = "https://billing.test",
            TestTitle = "entry prompt",
        });
        await session.App.OpenAsync("/settings/billing");
        return session;
    }
}
