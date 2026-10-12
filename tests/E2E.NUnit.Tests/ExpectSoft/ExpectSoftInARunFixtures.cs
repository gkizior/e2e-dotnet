// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.NUnit.Tests.ExpectSoft;

/// <summary>The suite of upstream's <c>expect-soft.test.ts</c>, as an NUnit body would write it.</summary>
[Explicit("Run by ExpectSoftInARunTests")]
public sealed class SoftSuiteFixture : E2ETest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """{ "cache": { "mode": "off" } }""",
        AppContext.BaseDirectory,
        _ => null);

    private static readonly TimeSpan SoftTimeout = TimeSpan.FromMilliseconds(200);

    protected override E2EConfig Config => FixtureConfig;

    protected override string? BaseUrl => E2E.Playground.Testbed.Url;

    [SetUp]
    public async Task OpenBrowserPageAsync()
    {
        await App.OpenAsync("/browser");
    }

    [Test]
    public async Task Soft_failures_let_the_body_run_on()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        await Expect.Soft(Screen.GetByLabel("Loads")).ToBeHiddenAsync(SoftTimeout);
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("loads: 1", timeout: SoftTimeout);
        await Expect.That(Screen.GetByLabel("Loads")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_body_error_stays_primary_over_the_soft_failures()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        throw new InvalidOperationException("the body gave up");
    }

    [Test]
    [CancelAfter(1500)]
    public async Task A_timed_out_body_still_settles_its_soft_failures()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        await Task.Delay(Timeout.Infinite, global::NUnit.Framework.TestContext.CurrentContext.CancellationToken);
    }
}
