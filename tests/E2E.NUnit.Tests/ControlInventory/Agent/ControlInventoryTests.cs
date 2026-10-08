// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.NUnit.Tests.Testbed;

namespace E2E.NUnit.Tests.ControlInventory.Agent;

/// <summary>
/// The agent verbs on the control inventory, against a real model, ported from upstream's
/// <c>apps/web-benchmark/tests-agent/control-inventory.e2e.ts</c>. One step per verb, so a
/// red test names the verb, and a locator check pins each outcome. Needs
/// <c>e2e login github-copilot</c>.
/// </summary>
[Category("RealModel")]
public sealed class ControlInventoryTests : ControlInventoryTest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """
        {
          "cache": { "mode": "off" },
          "agents": { "default": { "model": "claude-sonnet-5.5", "provider": "copilot" } }
        }
        """,
        AppContext.BaseDirectory);

    protected override E2EConfig Config => FixtureConfig;

    [Test]
    public async Task Check_picks_a_radio_the_pick_replaces_with_its_summary()
    {
        await Agent.ActAsync("pick Express delivery");
        await Expect.That(Screen.GetByLabel("Delivery state")).ToHaveTextAsync("Express");
    }
}
