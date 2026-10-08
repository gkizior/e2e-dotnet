// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.RekeyedCache;

namespace E2E.Tests.StepCache;

public sealed class CacheStrictAndAStepWhoseKeyChangedUnderItsRecordingTests
{
    [Fact]
    public async Task Fails_with_REPLAY_STALE_naming_the_old_entry_instead_of_running_the_step_live()
    {
        var directory = ConfigTests.TempCache();
        await RecordAsync(directory);
        Rekey(directory, entry => { });

        var calls = 0;
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadOnly, strict: true, "Upgrade to Pro", () => calls++))
        {
            await session.App.OpenAsync("/settings/billing");
            var error = await Assert.ThrowsAsync<AgentException>(() => session.Agent.ActAsync("upgrade the workspace to the Pro plan"));
            Assert.Equal("REPLAY_STALE", error.Code);
            Assert.Contains("sits under another cache key (" + OldKey + ".json)", error.Message, StringComparison.Ordinal);
            Assert.EndsWith("re-record it without cache.strict.", error.Message, StringComparison.Ordinal);
            Assert.Equal(1, session.Missed);
            session.Complete(error);
        }

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Runs_the_step_live_without_cache_strict()
    {
        var directory = ConfigTests.TempCache();
        await RecordAsync(directory);
        Rekey(directory, entry => { });

        var calls = 0;
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadOnly, strict: false, "Upgrade to Pro", () => calls++))
        {
            await session.App.OpenAsync("/settings/billing");
            var result = await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
            Assert.Equal("missed", result.Cache?.Mode);
            Assert.Equal("no-entry", result.Cache?.Reason);
        }

        Assert.True(calls > 0);
    }

    [Fact]
    public async Task Runs_live_against_an_entry_recorded_before_the_occurrence_fields_were_which_could_be_another_call_of_the_instruction()
    {
        var directory = ConfigTests.TempCache();
        await RecordAsync(directory);
        Rekey(directory, entry =>
        {
            entry.Engine = null;
            entry.ParamsDigest = null;
            entry.CallIndex = null;
            entry.Agent = null;
        });

        await AssertRunsLiveAsync(directory);
    }

    [Theory]
    [InlineData("instruction")]
    [InlineData("params")]
    [InlineData("occurrence")]
    [InlineData("agent")]
    [InlineData("test")]
    [InlineData("engine")]
    public async Task Runs_live_a_step_the_store_holds_no_recording_of_another_instruction_params_occurrence_agent_test_or_target(string change)
    {
        var directory = ConfigTests.TempCache();
        await RecordAsync(directory);
        Rekey(directory, entry =>
        {
            switch (change)
            {
                case "instruction":
                    entry.Instruction = "open the billing page";
                    break;
                case "params":
                    entry.ParamsDigest = CacheKeys.ParamsDigest(new Dictionary<string, object?> { ["plan"] = "pro" });
                    break;
                case "occurrence":
                    entry.CallIndex = 1;
                    break;
                case "agent":
                    entry.Agent = "admin";
                    break;
                case "test":
                    entry.Test = "billing > downgrades";
                    break;
                case "engine":
                    entry.Engine = "ios";
                    break;
            }
        });

        await AssertRunsLiveAsync(directory);
    }

    [Fact]
    public async Task Runs_live_when_the_only_recording_has_no_actions_or_records_no_step()
    {
        // Upstream also runs live on a truncated recording; the port has no truncated entries.
        Action<CacheEntry>[] changes =
        [
            entry => entry.Actions = [],
            entry =>
            {
                entry.Test = null;
                entry.Instruction = null;
                entry.Engine = null;
                entry.ParamsDigest = null;
                entry.CallIndex = null;
                entry.Agent = null;
            },
        ];
        foreach (var change in changes)
        {
            var directory = ConfigTests.TempCache();
            await RecordAsync(directory);
            Rekey(directory, change);
            await AssertRunsLiveAsync(directory);
        }
    }

    [Fact]
    public async Task Compares_the_step_as_the_entry_stores_it_a_registered_secret_in_the_title_masked_and_never_names_the_secret()
    {
        const string title = "billing > logs in with hunter2pw";
        var parameters = new Dictionary<string, object?> { ["password"] = Secret.Create("password", "hunter2pw") };
        var directory = ConfigTests.TempCache();
        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadWrite, strict: false, "Upgrade to Pro", () => { }, title))
        {
            await session.App.OpenAsync("/settings/billing");
            await session.Agent.ActAsync("upgrade the workspace to the Pro plan", new ActOptions { Params = parameters });
            await session.Agent.AssertAsync("the invoice preview shows a prorated amount");
            session.Complete();
        }

        Rekey(directory, entry => Assert.DoesNotContain("hunter2pw", entry.Test, StringComparison.Ordinal));

        await using (var session = await ConfigTests.StartAsync(directory, CacheMode.ReadOnly, strict: true, "Upgrade to Pro", () => { }, title))
        {
            await session.App.OpenAsync("/settings/billing");
            var error = await Assert.ThrowsAsync<AgentException>(() => session.Agent.ActAsync("upgrade the workspace to the Pro plan", new ActOptions { Params = parameters }));
            Assert.Equal("REPLAY_STALE", error.Code);
            Assert.DoesNotContain("hunter2pw", error.Message, StringComparison.Ordinal);
        }
    }
}
