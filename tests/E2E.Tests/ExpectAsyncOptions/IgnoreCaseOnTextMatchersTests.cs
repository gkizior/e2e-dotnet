// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using static E2E.Tests.ExpectAsyncOptions.Banner;

namespace E2E.Tests.ExpectAsyncOptions;

public sealed class IgnoreCaseOnTextMatchersTests
{
    [Fact]
    public async Task Matches_a_string_regardless_of_case()
    {
        await At("banner").ToHaveTextAsync("save error", ignoreCase: true);
        await At("banner").ToContainTextAsync("ERROR", ignoreCase: true);
        await At("banner").ToHaveAccessibleNameAsync("SAVE ERROR", ignoreCase: true);
        await At("banner").ToHaveAttributeAsync("data-kind", "error", ignoreCase: true);
        await At("banner").Not.ToContainTextAsync("error", ignoreCase: false, timeout: Soon);
    }

    [Fact]
    public async Task Fails_a_negated_match_that_differs_only_in_case()
    {
        await FailsAssertion(
            () => At("banner").Not.ToContainTextAsync("error", ignoreCase: true, timeout: Soon),
            "expected not text containing \"error\" (ignoring case)");
        await FailsAssertion(() => At("banner").Not.ToHaveTextAsync("SAVE ERROR", ignoreCase: true, timeout: Soon));
        await FailsAssertion(() => At("banner").Not.ToHaveAttributeAsync("data-kind", "ERROR", ignoreCase: true, timeout: Soon));
    }

    [Fact]
    public async Task Keeps_case_sensitive_matching_as_the_default()
    {
        await At("banner").Not.ToContainTextAsync("error", timeout: Soon);
        await FailsAssertion(() => At("banner").ToContainTextAsync("error", timeout: Soon));
    }

    [Fact]
    public async Task Adds_or_strips_the_i_flag_of_a_RegExp()
    {
        await At("banner").ToHaveTextAsync(new Regex("^save error$"), ignoreCase: true);
        await At("banner").Not.ToHaveTextAsync(new Regex("^save error$", RegexOptions.IgnoreCase), ignoreCase: false, timeout: Soon);
        await At("banner").ToHaveTextAsync(new Regex("^save error$", RegexOptions.IgnoreCase));
    }

    [Fact]
    public async Task Applies_to_every_entry_of_a_list()
    {
        await At("banner").ToContainTextAsync(["error"], ignoreCase: true);
        await FailsAssertion(() => At("banner").Not.ToHaveTextAsync(["save error"], ignoreCase: true, timeout: Soon));
    }
}
