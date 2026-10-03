// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.NUnit;

namespace E2E.NUnit.Tests;

public sealed class SoftPollTests : E2ETest
{
    private readonly List<TestException> _soft = [];

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

    protected override void RecordSoftFailure(TestException failure) => _soft.Add(failure);

    [Test]
    public async Task Soft_failures_reach_the_fixture_and_the_body_runs_on()
    {
        await App.OpenAsync("https://billing.test/");
        await Expect.Soft(Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
        await Expect.Soft(Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync();

        Assert.That(_soft, Has.Count.EqualTo(1));
        Assert.That(_soft[0].Code, Is.EqualTo("ASSERTION_FAILED"));
    }

    [Test]
    public async Task Poll_takes_an_NUnit_constraint()
    {
        var reads = 0;
        await Expect.Poll(() => ++reads, new PollOptions { Interval = TimeSpan.FromMilliseconds(5) }).ToMatchAsync(Is.GreaterThan(2));
        Assert.That(reads, Is.EqualTo(3));

        var error = await Assert.ThrowsAsync<TestException>(() =>
            Expect.Poll(() => 1, new PollOptions { Timeout = TimeSpan.FromMilliseconds(50) }).ToMatchAsync(Is.GreaterThan(2)));
        Assert.That(error!.Message, Does.EndWith("last: received 1, which does not satisfy greater than 2"));
    }
}
