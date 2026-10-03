// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.NUnit;

namespace E2E.NUnit.Tests;

public sealed class ConfigFixtureTests : E2ETest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """
        {
          "targets": [{ "app": { "url": "https://billing.test" } }],
          "actionTimeout": 2000,
          "cache": { "mode": "off" },
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

    [Test]
    public async Task The_fixture_applies_the_config()
    {
        Assert.That(BaseUrl, Is.EqualTo("https://billing.test"));
        Assert.That(CacheMode, Is.EqualTo(CacheMode.Off));
        Assert.That(ActionTimeout, Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(Secrets.Get("admin-token").Name, Is.EqualTo("admin-token"));

        await App.OpenAsync("/settings/billing");
        await Expect.That(Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync();
    }
}
