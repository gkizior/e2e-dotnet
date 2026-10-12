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
/// observation failure", and "fails an agent call when a non-retryable observation failure repeats". Also not ported:
/// "reports the step timeout when an observation outlives the deadline that bounded it". <c>WaitFor</c> turns only a
/// cancelled observation into <c>STEP_TIMEOUT</c>, so an engine that fails after the deadline ends the wait with its own
/// <c>ENGINE_FAILURE</c>.
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
}
