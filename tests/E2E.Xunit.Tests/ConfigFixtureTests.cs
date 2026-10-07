// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.Xunit;

namespace E2E.Xunit.Tests;

public sealed class ConfigFixtureTests : E2ETest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """
        {
          "targets": [{ "app": { "url": "https://billing.test" } }],
          "actionTimeout": 2000,
          "cache": { "mode": "off" },
          "agents": {
            "default": { "maxSteps": 7, "judgmentTimeout": 9000, "context": "Billing is under Settings." },
            "careful": { "model": "gpt-4.1", "judge": "o4-mini", "maxModelCalls": 4 }
          },
          "secrets": { "admin-token": "token-from-config" }
        }
        """,
        Path.GetTempPath(),
        _ => null);

    protected override E2EConfig Config => FixtureConfig;

    protected override IEngine CreateEngine()
    {
        return new DocumentEngine(new DocumentWorld().Map("/settings/billing", page => page.Heading("Billing")));
    }

    [Fact]
    public async Task The_fixture_applies_the_config()
    {
        Assert.Equal("https://billing.test", BaseUrl);
        Assert.Equal(CacheMode.Off, CacheMode);
        Assert.Equal(TimeSpan.FromSeconds(2), ActionTimeout);
        Assert.Equal("admin-token", Secrets.Get("admin-token").Name);
        Assert.Equal(7, MaxSteps);
        Assert.Equal(TimeSpan.FromSeconds(9), JudgmentTimeout);
        Assert.Equal("Billing is under Settings.", AgentContext);
        var agents = CreateAgents();
        Assert.Equal(["careful"], agents.Keys);
        Assert.Equal("gpt-4.1", agents["careful"].Model!.Name);
        Assert.Equal("o4-mini", agents["careful"].Judge!.Name);
        Assert.Equal(4, agents["careful"].MaxModelCalls);

        await App.OpenAsync("/settings/billing", Context.CancellationToken);
        await Expect.That(Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync(cancellationToken: Context.CancellationToken);
    }
}
