// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using static E2E.Tests.RekeyedCache;

namespace E2E.Tests.TraceCacheReplay;

public sealed class TraceCacheStrictCacheFailsAStepWhoseKeyChangedUnderItsRecordingTests
{
    // The agent's context is part of every key, so changing it re-keys every recorded step.
    private const string Rekeyed = "The workspace starts on the free plan.";

    [Fact]
    public async Task Fails_the_step_with_REPLAY_STALE_naming_the_old_entry_without_a_model_call()
    {
        var (directory, oldEntry) = await RecordedAsync();

        var calls = 0;
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadWrite, strict: true, "Upgrade to Pro", () => calls++, context: Rekeyed))
        {
            await session.App.OpenAsync("/settings/billing");
            var error = await Assert.ThrowsAsync<AgentException>(() => session.Agent.ActAsync("upgrade the workspace to the Pro plan"));
            Assert.Equal("REPLAY_STALE", error.Code);
            Assert.Contains("sits under another cache key (" + oldEntry + ")", error.Message, StringComparison.Ordinal);
            Assert.EndsWith("re-record it without cache.strict.", error.Message, StringComparison.Ordinal);
            Assert.Equal(1, session.Missed);
            session.Complete(error);
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Runs_the_step_live_without_the_flag_and_a_strict_run_then_replays_the_new_recording()
    {
        var (directory, _) = await RecordedAsync();

        var calls = 0;
        var lenient = await UpgradeAsync(directory, strict: false, () => calls++, Rekeyed);
        Assert.True(calls > 0);
        Assert.Equal("missed", lenient.Cache?.Mode);
        Assert.Equal("no-entry", lenient.Cache?.Reason);
        Assert.Equal(2, Directory.GetFiles(directory).Length);

        calls = 0;
        var strict = await UpgradeAsync(directory, strict: true, () => calls++, Rekeyed);
        Assert.Equal(0, calls);
        Assert.Equal("self-finalized", strict.Cache?.Mode);
        Assert.Equal(0, strict.ModelCalls);
        Assert.Equal(1, strict.Actions);
    }

    // Upstream's beforeAll: records the step, checks its provenance, lets a read-write replay
    // complete an entry from before the occurrence fields, and returns the directory and the entry's file name.
    private static async Task<(string Directory, string File)> RecordedAsync()
    {
        var directory = ConfigTests.TempCache();
        var recorded = await RecordAsync(directory);
        Assert.Matches("^[a-f0-9]{64}$", recorded.ParamsDigest);
        Assert.Equal(0, recorded.CallIndex);
        Assert.Equal("default", recorded.Agent);

        var cache = new FileStepCache(directory);
        var key = Path.GetFileNameWithoutExtension(Directory.GetFiles(directory).Single());
        var legacy = cache.Read(key).Entry!;
        legacy.Engine = null;
        legacy.ParamsDigest = null;
        legacy.CallIndex = null;
        legacy.Agent = null;
        cache.Write(key, legacy);
        var replayed = await UpgradeAsync(directory, strict: false, () => { });
        Assert.Equal("self-finalized", replayed.Cache?.Mode);
        Assert.Equal(0, replayed.ModelCalls);
        Assert.Equal(JsonSerializer.Serialize(recorded), JsonSerializer.Serialize(cache.Read(key).Entry));

        return (directory, key + ".json");
    }

    // Runs the upgrade step in read-write mode until it passes and returns the act's result.
    private static async Task<ActResult> UpgradeAsync(string directory, bool strict, Action onAct, string? context = null)
    {
        await using var session = await ConfigTests.StartAsync(directory, CacheMode.ReadWrite, strict, "Upgrade to Pro", onAct, context: context);
        await session.App.OpenAsync("/settings/billing");
        var result = await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
        await session.Agent.AssertAsync("the invoice preview shows a prorated amount");
        session.Complete();
        return result;
    }
}
