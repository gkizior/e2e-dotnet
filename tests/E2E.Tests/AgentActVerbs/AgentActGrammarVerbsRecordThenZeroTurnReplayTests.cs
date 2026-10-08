// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using static E2E.Tests.AgentActVerbs.GesturesSession;

namespace E2E.Tests.AgentActVerbs;

/// <summary>
/// <c>agent.act</c> grammar verbs recorded on a first run, then replayed with no model call.
/// Upstream's replay pass also runs hover, drag, upload, and two repeated-press flows. This
/// port has no hover, drag, or upload tool, and its press tool takes no repeat count.
/// The engine-name and policy-event lines are not ported: the port has no engine or policy events.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class AgentActGrammarVerbsRecordThenZeroTurnReplayTests(AgentActGrammarVerbsRecordThenZeroTurnReplayTests.Runs runs)
    : IClassFixture<AgentActGrammarVerbsRecordThenZeroTurnReplayTests.Runs>
{
    private const string RoundTrip = "opens a page and comes back";

    [Fact]
    public void Records_every_verb_on_the_first_run()
    {
        foreach (var flow in ReplayFlows)
        {
            var cache = runs.First[flow.Title].Cache;
            Assert.NotNull(cache);
            Assert.Equal(("missed", "no-entry"), (cache.Mode, cache.Reason));
        }
    }

    [Fact]
    public void Replays_hover_drag_check_upload_and_scroll_into_view_without_a_model_call_and_runs_a_round_trip_that_changed_nothing_live()
    {
        // Every second-run flow passed its check: RunFlowAsync fails the fixture otherwise.
        // A round trip leaves the screen and the route as it found them: nothing a
        // replay could check, so it is never recorded and the model runs it again.
        var roundTrip = runs.Second[RoundTrip];
        Assert.Equal(("missed", "no-entry"), (roundTrip.Cache?.Mode, roundTrip.Cache?.Reason));
        Assert.Equal(runs.Second.Values.Sum(result => result.ModelCalls), roundTrip.ModelCalls);
        foreach (var flow in ReplayFlows.Where(entry => entry.Title != RoundTrip))
        {
            var step = runs.Second[flow.Title];
            Assert.Equal("self-finalized", step.Cache?.Mode);
            Assert.Equal(0, step.ModelCalls);
        }
    }

    /// <summary>Both passes over the replay flows, sharing one cache: the first records, the second replays.</summary>
    public sealed class Runs : IAsyncLifetime
    {
        public string CacheDirectory { get; } = Path.Combine(Path.GetTempPath(), "e2e-verbs", Guid.NewGuid().ToString("n"));

        public Dictionary<string, ActResult> First { get; } = [];

        public Dictionary<string, ActResult> Second { get; } = [];

        public async Task InitializeAsync()
        {
            using var site = await StartSiteAsync();
            foreach (var flow in ReplayFlows)
            {
                First[flow.Title] = await RunFlowAsync(site, flow, ModelFor(flow), CacheDirectory);
            }

            foreach (var flow in ReplayFlows)
            {
                Second[flow.Title] = await RunFlowAsync(site, flow, ModelFor(flow), CacheDirectory);
            }
        }

        public Task DisposeAsync() => Task.CompletedTask;
    }
}
