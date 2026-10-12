// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.NamedAgents;

/// <summary>
/// <c>agents</c> by name. Upstream pins suites to agents and picks one with <c>--agent</c>, which belong to its runner
/// and are not ported. The port names the agent on each call.
/// </summary>
public sealed class NamedAgentsTests
{
    // Upstream also reads the agent each step ran with from its report, and the error adds a did-you-mean hint.
    [Fact]
    public async Task Routes_each_call_to_the_agent_it_names_and_fails_a_call_naming_an_unknown_agent()
    {
        var house = Signing("house");
        var buyer = Signing("buyer-brain");
        var admin = Signing("admin-brain");
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(new DocumentWorld().Map("/", page => page.Heading("Home"))),
            Model = house,
            Agents = new Dictionary<string, AgentOptions>
            {
                ["buyer"] = new() { Model = buyer },
                ["admin"] = new() { Model = admin },
            },
            BaseUrl = "https://app.test",
            CacheEnabled = false,
            TestTitle = "named agents",
        });
        await session.App.OpenAsync("/");

        Assert.Equal("done by house", (await session.Agent.ActAsync("whatever")).Summary);
        Assert.Equal("done by admin-brain", (await session.Agent.ActAsync("refund", new ActOptions { Agent = "admin" })).Summary);
        Assert.Equal("done by buyer-brain", (await session.Agent.ActAsync("confirm", new ActOptions { Agent = "buyer" })).Summary);
        await session.Agent.AssertAsync("the refund shows", new AssertOptions { Agent = "buyer" });
        Assert.Equal((1, 1, 2), (house.CallCount, admin.CallCount, buyer.CallCount));

        // A call naming nothing configured fails before it reaches any model.
        var error = await Assert.ThrowsAsync<TestException>(() => session.Agent.ActAsync("x", new ActOptions { Agent = "buyr" }));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal("unknown agent \"buyr\"; configured: default, buyer, admin", error.Message);
        Assert.Equal((1, 1, 2), (house.CallCount, admin.CallCount, buyer.CallCount));
        session.Complete(error);
    }

    // A model that signs its act verdict, so the result shows which one ran. A judgment answers "ready".
    private static ScriptedModel Signing(string name)
    {
        return new ScriptedModel(request => request.Tools.Any(tool => tool.Name == "tap")
            ? ModelResponses.Done("passed", "done by " + name)
            : ModelResponses.Done("passed", "ready"));
    }
}
