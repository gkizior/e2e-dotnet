// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Errors;

/// <summary>
/// Playwright error translation at the engine contract boundary. Not ported: "passes runner errors through
/// untouched" (the port translates only what Playwright raised, and <c>Translate</c> returns an engine error, so a
/// runner error never reaches it), and the two "another module copy" cases (there is one copy of an assembly).
/// </summary>
public sealed class TranslatePwErrorTests
{
    [Fact]
    public void Passes_engine_errors_through_untouched()
    {
        var original = new EngineException(EngineErrorCodes.NodeStale, "gone", retryable: true);
        Assert.Same(original, WebErrors.Translate(original, "observe"));
    }

    [Fact]
    public void Maps_a_timeout_to_a_non_retryable_OPERATION_TIMEOUT()
    {
        var error = WebErrors.Translate(new TimeoutException("waiting for locator"), "observe");
        Assert.Equal("OPERATION_TIMEOUT", error.Code);
        Assert.False(error.Retryable);
    }
}
