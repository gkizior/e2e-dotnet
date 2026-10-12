// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.EngineContract;

/// <summary>
/// Ported from upstream's <c>engine-contract.test.ts</c>, whose tests run a project through the CLI over a fake engine. The
/// port runs each body in a session over a custom <see cref="IEngine"/>. Not ported, because the port has no such feature:
/// every other test of the file needs the <c>defineEngine</c> hooks (init, dispose, endAttempt, settleAttempt), the runner
/// (retries, workers, serial suites, exit codes, run errors, plan notices), the report, artifacts, video, sessions and state,
/// engine-contributed fixtures, or the <c>requires</c> test option. Also not ported, with a feature gap: "names
/// APP_UNREACHABLE, with the URL, when the network refused the navigation", "re-observes when an engine reports a retryable
/// observation failure", and "fails an agent call when a non-retryable observation failure repeats".
/// </summary>
public sealed class RunnerEngineContractTests
{
    [Fact]
    public async Task Maps_a_hung_startAttempt_to_an_environment_unavailable_error_and_tells_the_hook_to_stop()
    {
        // Partial: the port names the code ENVIRONMENT_UNAVAILABLE, not LAUNCH_TIMEOUT, and a failed launch has no
        // session to end, so there is no attemptsEnded count to check. The category, the retry and the report are the runner's.
        var engine = new HangingEngine();
        var error = await Assert.ThrowsAsync<EngineException>(() => E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = engine,
            TestTitle = "launch hang",
            LaunchTimeout = TimeSpan.FromMilliseconds(50),
        }));
        Assert.Equal("ENVIRONMENT_UNAVAILABLE", error.Code);
        Assert.Contains("launch timeout", error.Message, StringComparison.Ordinal);
        Assert.True(engine.Stopped);
    }

    [Fact]
    public async Task Reaching_a_fixture_the_engine_does_not_contribute_is_a_configuration_error()
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            BaseUrl = "https://billing.test",
            TestTitle = "needs browser",
        });
        await session.App.OpenAsync("/settings/billing");
        var error = await Assert.ThrowsAsync<EngineException>(() => session.Browser.GotoAsync("/somewhere"));
        Assert.Equal("UNSUPPORTED_CAPABILITY", error.Code);
        Assert.Contains("browser.goto needs a browser engine", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_the_step_timeout_when_an_observation_outlives_the_deadline_that_bounded_it()
    {
        // An observation is handed whatever remains of the invocation deadline, so one starting near the end cannot
        // finish. The engine failure that follows describes a truncated budget, not a broken app. Only the second
        // observation stalls, so the first one reads at once.
        var timeout = TimeSpan.FromMilliseconds(300);
        var engine = new StallingEngine(new DocumentEngine(BillingWorld.Create()), TimeSpan.FromMilliseconds(50));
        var model = new ScriptedModel(_ => ModelResponses.Done("failed", "the Submit button is disabled"));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = engine,
            Model = model,
            BaseUrl = "https://billing.test",
            TestTitle = "observe deadline",
            TestTimeout = TimeSpan.FromSeconds(10),
        });

        Exception? error = null;
        try
        {
            await session.App.OpenAsync("/settings/billing");
            await session.Agent.WaitForAsync("the Submit button is enabled", new WaitForOptions
            {
                Timeout = timeout,
                Interval = TimeSpan.FromMilliseconds(100),
            });
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        var failure = Assert.IsType<AgentException>(error);
        Assert.Equal("STEP_TIMEOUT", failure.Code);
        Assert.Contains("the Submit button is disabled", failure.Message, StringComparison.Ordinal);
    }

    internal sealed class HangingEngine : IEngine
    {
        private CancellationToken _token;

        public bool Stopped => _token.IsCancellationRequested;

        public string Platform => "web";

        public string Version => "1.0";

        public EngineCapabilities Capabilities => EngineCapabilities.None;

        public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
        {
            _token = cancellationToken;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    /// <summary>Wraps an engine so that the second observation fails 50 ms after its token is cancelled.</summary>
    private sealed class StallingEngine(IEngine inner, TimeSpan stall) : IEngine
    {
        public string Platform => inner.Platform;

        public string Version => inner.Version;

        public EngineCapabilities Capabilities => inner.Capabilities;

        public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken) =>
            new Session(await inner.StartAsync(options, cancellationToken).ConfigureAwait(false), stall);

        private sealed class Session(IEngineSession inner, TimeSpan stall) : IEngineSession
        {
            private int _observations;

            public string Route => inner.Route;

            public Task OpenAsync(string url, CancellationToken cancellationToken) => inner.OpenAsync(url, cancellationToken);

            public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
            {
                if (Interlocked.Increment(ref _observations) != 2)
                {
                    return await inner.ObserveAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await Task.Delay(stall, CancellationToken.None).ConfigureAwait(false);
                }

                throw new EngineException("ENGINE_FAILURE", "observation ran out of budget");
            }

            public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) =>
                inner.PerformAsync(node, action, cancellationToken);

            public Task PressAsync(string key, CancellationToken cancellationToken) => inner.PressAsync(key, cancellationToken);

            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }
}
