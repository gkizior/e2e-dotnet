// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentTraceCache;

/// <summary>Upstream runs a project through the runner and reads the step from its report. The port runs the same test body in a session.</summary>
public sealed class TraceCacheAModelThatNeverAnsweredImplicatesNothingTests
{
    [Fact]
    public async Task Keeps_an_entry_replayed_whole_when_a_later_judgment_finds_no_model()
    {
        var directory = TwoTapCounter.NewDirectory();
        await RecordAsync(directory);
        var recorded = TwoTapCounter.FileState(directory);

        ActResult result;
        AgentException error;
        await using (var session = await TwoTapCounter.StartAsync(directory, TwoTapCounter.Model(), TwoTapCounter.UnreachableModel()))
        {
            result = await TwoTapCounter.RunAsync(session);
            error = await Assert.ThrowsAsync<AgentException>(() => session.Agent.AssertAsync("the counter shows 2"));
            session.Complete(error);
        }

        Assert.Equal("self-finalized", result.Cache?.Mode);
        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.Equal(recorded, TwoTapCounter.FileState(directory));
    }

    [Fact]
    public async Task Keeps_an_entry_whose_replay_handed_off_to_a_model_that_never_answered()
    {
        var directory = TwoTapCounter.NewDirectory();
        await TwoTapCounter.RecordAndSabotageAsync(directory);
        var handedOff = TwoTapCounter.FileState(directory);

        AgentException error;
        int replayed;
        int handedOffSteps;
        await using (var session = await TwoTapCounter.StartAsync(directory, TwoTapCounter.UnreachableModel()))
        {
            await session.App.OpenAsync("/");
            error = await Assert.ThrowsAsync<AgentException>(() => session.Agent.ActAsync(TwoTapCounter.Instruction));
            replayed = session.Replayed;
            handedOffSteps = session.HandedOff;
            session.Complete(error);
        }

        // The replay ran the first tap, then handed the step to a model that never answered.
        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.Equal((0, 1), (replayed, handedOffSteps));
        Assert.Equal(handedOff, TwoTapCounter.FileState(directory));
    }

    private static async Task RecordAsync(string directory)
    {
        await using var session = await TwoTapCounter.StartAsync(directory, TwoTapCounter.Model());
        await TwoTapCounter.RunAsync(session);
        session.Complete();
    }
}
