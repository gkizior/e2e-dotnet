// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Engine;
using static E2E.Tests.LocatorBasics.Items;

namespace E2E.Tests.LocatorBasics;

public sealed class IsCheckedAndIsEnabledTests
{
    [Fact]
    public async Task IsChecked_reads_the_checked_state_true_false_and_no_state_at_all_as_false()
    {
        Assert.True(await ScreenFixture.Create(Item("on", "On", new NodeStates { Checked = true })).GetByRole("listitem").IsCheckedAsync());
        Assert.False(await ScreenFixture.Create(Item("off", "Off", new NodeStates { Checked = false })).GetByRole("listitem").IsCheckedAsync());
        Assert.False(await ScreenFixture.Create(Shown).GetByRole("listitem").IsCheckedAsync());
    }

    [Fact]
    public async Task IsChecked_reads_a_secure_field_since_a_state_is_not_a_value()
    {
        Assert.True(await ScreenFixture.Create(Item("pw", "", new NodeStates { Secure = true, Checked = true })).GetByRole("listitem").IsCheckedAsync());
    }

    [Fact]
    public async Task IsEnabled_is_true_with_no_states_and_with_disabled_false_false_once_disabled()
    {
        Assert.True(await ScreenFixture.Create(Shown).GetByRole("listitem").IsEnabledAsync());
        Assert.True(await ScreenFixture.Create(Item("x", "x", new NodeStates { Disabled = false })).GetByRole("listitem").IsEnabledAsync());
        Assert.False(await ScreenFixture.Create(Item("x", "x", new NodeStates { Disabled = true })).GetByRole("listitem").IsEnabledAsync());
    }

    [Fact]
    public async Task Both_fail_at_once_on_zero_matches_where_isVisible_answers_false_and_on_several_like_isVisible()
    {
        var none = ScreenFixture.Create().GetByRole("listitem");
        var several = ScreenFixture.Create(List).GetByRole("listitem");
        var watch = Stopwatch.StartNew();
        await FailsWith("NOT_FOUND", () => none.IsCheckedAsync());
        await FailsWith("NOT_FOUND", () => none.IsEnabledAsync());
        await FailsWith("STRICT_MODE", () => several.IsCheckedAsync());
        await FailsWith("STRICT_MODE", () => several.IsEnabledAsync());
        Assert.False(await none.IsVisibleAsync());
        Assert.True(watch.Elapsed < ScreenFixture.Timeout, "answered after " + watch.Elapsed);
    }
}
