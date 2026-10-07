// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.NUnit;

namespace E2E.Agent.Tests;

/// <summary>
/// The judgment tier against a real model: <c>AssertAsync</c>, each paired with a
/// deterministic check so a wrong judgment cannot pass silently.
/// </summary>
[Category("RealModel")]
public sealed class JudgmentsTests : E2ETest
{
    protected override string? BaseUrl => Testbed.Url;

    [Test]
    public async Task Assert_fails_when_one_of_two_displayed_totals_contradicts_it()
    {
        await App.OpenAsync("/checkout");
        await Screen.GetByLabel("Notebook quantity").FillAsync("3");
        await Expect.That(Screen.GetByText("$42.00")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByRole("button", "Pay $18.00")).ToBeVisibleAsync();
        var error = await Assert.ThrowsAsync<AgentException>(() => Agent.AssertAsync("the order total is $42.00"));
        Assert.That(error?.Code, Is.EqualTo("ASSERTION_FAILED"));
    }

    [Test]
    public async Task Assert_holds_for_a_claim_about_some_item_when_another_item_differs()
    {
        await App.OpenAsync("/todos");
        await Screen.GetByLabel("New todo").FillAsync("Pay the invoice");
        await Screen.GetByRole("button", "Add").ClickAsync();
        await Screen.GetByLabel("New todo").FillAsync("Book the venue");
        await Screen.GetByRole("button", "Add").ClickAsync();
        await Screen.GetByRole("checkbox").First().CheckAsync();
        await Expect.That(Screen.GetByRole("status")).ToHaveTextAsync("1 remaining");
        await Agent.AssertAsync("a todo is marked done");
    }

    [Test]
    public async Task Assert_holds_for_one_value_when_a_different_value_is_stale()
    {
        await App.OpenAsync("/checkout");
        await Screen.GetByLabel("Notebook quantity").FillAsync("3");
        await Expect.That(Screen.GetByRole("button", "Pay $18.00")).ToBeVisibleAsync();
        await Agent.AssertAsync("the Notebook line total is $36.00");
    }

    [Test]
    public async Task Assert_holds_for_the_named_instance_when_another_instance_is_stale()
    {
        await App.OpenAsync("/checkout");
        await Screen.GetByLabel("Notebook quantity").FillAsync("3");
        await Expect.That(Screen.GetByText("$42.00")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByRole("button", "Pay $18.00")).ToBeVisibleAsync();
        await Agent.AssertAsync("the order summary total is $42.00");
    }
}
