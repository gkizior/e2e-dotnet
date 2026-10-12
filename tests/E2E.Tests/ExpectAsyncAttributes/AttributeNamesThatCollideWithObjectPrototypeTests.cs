// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Tests.ExpectAsyncValue;

namespace E2E.Tests.ExpectAsyncAttributes;

public sealed class AttributeNamesThatCollideWithObjectPrototypeTests
{
    [Fact]
    public async Task An_absent_attribute_is_absent_whatever_its_name()
    {
        var screen = ScreenFixture.Create(new SemanticNode
        {
            Ref = "node-1",
            Role = "button",
            Attributes = new Dictionary<string, string> { ["type"] = "submit" },
        });
        var locator = screen.GetByRole("button");

        foreach (var name in new[] { "constructor", "toString", "__proto__", "hasOwnProperty" })
        {
            await Expect.That(locator).Not.ToHaveAttributeAsync(name);
            Assert.Null(await locator.GetAttributeAsync(name));
            var error = await ToHaveValueTests.Fails("ASSERTION_FAILED", () => Expect.That(locator).ToHaveAttributeAsync(name, TimeSpan.FromMilliseconds(50)));
            Assert.Contains("attribute \"" + name + "\" absent", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task A_present_attribute_answers_its_string_whatever_its_name()
    {
        var screen = ScreenFixture.Create(new SemanticNode
        {
            Ref = "node-1",
            Role = "button",
            Attributes = new Dictionary<string, string> { ["constructor"] = "x", ["toString"] = "", ["__proto__"] = "proto" },
        });
        var locator = screen.GetByRole("button");

        await Expect.That(locator).ToHaveAttributeAsync("constructor", "x");
        await Expect.That(locator).ToHaveAttributeAsync("toString", "");
        await Expect.That(locator).ToHaveAttributeAsync("__proto__", "proto");
        Assert.Equal("x", await locator.GetAttributeAsync("constructor"));
        Assert.Equal("", await locator.GetAttributeAsync("toString"));
        Assert.Equal("proto", await locator.GetAttributeAsync("__proto__"));
    }
}
