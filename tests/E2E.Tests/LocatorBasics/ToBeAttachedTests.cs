// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.LocatorBasics.Items;

namespace E2E.Tests.LocatorBasics;

public sealed class ToBeAttachedTests
{
    [Fact]
    public async Task Passes_on_a_hidden_node_where_toBeVisible_would_not()
    {
        var locator = ScreenFixture.Create(Hidden).GetByText("Hidden");
        await Expect.That(locator).ToBeAttachedAsync();
        await Expect.That(locator).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Fails_on_zero_matches_once_the_timeout_passes()
    {
        var locator = ScreenFixture.Create().GetByText("Gone");
        var error = await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToBeAttachedAsync());
        Assert.Contains("observed no node (match count 0)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Negated_passes_on_zero_matches_and_fails_on_a_hidden_node()
    {
        await Expect.That(ScreenFixture.Create().GetByText("Gone")).Not.ToBeAttachedAsync();
        await FailsWith("ASSERTION_FAILED", () => Expect.That(ScreenFixture.Create(Hidden).GetByText("Hidden")).Not.ToBeAttachedAsync());
    }

    [Fact]
    public async Task Is_LOCATOR_AMBIGUOUS_on_several_matches_like_every_positive_matcher()
    {
        await FailsWith("STRICT_MODE", () => Expect.That(ScreenFixture.Create(List).GetByRole("listitem")).ToBeAttachedAsync());
    }
}
