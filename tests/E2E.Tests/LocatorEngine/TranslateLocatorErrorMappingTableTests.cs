// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.LocatorEngine;

/// <summary>
/// The port has no <c>translateLocatorError</c>: an engine error reaches the test with the engine's own code (COMPATIBILITY,
/// Web engine), except <c>INVALID_STATE</c>, which means no app is open. So the rows of the mapping table for every other
/// code, and the its that pass errors through, wrap unknown errors, and append the locator description, are not ported.
/// </summary>
public sealed class TranslateLocatorErrorMappingTableTests
{
    private static Screen ScreenThatFails(Exception failure) => new(
        _ => throw failure,
        (_, _, _) => Task.CompletedTask,
        () => CancellationToken.None,
        () => { },
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(300));

    [Fact]
    public async Task INVALID_STATE_to_test_APP_NOT_OPEN()
    {
        var failure = new EngineException(EngineErrorCodes.InvalidState, "boom");

        var translated = await Assert.ThrowsAsync<TestException>(() => ScreenThatFails(failure).GetByRole("button").CountAsync());

        Assert.Equal("APP_NOT_OPEN", translated.Code);
        Assert.Same(failure, translated.InnerException);
    }
}
