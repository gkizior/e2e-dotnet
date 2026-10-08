// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using static E2E.Tests.LocatorBasics.Items;

namespace E2E.Tests.LocatorBasics;

public sealed class TheListFormOfToHaveTextAndToContainTextTests
{
    private const string AllThree = "observed text [\"Item Alpha\", \"Item Beta\", \"Item Gamma\"] (match count 3)";

    private static Locator ListItems() => ScreenFixture.Create(List).GetByRole("listitem");

    [Fact]
    public async Task Requires_the_match_count_and_each_entry_at_its_position()
    {
        var locator = ListItems();
        await Expect.That(locator).ToHaveTextAsync(["Item Alpha", new Regex("Beta$"), "Item  Gamma"]);
        await Expect.That(locator).ToContainTextAsync(["Alpha", "Beta", new Regex("gamma", RegexOptions.IgnoreCase)]);
    }

    [Fact]
    public async Task Fails_on_a_count_mismatch_and_reports_every_text_it_saw()
    {
        var error = await FailsWith("ASSERTION_FAILED", () => Expect.That(ListItems()).ToHaveTextAsync(["Item Alpha", "Item Beta"]));
        Assert.Contains(AllThree, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fails_when_an_entry_is_out_of_order_or_only_contained()
    {
        var locator = ListItems();
        var error = await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToHaveTextAsync(["Item Beta", "Item Alpha", "Item Gamma"]));
        Assert.Contains("expected text [\"Item Beta\", \"Item Alpha\", \"Item Gamma\"]", error.Message, StringComparison.Ordinal);
        await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToHaveTextAsync(["Alpha", "Beta", "Gamma"]));
    }

    [Fact]
    public async Task Negated_passes_when_the_list_does_not_hold_on_zero_matches_too()
    {
        await Expect.That(ListItems()).Not.ToHaveTextAsync(["Item Alpha"]);
        await Expect.That(ScreenFixture.Create().GetByRole("listitem")).Not.ToContainTextAsync(["Alpha"]);
    }

    [Fact]
    public async Task An_empty_list_holds_exactly_when_nothing_matches()
    {
        await Expect.That(ScreenFixture.Create().GetByRole("listitem")).ToHaveTextAsync(Array.Empty<TextMatch>());
        await FailsWith("ASSERTION_FAILED", () => Expect.That(ListItems()).ToHaveTextAsync(Array.Empty<TextMatch>()));
    }

    [Fact]
    public async Task ToContainText_holds_for_an_ordered_subsequence_of_the_matches()
    {
        var locator = ListItems();
        await Expect.That(locator).ToContainTextAsync(["Beta"]);
        await Expect.That(locator).ToContainTextAsync(["Alpha", "Gamma"]);
        await Expect.That(locator).ToContainTextAsync(["Item", "Item", "Item"]);
        await Expect.That(locator).ToContainTextAsync(Array.Empty<TextMatch>());
    }

    [Fact]
    public async Task Negated_toContainText_fails_when_one_entry_is_contained_by_some_match()
    {
        var error = await FailsWith("ASSERTION_FAILED", () => Expect.That(ListItems()).Not.ToContainTextAsync(["Beta"]));
        Assert.Contains("expected not text containing [\"Beta\"]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ToContainText_fails_out_of_order_with_more_entries_than_matches_or_on_a_missing_entry()
    {
        var locator = ListItems();
        var error = await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToContainTextAsync(["Gamma", "Alpha"]));
        Assert.Contains(AllThree, error.Message, StringComparison.Ordinal);
        await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToContainTextAsync(["Item", "Item", "Item", "Item"]));
        await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToContainTextAsync(["Alpha", "Delta"]));
        await Expect.That(locator).Not.ToContainTextAsync(["Gamma", "Alpha"]);
    }

    [Fact]
    public async Task ToContainText_needs_a_distinct_match_for_each_duplicate_entry()
    {
        var locator = ScreenFixture.Create(Item("a", "Done"), Item("b", "Open")).GetByRole("listitem");
        await FailsWith("ASSERTION_FAILED", () => Expect.That(locator).ToContainTextAsync(["Done", "Done"]));
        await Expect.That(locator).Not.ToContainTextAsync(["Done", "Done"]);
    }

    [Fact]
    public async Task Keeps_the_single_value_path_strict_on_several_matches()
    {
        await FailsWith("STRICT_MODE", () => Expect.That(ListItems()).ToHaveTextAsync("Item Alpha"));
    }
}
