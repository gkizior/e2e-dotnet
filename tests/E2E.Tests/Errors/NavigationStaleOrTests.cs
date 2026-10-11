// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Errors;

/// <summary>A read that raced a navigation is a stale node, and nothing else is.</summary>
public sealed class NavigationStaleOrTests
{
    private static readonly string[] NavigationRaces =
    [
        "Execution context was destroyed, most likely because of a navigation.",
        "locator.evaluate: Frame was detached",
    ];

    [Fact]
    public void Reports_a_capture_that_lost_its_document_as_retryable()
    {
        foreach (var text in NavigationRaces)
        {
            var error = WebErrors.NavigationStaleOr(new Exception(text), "observe");
            Assert.Equal("NODE_STALE", error.Code);
            Assert.True(error.Retryable);
            Assert.Contains("observe:", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Keeps_a_timeout_a_timeout_a_capture_that_ran_out_of_budget_is_not_a_race()
    {
        var error = WebErrors.NavigationStaleOr(new TimeoutException("observation capture timed out"), "observe");
        Assert.Equal("OPERATION_TIMEOUT", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public void Leaves_every_other_failure_a_non_retryable_ENGINE_FAILURE()
    {
        var error = WebErrors.NavigationStaleOr(new Exception("protocol error"), "observe");
        Assert.Equal("ENGINE_FAILURE", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public void Never_reclassifies_an_engine_error_the_capture_already_classified()
    {
        var original = new EngineException(EngineErrorCodes.EngineFailure, "Execution context was destroyed", retryable: false);
        Assert.Same(original, WebErrors.NavigationStaleOr(original, "observe"));
    }
}
