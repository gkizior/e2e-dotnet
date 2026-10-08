// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ExpectAsyncOptions;

/// <summary>The fixture screen of upstream's expect-async-options tests.</summary>
internal static class Banner
{
    public static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(100);

    private static readonly Screen Screen = ScreenFixture.Create(
        new SemanticNode { Ref = "node-1", Role = "checkbox", Name = "Agree", TestId = "agree", States = new NodeStates { Checked = true } },
        new SemanticNode { Ref = "node-2", Role = "checkbox", TestId = "terms" },
        new SemanticNode { Ref = "node-3", Role = "button", TestId = "locked", States = new NodeStates { Disabled = true } },
        new SemanticNode { Ref = "node-4", Role = "generic", TestId = "tucked", States = new NodeStates { Hidden = true } },
        new SemanticNode
        {
            Ref = "node-5",
            Role = "alert",
            Name = "Save Error",
            TestId = "banner",
            Text = "Save Error",
            Attributes = new Dictionary<string, string> { ["data-kind"] = "Error" },
        });

    public static LocatorExpect At(string testId) => Expect.That(Screen.GetByTestId(testId));

    public static async Task FailsAssertion(Func<Task> run, string? message = null)
    {
        var error = await Assert.ThrowsAsync<TestException>(run);
        Assert.Equal("ASSERTION_FAILED", error.Code);
        if (message is not null)
        {
            Assert.True(error.Message.Contains(message, StringComparison.Ordinal), error.Message);
        }
    }
}
