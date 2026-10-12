// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Mobile;

namespace MobileApp.E2ETests;

/// <summary>The counter, and the device controls a test uses around it.</summary>
public sealed class DeviceTests : MobileAppTest
{
    [Test]
    public async Task The_agent_counts_taps()
    {
        await Agent.ActAsync("press Add one three times");

        await Expect.That(Screen.GetByTestId("counter")).ToHaveTextAsync("Count: 3");
    }

    [Test]
    public async Task Keeps_the_count_after_the_app_goes_to_the_background()
    {
        await Screen.GetByTestId("increment").TapAsync();
        await Expect.That(Screen.GetByTestId("counter")).ToHaveTextAsync("Count: 1");

        await Device.HomeAsync();
        await Device.OpenAppAsync(AppId);

        await Expect.That(Screen.GetByTestId("counter")).ToHaveTextAsync("Count: 1");
        Assert.That((await Device.ForegroundAppAsync()).BundleId, Is.EqualTo(AppId));
    }

    [Test]
    public async Task Starts_again_from_zero_after_a_fresh_launch()
    {
        await Screen.GetByTestId("increment").TapAsync();

        await Device.OpenAppAsync(AppId, new OpenAppOptions { Relaunch = true });

        await Expect.That(Screen.GetByTestId("counter")).ToHaveTextAsync("Count: 0");
    }

    [Test]
    public async Task Works_in_dark_mode()
    {
        await Device.SetAppearanceAsync(Appearance.Dark);
        try
        {
            await Agent.ActAsync("get the app to greet {name}", new ActOptions
            {
                Params = new Dictionary<string, object?> { ["name"] = "Linus" },
            });

            await Expect.That(Screen.GetByTestId("greeting")).ToHaveTextAsync("Hello, Linus!");
        }
        finally
        {
            await Device.SetAppearanceAsync(Appearance.Light);
        }
    }
}
