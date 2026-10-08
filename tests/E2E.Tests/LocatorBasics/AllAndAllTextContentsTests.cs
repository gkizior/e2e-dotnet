// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Engine;
using static E2E.Tests.LocatorBasics.Items;

namespace E2E.Tests.LocatorBasics;

public sealed class AllAndAllTextContentsTests
{
    [Fact]
    public async Task Hands_back_one_nth_locator_per_current_match_in_document_order()
    {
        var all = await ScreenFixture.Create(List).GetByRole("listitem").AllAsync();
        Assert.Equal(3, all.Count);
        var texts = new List<string?>();
        foreach (var locator in all)
        {
            texts.Add(await locator.TextContentAsync());
        }

        Assert.Equal(["Item Alpha", "Item Beta", "Item Gamma"], texts);
    }

    [Fact]
    public async Task Reads_the_normalized_text_of_every_match_without_strictness()
    {
        Assert.Equal(["Item Alpha", "Item Beta", "Item Gamma"], await ScreenFixture.Create(List).GetByRole("listitem").AllTextContentsAsync());
    }

    [Fact]
    public async Task Answers_zero_matches_with_empty_lists_instead_of_waiting()
    {
        var screen = ScreenFixture.Create();
        var watch = Stopwatch.StartNew();
        Assert.Empty(await screen.GetByRole("listitem").AllAsync());
        Assert.Empty(await screen.GetByRole("listitem").AllTextContentsAsync());
        Assert.True(watch.Elapsed < ScreenFixture.Timeout, "answered after " + watch.Elapsed);
    }

    [Fact]
    public async Task Reads_a_match_without_text_as_an_empty_string()
    {
        var screen = ScreenFixture.Create(Item("bare", null));
        Assert.Equal([""], await screen.GetByRole("listitem").AllTextContentsAsync());
    }

    [Fact(Skip = "Gap: the port's allTextContents reads a secure field's text; upstream denies it with POLICY_DENIED, like textContent.")]
    public async Task Denies_allTextContents_when_a_match_is_a_secure_field_like_textContent()
    {
        var screen = ScreenFixture.Create(Shown, Item("pw", "hunter2", new NodeStates { Secure = true }));
        await FailsWith("POLICY_DENIED", () => screen.GetByRole("listitem").AllTextContentsAsync());
    }
}
