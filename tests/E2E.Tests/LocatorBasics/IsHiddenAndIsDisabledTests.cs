// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.LocatorBasics.Items;

namespace E2E.Tests.LocatorBasics;

public sealed class IsHiddenAndIsDisabledTests
{
    [Fact]
    public async Task IsHidden_is_true_for_an_absent_node_and_a_hidden_one_false_for_a_shown_one()
    {
        Assert.True(await ScreenFixture.Create().GetByText("Gone").IsHiddenAsync());
        Assert.True(await ScreenFixture.Create(Hidden).GetByText("Hidden").IsHiddenAsync());
        Assert.False(await ScreenFixture.Create(Shown).GetByText("Shown").IsHiddenAsync());
    }

    [Fact]
    public async Task IsHidden_fails_on_several_matches_like_isVisible()
    {
        await FailsWith("STRICT_MODE", () => ScreenFixture.Create(List).GetByRole("listitem").IsHiddenAsync());
    }

    [Fact]
    public async Task IsDisabled_mirrors_isEnabled_secure_fields_included()
    {
        Assert.True(await ScreenFixture.Create(Item("x", "x", new NodeStates { Disabled = true })).GetByRole("listitem").IsDisabledAsync());
        Assert.False(await ScreenFixture.Create(Shown).GetByRole("listitem").IsDisabledAsync());
        Assert.True(await ScreenFixture.Create(Item("pw", "", new NodeStates { Secure = true, Disabled = true })).GetByRole("listitem").IsDisabledAsync());
    }

    [Fact]
    public async Task IsDisabled_fails_on_zero_and_several_matches_like_isEnabled()
    {
        await FailsWith("NOT_FOUND", () => ScreenFixture.Create().GetByRole("listitem").IsDisabledAsync());
        await FailsWith("STRICT_MODE", () => ScreenFixture.Create(List).GetByRole("listitem").IsDisabledAsync());
    }
}
