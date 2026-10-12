// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.FilterOptions;

/// <summary>
/// <c>Filter</c> takes the text or the locator as an argument, with no options object. So the upstream its that refuse a
/// key it does not implement (<c>hasNot</c>, <c>hasNotText</c>, <c>visible</c>) and a <c>null</c> or inherited options
/// object cannot be written, and are not ported. The <c>query options</c> describe is not ported for the same reason:
/// <c>RoleOptions</c> and <c>TextMatchOptions</c> are typed.
/// </summary>
public sealed class LocatorFilterOptionsTests
{
    private static readonly SemanticNode Paid = new() { Ref = "paid", Role = "row", Text = "Invoice 41 Paid" };
    private static readonly SemanticNode Due = new() { Ref = "due", Role = "row", Text = "Invoice 42 Due" };

    [Fact]
    public async Task Keeps_hasText_as_the_only_predicate_when_it_is_the_only_key()
    {
        var screen = ScreenFixture.Create(Paid, Due);

        var rows = screen.GetByRole("row").Filter("Invoice");

        Assert.Equal(["Invoice 41 Paid", "Invoice 42 Due"], await rows.AllTextContentsAsync());
        Assert.Equal("Invoice 42 Due", await screen.GetByRole("row").Filter("Due").TextContentAsync());
    }

    /// <summary>Partial: an absent argument is an <see cref="ArgumentNullException"/>, not an INVALID_LOCATOR error with the message "filter() requires hasText and/or has".</summary>
    [Fact]
    public void Rejects_an_absent_filter_with_an_argument_error()
    {
        var rows = ScreenFixture.Create(Paid, Due).GetByRole("row");

        Assert.Equal("hasText", Assert.Throws<ArgumentNullException>(() => rows.Filter((TextMatch)null!)).ParamName);
        Assert.Equal("has", Assert.Throws<ArgumentNullException>(() => rows.Filter((Locator)null!)).ParamName);
    }
}
