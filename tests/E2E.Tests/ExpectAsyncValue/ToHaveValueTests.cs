// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.ExpectAsyncValue;

public sealed class ToHaveValueTests
{
    private static readonly SemanticNode Textarea = new() { Ref = "node-1", Role = "textbox", Value = "line1\n\nline2  " };

    private static readonly SemanticNode Heading = new() { Ref = "node-1", Role = "heading", Text = "Title" };

    [Fact]
    public async Task Compares_the_raw_value_newlines_and_trailing_spaces_included()
    {
        var locator = ScreenFixture.Create(Textarea).GetByRole("textbox");
        await Expect.That(locator).ToHaveValueAsync("line1\n\nline2  ");
        await Expect.That(locator).ToHaveValueAsync(new Regex("^line1\n\nline2 {2}$"));
        await Expect.That(locator).Not.ToHaveValueAsync("line1 line2");
    }

    [Fact]
    public async Task Prints_the_raw_value_it_compared_when_it_fails()
    {
        var locator = ScreenFixture.Create(Textarea).GetByRole("textbox");
        var error = await Fails("ASSERTION_FAILED", () => Expect.That(locator).ToHaveValueAsync("line1 line2"));
        Assert.True(error.Message.Contains("observed value \"line1\\n\\nline2  \"", StringComparison.Ordinal), error.Message);
    }

    [Theory]
    [InlineData("textbox")]
    [InlineData("searchbox")]
    [InlineData("combobox")]
    [InlineData("spinbutton")]
    [InlineData("slider")]
    [InlineData("listbox")]
    [InlineData("option")]
    public async Task Reads_a_control_the_platform_reports_without_a_value_as_the_empty_string(string role)
    {
        var locator = ScreenFixture.Create(new SemanticNode { Ref = "node-1", Role = role, TestId = "field" }).GetByTestId("field");
        await Expect.That(locator).ToHaveValueAsync("");
        var error = await Fails("ASSERTION_FAILED", () => Expect.That(locator).ToHaveValueAsync("x"));
        Assert.True(error.Message.Contains("observed value \"\"", StringComparison.Ordinal), error.Message);
    }

    [Fact]
    public async Task Refuses_a_node_without_a_value_negated_or_not()
    {
        var locator = ScreenFixture.Create(Heading).GetByRole("heading");
        var error = await Fails("ASSERTION_FAILED", () => Expect.That(locator).ToHaveValueAsync(""));
        Assert.True(error.Message.Contains("observed no value (not a form control)", StringComparison.Ordinal), error.Message);
        error = await Fails("ASSERTION_FAILED", () => Expect.That(locator).Not.ToHaveValueAsync("Title"));
        Assert.True(error.Message.Contains("observed no value (not a form control)", StringComparison.Ordinal), error.Message);
    }

    [Fact]
    public async Task Refuses_a_link_whose_name_and_text_are_not_a_value()
    {
        var locator = ScreenFixture.Create(new SemanticNode { Ref = "node-1", Role = "link", Name = "Docs", Text = "Docs" }).GetByRole("link");
        var error = await Fails("ASSERTION_FAILED", () => Expect.That(locator).ToHaveValueAsync("Docs"));
        Assert.True(error.Message.Contains("observed no value (not a form control)", StringComparison.Ordinal), error.Message);
        error = await Fails("ASSERTION_FAILED", () => Expect.That(locator).Not.ToHaveValueAsync(""));
        Assert.True(error.Message.Contains("observed no value (not a form control)", StringComparison.Ordinal), error.Message);
    }

    [Fact]
    public async Task Refuses_to_judge_a_secure_field_whose_value_is_withheld_negated_or_not()
    {
        var secure = new SemanticNode { Ref = "node-1", Role = "textbox", Name = "Password", States = new NodeStates { Secure = true } };
        var locator = ScreenFixture.Create(secure).GetByRole("textbox");
        await Denied(() => Expect.That(locator).ToHaveValueAsync(""));
        await Denied(() => Expect.That(locator).Not.ToHaveValueAsync(""));
        await Denied(() => Expect.That(locator).ToHaveTextAsync(""));
        await Denied(() => Expect.That(locator).Not.ToHaveTextAsync(""));
        await Denied(() => Expect.That(locator).ToContainTextAsync(""));
        await Denied(() => Expect.That(locator).ToHaveTextAsync([""]));
    }

    [Fact]
    public async Task Denies_text_value_and_attribute_reads_of_a_secure_field_but_answers_state_reads()
    {
        var secure = new SemanticNode { Ref = "node-1", Role = "textbox", Name = "Password", States = new NodeStates { Secure = true } };
        var locator = ScreenFixture.Create(secure).GetByRole("textbox");
        await Denied(() => locator.TextContentAsync());
        await Denied(() => locator.InputValueAsync());
        await Denied(() => locator.GetAttributeAsync("type"));
        Assert.True(await locator.IsEnabledAsync());
        Assert.False(await locator.IsCheckedAsync());
        Assert.True(await locator.IsVisibleAsync());
    }

    [Fact]
    public async Task Still_reads_a_secure_field_for_state_and_name_matchers()
    {
        var secure = new SemanticNode { Ref = "node-1", Role = "textbox", Name = "Password", States = new NodeStates { Secure = true, Focused = true } };
        var locator = ScreenFixture.Create(secure).GetByRole("textbox");
        await Expect.That(locator).ToBeFocusedAsync();
        await Expect.That(locator).ToHaveAccessibleNameAsync("Password");
    }

    [Fact]
    public async Task Leaves_toHaveAccessibleName_normalized_like_text()
    {
        var locator = ScreenFixture.Create(new SemanticNode { Ref = "node-1", Role = "button", Name = " Save  now " }).GetByRole("button");
        await Expect.That(locator).ToHaveAccessibleNameAsync("Save now");
        var error = await Assert.ThrowsAsync<TestException>(() => Expect.That(locator).ToHaveAccessibleNameAsync("Save"));
        Assert.True(error.Message.Contains("observed accessible name \"Save now\"", StringComparison.Ordinal), error.Message);
    }

    internal static async Task<TestException> Fails(string code, Func<Task> run)
    {
        var error = await Assert.ThrowsAsync<TestException>(run);
        Assert.Equal(code, error.Code);
        return error;
    }

    internal static async Task Denied(Func<Task> run)
    {
        var error = await Fails("POLICY_DENIED", run);
        Assert.Equal("reading values from a secure field is denied: getByRole(\"textbox\")", error.Message);
    }
}
