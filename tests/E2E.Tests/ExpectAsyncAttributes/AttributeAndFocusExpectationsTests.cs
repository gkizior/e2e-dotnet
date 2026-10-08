// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Tests.ExpectAsyncValue;

namespace E2E.Tests.ExpectAsyncAttributes;

public sealed class AttributeAndFocusExpectationsTests
{
    [Fact]
    public async Task Matches_attribute_presence_and_values_without_normalizing_whitespace()
    {
        var screen = ScreenFixture.Create(new SemanticNode
        {
            Ref = "node-1",
            Role = "textbox",
            Attributes = new Dictionary<string, string> { ["readonly"] = "", ["class"] = "card  active" },
            States = new NodeStates { Focused = true },
        });
        var locator = screen.GetByRole("textbox");

        await Expect.That(locator).ToHaveAttributeAsync("readonly");
        await Expect.That(locator).ToHaveAttributeAsync("readonly", "");
        await Expect.That(locator).ToHaveAttributeAsync("class", new Regex("active"));
        await Expect.That(locator).Not.ToHaveAttributeAsync("hidden");
        await Expect.That(locator).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Refuses_to_judge_an_attribute_of_a_secure_field_whose_value_attribute_is_withheld_negated_or_not()
    {
        var secure = new SemanticNode
        {
            Ref = "node-1",
            Role = "textbox",
            Name = "Password",
            Attributes = new Dictionary<string, string> { ["type"] = "password" },
            States = new NodeStates { Secure = true },
        };
        var locator = ScreenFixture.Create(secure).GetByRole("textbox");
        await ToHaveValueTests.Denied(() => locator.GetAttributeAsync("value"));
        await ToHaveValueTests.Denied(() => Expect.That(locator).ToHaveAttributeAsync("value"));
        await ToHaveValueTests.Denied(() => Expect.That(locator).Not.ToHaveAttributeAsync("value"));
        await ToHaveValueTests.Denied(() => Expect.That(locator).ToHaveAttributeAsync("value", "synthetic"));
        await ToHaveValueTests.Denied(() => Expect.That(locator).Not.ToHaveAttributeAsync("value", "synthetic"));
        await ToHaveValueTests.Denied(() => Expect.That(locator).ToHaveAttributeAsync("type", "password"));
    }

    [Fact]
    public async Task Still_judges_the_value_attribute_of_a_plain_field_and_of_one_without_it()
    {
        var screen = ScreenFixture.Create(
            new SemanticNode { Ref = "node-1", Role = "textbox", Name = "Plain", Attributes = new Dictionary<string, string> { ["value"] = "synthetic" } },
            new SemanticNode { Ref = "node-2", Role = "textbox", Name = "Blank" });
        var plain = screen.GetByRole("textbox", "Plain");
        var blank = screen.GetByRole("textbox", "Blank");
        await Expect.That(plain).ToHaveAttributeAsync("value", "synthetic");
        await Expect.That(blank).Not.ToHaveAttributeAsync("value");
        var error = await ToHaveValueTests.Fails("ASSERTION_FAILED", () => Expect.That(plain).Not.ToHaveAttributeAsync("value", TimeSpan.FromMilliseconds(50)));
        Assert.Contains("observed attribute \"value\" \"synthetic\"", error.Message, StringComparison.Ordinal);
        error = await ToHaveValueTests.Fails("ASSERTION_FAILED", () => Expect.That(blank).ToHaveAttributeAsync("value", TimeSpan.FromMilliseconds(50)));
        Assert.Contains("observed attribute \"value\" absent", error.Message, StringComparison.Ordinal);
    }
}
