// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Tests.ExpectAsyncOptions.Banner;

namespace E2E.Tests.ExpectAsyncOptions;

/// <summary>"reads a computed option bag the same way" and "refuses a flag that is not a boolean" are not ported: the port's flags are typed bool parameters, not an option bag.</summary>
public sealed class PlaywrightStateFlagsOnLocatorMatchersTests
{
    [Fact]
    public async Task ToBeChecked_checked_false_waits_for_an_unchecked_node()
    {
        await At("terms").ToBeCheckedAsync(isChecked: false);
        await At("agree").ToBeCheckedAsync(isChecked: true);
        await At("agree").Not.ToBeCheckedAsync(isChecked: false, timeout: Soon);
        await FailsAssertion(() => At("agree").ToBeCheckedAsync(isChecked: false, timeout: Soon), "expected unchecked");
        await FailsAssertion(() => At("terms").Not.ToBeCheckedAsync(isChecked: false, timeout: Soon));
    }

    [Fact]
    public async Task ToBeEnabled_enabled_false_waits_for_a_disabled_node()
    {
        await At("locked").ToBeEnabledAsync(enabled: false);
        await At("banner").ToBeEnabledAsync(enabled: true);
        await FailsAssertion(() => At("banner").ToBeEnabledAsync(enabled: false, timeout: Soon), "expected disabled");
    }

    [Fact]
    public async Task ToBeVisible_visible_false_waits_for_a_hidden_or_absent_node()
    {
        await At("tucked").ToBeVisibleAsync(visible: false);
        await At("nowhere").ToBeVisibleAsync(visible: false);
        await At("banner").ToBeVisibleAsync(visible: true);
        await FailsAssertion(() => At("banner").ToBeVisibleAsync(visible: false, timeout: Soon), "expected hidden or absent");
        await FailsAssertion(() => At("tucked").Not.ToBeVisibleAsync(visible: false, timeout: Soon));
    }

    [Fact]
    public async Task ToBeAttached_attached_false_waits_for_no_match()
    {
        await At("nowhere").ToBeAttachedAsync(attached: false);
        await At("tucked").ToBeAttachedAsync(attached: true);
        await FailsAssertion(() => At("tucked").ToBeAttachedAsync(attached: false, timeout: Soon), "expected detached");
    }
}
