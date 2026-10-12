// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.StepCache;

public sealed class FailedStepOutcomeTests
{
    /// <summary>
    /// Partial port of "reads a cancellation and a model that never answered as no verdict, and every other failure as failed":
    /// the port's cache keeps its entry on an unavailable model, a failed provider, or a stale replay, and has no cancellation code.
    /// The first two are the upstream no-verdict codes, and a stale replay is the port's own.
    /// </summary>
    [Fact]
    public void Reads_a_model_that_never_answered_as_no_verdict_and_every_other_failure_as_failed()
    {
        foreach (var code in new[] { "MODEL_PROVIDER_FAILED", "MODEL_UNAVAILABLE", "REPLAY_STALE" })
        {
            Assert.True(AttemptScope.KeepsCache(new AgentException(code, "x")), code);
        }

        foreach (var code in new[] { "ASSERTION_FAILED", "STEP_TIMEOUT", "MODEL_OUTPUT_INVALID", "CONTEXT_OVERFLOW", "APP_UNREACHABLE" })
        {
            Assert.False(AttemptScope.KeepsCache(new AgentException(code, "x")), code);
        }

        Assert.False(AttemptScope.KeepsCache(new InvalidOperationException("MODEL_PROVIDER_FAILED")));
    }
}
