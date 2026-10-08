// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.NUnit;
using E2E.Playground;

namespace E2E.Agent.Tests.Judgments;

/// <summary>
/// The judgment tier against a real model: <c>AssertAsync</c>, <c>WaitForAsync</c>, and
/// schema-validated <c>ExtractAsync</c>, each paired with a deterministic check so a wrong
/// judgment cannot pass silently. "a vision judgment reads drawn pixels the tree cannot show"
/// is not ported: the port has no vision judgments.
/// </summary>
[Category("RealModel")]
public sealed class JudgmentsTests : E2ETest
{
    protected override string? BaseUrl => Testbed.Url;

    [Test]
    public async Task Assert_judges_seeded_state_mixed_with_deterministic_steps()
    {
        await App.OpenAsync("/todos");
        await Screen.GetByLabel("New todo").FillAsync("Review the release notes");
        await Screen.GetByRole("button", "Add").ClickAsync();
        await Screen.GetByLabel("New todo").FillAsync("File the expense report");
        await Screen.GetByRole("button", "Add").ClickAsync();
        await Expect.That(Screen.GetByRole("status")).ToHaveTextAsync("2 remaining");
        await Agent.AssertAsync("two todos are listed and neither is marked done");
    }

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

    [Test]
    public async Task WaitFor_polls_until_the_loaded_users_appear()
    {
        await App.OpenAsync("/network");
        await Screen.GetByRole("button", "Load users").ClickAsync();
        await Agent.WaitForAsync("the list shows the three users Ada, Grace, and Margaret", new WaitForOptions { Interval = TimeSpan.FromMilliseconds(250) });
        await Expect.That(Screen.GetByRole("status")).ToHaveTextAsync("loaded 3");
    }

    [Test]
    public async Task Extract_returns_schema_validated_data_from_the_screen()
    {
        await App.OpenAsync("/todos");
        await Screen.GetByLabel("New todo").FillAsync("Water the plants");
        await Screen.GetByRole("button", "Add").ClickAsync();
        var data = await Agent.ExtractAsync<TodoSummary>("the todo titles and the remaining count");
        Assert.That(data.Remaining, Is.EqualTo(1));
        Assert.That(data.Todos, Does.Contain("Water the plants"));
    }

    [Test]
    public async Task Act_and_a_judgment_cooperate_in_one_flow()
    {
        await App.OpenAsync("/forms");
        await Agent.ActAsync("fill the profile with the name \"Ada Lovelace\" and the team \"Platform\", then save");
        await Agent.AssertAsync("the profile form reports it was saved");
        await Expect.That(Screen.GetByLabel("Full name")).ToHaveValueAsync("Ada Lovelace");
    }

    /// <summary>The schema upstream's extract test passes: the todo titles and the remaining count.</summary>
    public sealed record TodoSummary(IReadOnlyList<string> Todos, int Remaining);
}
