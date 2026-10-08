// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.TraceCache;

/// <summary>
/// The port decides replay inside the agent, so these act over a stored recording. "never replays a truncated trace"
/// and "skips leading gaps when checking for the navigate opener" are not ported: the port records neither truncated
/// entries nor gaps. A page always has a route in the port, so the undefined current path is not checked.
/// </summary>
public sealed class DecideTraceReplayTests
{
    [Fact]
    public async Task Replays_a_navigate_opening_trace_from_anywhere()
    {
        var directory = BillingSession.TempCache();
        var calls = 0;
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("Statement:", StringComparison.Ordinal))
            {
                return ModelResponses.Done(text.Contains("Prorated", StringComparison.Ordinal) ? "passed" : "failed", "judged", text.Contains("Prorated", StringComparison.Ordinal) ? null : "ASSERTION_FAILED");
            }

            calls++;
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "Upgraded to Pro.");
            }

            return text.Contains("navigated", StringComparison.Ordinal)
                ? ModelResponses.Tap("button", "Upgrade to Pro")
                : ModelResponses.Call("navigate", new { url = "/settings/billing" });
        });

        async Task RunAsync(string start)
        {
            await using var session = await BillingSession.StartAsync(directory, model, attempt: 1);
            await session.App.OpenAsync(start);
            await session.Agent.ActAsync("open billing and upgrade to Pro");
            await session.Agent.AssertAsync("the invoice preview shows a prorated amount");
            session.Complete();
        }

        await RunAsync("/");
        Assert.True(calls > 0);
        calls = 0;
        await RunAsync("/home");
        Assert.Equal(0, calls);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/134: a start path that differs in a record id misses")]
    public async Task Enforces_the_start_path_precondition_when_the_trace_does_not_open_with_navigate()
    {
        var entry = Entry("/settings", Tap());
        Assert.NotEqual("missed", (await ActAt("/settings", entry))?.Mode);
        Assert.NotEqual("missed", (await ActAt("/orders/43917#top", Entry("/orders/42", Tap())))?.Mode);
        var miss = await ActAt("/other", entry);
        Assert.Equal(("missed", "wrong-context"), (miss?.Mode, miss?.Reason));
    }

    [Fact]
    public async Task Requires_a_recorded_start_path_for_a_non_navigating_trace()
    {
        var miss = await ActAt("/settings", Entry(null, Tap()));
        Assert.Equal(("missed", "wrong-context"), (miss?.Mode, miss?.Reason));
    }

    private static RecordedAction Tap() => new() { Kind = "tap", Role = "button", Name = "Upgrade" };

    private static CacheEntry Entry(string? route, params RecordedAction[] actions) =>
        new() { Instruction = "upgrade the plan", Route = route, Actions = [.. actions] };

    /// <summary>Opens <paramref name="route"/>, acts with <paramref name="entry"/> as the stored recording, and returns how the cache served the step.</summary>
    private static async Task<CacheInfo?> ActAt(string route, CacheEntry entry)
    {
        var world = new DocumentWorld();
        foreach (var path in new[] { "/settings", "/other", "/orders/42", "/orders/43917" })
        {
            world.Map(path, page => page.Button("Upgrade"));
        }

        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            Model = new ScriptedModel(_ => ModelResponses.Done("passed", "upgraded")),
            BaseUrl = "https://billing.test",
            Cache = new StoredEntry(entry),
            CacheEnabled = true,
            TestTitle = "billing > upgrades",
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            ReplayTimeout = TimeSpan.FromMilliseconds(300),
        });
        await session.App.OpenAsync(route);
        var result = await session.Agent.ActAsync("upgrade the plan");
        session.Complete();
        return result.Cache;
    }

    /// <summary>A store that holds one recording under every key.</summary>
    private sealed class StoredEntry(CacheEntry entry) : IStepCache
    {
        public CacheLookup Read(string key) => new() { Entry = entry };

        public void Write(string key, CacheEntry entry)
        {
        }

        public void Delete(string key)
        {
        }
    }
}
