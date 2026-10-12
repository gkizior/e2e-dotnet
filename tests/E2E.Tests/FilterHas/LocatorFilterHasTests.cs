// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.FilterHas;

/// <summary>
/// The port resolves a locator over the observed tree and has no engine <c>locate</c> to hand an expression to. And
/// <c>Filter(Locator)</c> cannot take a value that is not a locator, so "refuses a has that is not an e2e locator
/// before any engine call" is not ported: a null is an <see cref="ArgumentNullException"/>.
/// </summary>
public sealed class LocatorFilterHasTests
{
    private static readonly SemanticNode Remove = new() { Ref = "remove", Role = "button", Name = "Remove" };
    private static readonly SemanticNode Note = new() { Ref = "note", Role = "text", Text = "Read only" };
    private static readonly SemanticNode Alpha = new() { Ref = "alpha", Role = "listitem", Text = "Alpha", Children = [Remove] };
    private static readonly SemanticNode Beta = new() { Ref = "beta", Role = "listitem", Text = "Beta", Children = [Note] };

    /// <summary>Partial: there is no engine call to hand the expression to, so the test reads the query the locator builds and the count the screen resolves from it.</summary>
    [Fact]
    public async Task Builds_one_filter_query_with_the_inner_locator_query_under_has_unchanged()
    {
        var screen = ScreenFixture.Create(Alpha, Beta);
        var source = screen.GetByRole("listitem");
        var inner = screen.GetByRole("button", "Remove");

        var filtered = source.Filter(inner);

        Assert.Equal("filter", filtered.Query.Kind);
        Assert.Same(source.Query, filtered.Query.Source);
        Assert.Same(inner.Query, filtered.Query.Has);
        Assert.Equal(1, await filtered.CountAsync());
    }

    [Fact]
    public async Task Names_the_whole_chain_when_a_filtered_read_finds_nothing()
    {
        var screen = ScreenFixture.Create(Alpha, Beta);

        var error = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("listitem").Filter(screen.GetByRole("link")).TextContentAsync());

        Assert.Equal("NOT_FOUND", error.Code);
        Assert.Contains("getByRole(\"listitem\").filter(has: getByRole(\"link\"))", error.Message, StringComparison.Ordinal);
    }
}
