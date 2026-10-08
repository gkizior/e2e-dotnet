// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.XUnit.V3;

namespace E2E.XUnit.V3.Tests;

public sealed class SoftFailureTests : E2ETest
{
    private bool _bodyRanOn;

    protected override IEngine CreateEngine()
    {
        return new DocumentEngine(new DocumentWorld().Map("/", page =>
        {
            page.Heading("Billing");
            page.Status("Pro", hidden: true);
        }));
    }

    protected override CacheMode CacheMode => CacheMode.Off;

    protected override TimeSpan AssertionTimeout => TimeSpan.FromMilliseconds(100);

    // The fixture throws the soft failures when the test is disposed. This test expects that throw, so it catches it here.
    public override async ValueTask DisposeAsync()
    {
        var error = await Assert.ThrowsAsync<TestException>(async () => await base.DisposeAsync());
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.True(_bodyRanOn);
    }

    [Fact]
    public async Task Soft_failures_fail_the_test_when_it_is_disposed()
    {
        await App.OpenAsync("https://billing.test/");
        await Expect.Soft(Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
        await Expect.Soft(Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync();
        _bodyRanOn = true;
    }
}
