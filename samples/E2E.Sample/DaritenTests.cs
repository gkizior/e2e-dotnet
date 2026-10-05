// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.NUnit;

namespace E2E.Sample;

/// <summary>
/// End-to-end tests against the live Dariten demo at https://dariten.vercel.app.
/// The demo data is shared and anyone can edit it, so the tests only read and
/// check the app's structure, never specific balances or transactions.
/// </summary>
public sealed class DaritenTests : E2ETest
{
    [Test]
    public async Task The_dashboard_links_to_the_transaction_register()
    {
        await App.OpenAsync("/");
        await Expect.That(Screen.GetByRole("heading", "Dashboard")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByTestId("demo-banner")).ToContainTextAsync("Shared demo");
        await Expect.That(Screen.GetByText("Net worth")).ToBeVisibleAsync();

        await Screen.GetByRole("link", "Transactions").ClickAsync();
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByRole("button", "Filter")).ToBeVisibleAsync();
    }

    [Test]
    [Category("RealModel")]
    public async Task An_agent_filters_the_register_by_category()
    {
        await App.OpenAsync("/transactions");
        await Agent.ActAsync("filter the register to show only the Groceries category");
        await Agent.AssertAsync("every transaction listed is in the Groceries category, or the register says no transactions match");
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync();
    }
}
