// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.NUnit.Tests.Testbed;

namespace E2E.NUnit.Tests.ControlInventory;

/// <summary>
/// The control inventory through locators only, ported from upstream's
/// <c>apps/web-benchmark/tests/control-inventory.e2e.ts</c>.
/// </summary>
public sealed class ControlInventoryTests : ControlInventoryTest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """{ "cache": { "mode": "off" } }""",
        AppContext.BaseDirectory,
        _ => null);

    protected override E2EConfig Config => FixtureConfig;

    [Test]
    public async Task Check_picks_a_radio_the_pick_replaces_with_its_summary()
    {
        await Screen.GetByRole("radio", "Express").CheckAsync();
        await Expect.That(Screen.GetByLabel("Delivery state")).ToHaveTextAsync("Express");
        await Expect.That(Screen.GetByText("Express delivery selected")).ToBeVisibleAsync();
    }
}
