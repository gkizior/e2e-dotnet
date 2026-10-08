// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentTraceCache;

/// <summary>Upstream runs a test project through the runner. The port runs the same test body in a session.</summary>
public sealed class TraceCacheUnconfirmedTracesAreWithheldAndPoisonedEntriesEvictedTests
{
    [Fact]
    public async Task Teardown_steps_passing_after_the_failure_cannot_confirm_the_implicated_trace()
    {
        var directory = BillingSession.TempCache();
        var failed = false;
        await using (var session = await BillingSession.StartAsync(directory, () => { }, attempt: 1, () => failed))
        {
            await session.App.OpenAsync("/settings/billing");
            await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
            failed = true;

            // The teardown passes after the body failed.
            await session.Agent.AssertAsync("the invoice preview shows a prorated amount");
            await Expect.That(session.Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
            session.Complete(new TestException("TEST_FAILED", "the body failed before teardown"));
        }

        Assert.Equal(0, BillingSession.CachedEntries(directory));
    }

    [Fact]
    public async Task Evicts_a_previously_good_entry_once_its_flow_is_implicated_in_a_failure()
    {
        var directory = BillingSession.TempCache();
        await BillingSession.RecordAsync(directory);
        Assert.Single(BillingSession.Entries(directory));

        // Same instruction, stricter assertion: the replay finishes the step, the assertion fails.
        var calls = 0;
        await using (var session = await BillingSession.StartAsync(directory, () => calls++, attempt: 1))
        {
            await session.App.OpenAsync("/settings/billing");
            await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
            var error = await Assert.ThrowsAsync<TestException>(() => Expect.That(session.Screen.GetByRole("status", "Enterprise")).ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(100)));
            session.Complete(error);
        }

        Assert.Equal(0, calls);
        Assert.Empty(BillingSession.Entries(directory));
    }
}
