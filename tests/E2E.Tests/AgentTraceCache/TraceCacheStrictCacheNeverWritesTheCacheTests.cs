// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.RekeyedCache;

namespace E2E.Tests.AgentTraceCache;

public sealed class TraceCacheStrictCacheNeverWritesTheCacheTests
{
    [Fact]
    public async Task Replays_on_a_retry_and_leaves_the_entry_exactly_as_recorded_when_the_first_attempt_fails()
    {
        var directory = ConfigTests.TempCache();
        await RecordAsync(directory);
        var file = Directory.GetFiles(directory).Single();
        var recorded = await File.ReadAllBytesAsync(file);

        var calls = 0;
        var results = new List<ActResult>();
        foreach (var attempt in new[] { 1, 2 })
        {
            await using var session = await ConfigTests.StartAsync(directory, CacheMode.ReadWrite, strict: true, "Upgrade to Pro", () => calls++, attempt: attempt);
            await session.App.OpenAsync("/settings/billing");
            results.Add(await session.Agent.ActAsync("upgrade the workspace to the Pro plan"));
            session.Complete(attempt == 1 ? new InvalidOperationException("the first attempt fails before anything verified the step") : null);
        }

        Assert.Equal(0, calls);
        Assert.All(results, result =>
        {
            Assert.Equal("self-finalized", result.Cache?.Mode);
            Assert.Equal(1, result.Cache?.ReplayedActions);
            Assert.Equal(1, result.Cache?.TotalActions);
        });
        Assert.Equal(file, Directory.GetFiles(directory).Single());
        Assert.Equal(recorded, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task Runs_a_never_recorded_step_live_without_recording_it()
    {
        var directory = ConfigTests.TempCache();

        var calls = 0;
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadWrite, strict: true, "Upgrade to Pro", () => calls++))
        {
            await ConfigTests.UpgradeAsync(session);
            session.Complete();
        }

        Assert.True(calls > 0);
        Assert.False(Directory.Exists(directory));
    }
}
