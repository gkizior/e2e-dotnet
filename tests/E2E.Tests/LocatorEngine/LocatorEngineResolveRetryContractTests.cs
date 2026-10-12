// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.LocatorEngine;

/// <summary>
/// The port resolves a locator over one observation per try and has no engine <c>locate</c> to retry, so the count of
/// resolves is the count of observations, and the codes are the port's STRICT_MODE and NOT_FOUND (COMPATIBILITY, Web
/// engine). The other upstream its of this file are not ported: the retry on a stale or frame miss, the deadline cut of a
/// resolve, the stale read and perform contracts, <c>readAll</c> and <c>readNow</c> by reference, and
/// <c>translateLocatorError</c>, all of which need the engine reference API the port does not have.
/// </summary>
public sealed class LocatorEngineResolveRetryContractTests
{
    private static Screen ScreenOver(Func<Observation> observe, Action onObserve) => new(
        _ =>
        {
            onObserve();
            return Task.FromResult(observe());
        },
        (_, _, _) => Task.CompletedTask,
        () => CancellationToken.None,
        () => { },
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(350));

    [Fact]
    public async Task Fails_ambiguous_matches_immediately_with_LOCATOR_AMBIGUOUS()
    {
        var observations = 0;
        var screen = ScreenOver(
            () => new Observation
            {
                Route = "/",
                Roots = [new SemanticNode { Ref = "node-1", Role = "button", Name = "Submit" }, new SemanticNode { Ref = "node-2", Role = "button", Name = "Submit" }],
            },
            () => observations++);

        var error = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button").TapAsync());

        Assert.Equal("STRICT_MODE", error.Code);
        Assert.Equal(1, observations);
    }

    [Fact]
    public async Task Polls_zero_matches_until_the_deadline_then_LOCATOR_NOT_FOUND()
    {
        var observations = 0;
        var screen = ScreenOver(() => new Observation { Route = "/", Roots = [] }, () => observations++);

        var error = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button").TapAsync());

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.True(observations > 1);
    }
}
