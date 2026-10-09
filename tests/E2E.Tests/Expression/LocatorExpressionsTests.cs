// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Tests.Expression;

/// <summary>
/// The port builds expressions through the locator API and describes them in its own format: a quoted value,
/// the kind "testid", and first() as nth(0).
/// "rejects empty filters" is not ported: the port's typed Filter overloads cannot express an empty filter.
/// "hints only a string test id, never a pattern source read as an id" is not ported: the port has no expression hints.
/// A fractional nth index cannot be written against the port's int parameter.
/// </summary>
public sealed class LocatorExpressionsTests
{
    private readonly Screen _screen = ScreenFixture.Create();

    [Fact]
    public void Builds_role_queries_with_name_and_states()
    {
        var query = _screen.GetByRole("button", "Save", new RoleOptions { Checked = true }).Query;
        Assert.Equal(("role", "button", "\"Save\"", true, true), (query.Kind, query.Role, query.Value?.ToString(), query.Exact, query.Checked));
        Assert.Null(query.Disabled);
    }

    [Fact]
    public void Rewrites_the_img_alias_to_the_image_role_before_the_expression_is_built()
    {
        Assert.Equal(_screen.GetByRole("image", "Logo").Query.Describe(), _screen.GetByRole("img", "Logo").Query.Describe());
        Assert.Equal("image", _screen.GetByRole("img").Query.Role);
        Assert.Equal(_screen.GetByRole("image").Query.Describe(), _screen.GetByRole("img").Query.Describe());
    }

    [Fact]
    public void Carries_visible_true_on_every_query_kind_and_drops_the_default()
    {
        Assert.True(_screen.GetByRole("button", new RoleOptions { Visible = true }).Query.Visible);
        Assert.True(_screen.GetByText("Pro", new TextMatchOptions { Visible = true }).Query.Visible);
        Assert.True(_screen.GetByTestId("card", new TextMatchOptions { Visible = true }).Query.Visible);
        Assert.False(_screen.GetByRole("button", new RoleOptions { Visible = false }).Query.Visible);
        Assert.False(_screen.GetByLabel("Email").Query.Visible);
        Assert.False(_screen.GetByTestId("card").Query.Visible);
    }

    [Fact]
    public void Describes_a_visible_query_so_an_ambiguity_message_shows_the_predicate()
    {
        Assert.Equal("getByText(\"Pro\", visible: true)", _screen.GetByText("Pro", new TextMatchOptions { Visible = true }).Query.Describe());
        Assert.Equal("getByRole(\"button\", \"Save\", visible: true)", _screen.GetByRole("button", "Save", new RoleOptions { Visible = true }).Query.Describe());
    }

    [Fact]
    public void Scopes_queries_under_a_parent_expression()
    {
        var scope = _screen.GetByTestId("card");
        var query = scope.GetByText("Pro").Query;
        Assert.Equal("text", query.Kind);
        Assert.Equal(scope.Query.Describe(), query.Parent?.Describe());
    }

    [Fact]
    public void Rejects_negative_and_fractional_nth_indices()
    {
        var source = _screen.GetByTestId("card");
        Assert.Throws<ArgumentOutOfRangeException>(() => source.Nth(-1));
        var first = source.Nth(0).Query;
        Assert.Equal(("index", 0), (first.Kind, first.Index));
    }

    [Fact]
    public void Regexp_text_queries_serialize_source_and_flags()
    {
        var value = _screen.GetByText(new Regex("pro", RegexOptions.IgnoreCase)).Query.Value;
        Assert.Equal("/pro/i", value?.ToString());
    }

    [Fact]
    public void Builds_a_test_id_query_from_a_string_or_a_regexp()
    {
        var card = _screen.GetByTestId("card").Query;
        Assert.Equal(("testid", "\"card\"", true), (card.Kind, card.Value?.ToString(), card.Exact));
        var total = _screen.GetByTestId(new Regex("^total-", RegexOptions.IgnoreCase)).Query;
        Assert.Equal(("testid", "/^total-/i"), (total.Kind, total.Value?.ToString()));
        Assert.Equal("getByTestId(/^total-/i)", total.Describe());
    }

    [Fact]
    public void DescribeExpression_renders_a_readable_chain()
    {
        Assert.Equal("getByRole(\"listitem\").filter(hasText: \"Pro\").nth(0)", _screen.GetByRole("listitem").Filter("Pro").First().Query.Describe());
    }

    [Fact]
    public void DescribeExpression_renders_a_has_filter_with_the_inner_locator_after_hasText_when_both_are_set()
    {
        var inner = _screen.GetByRole("button", "Remove");
        var source = _screen.GetByRole("listitem");
        Assert.Equal("getByRole(\"listitem\").filter(has: getByRole(\"button\", \"Remove\"))", source.Filter(inner).Query.Describe());
        Assert.Equal(
            "getByRole(\"listitem\").filter(hasText: \"Pro\").filter(has: getByRole(\"button\", \"Remove\"))",
            source.Filter("Pro").Filter(inner).Query.Describe());
    }
}
