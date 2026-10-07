// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Xunit;

namespace E2E.Xunit.Sample;

/// <summary>
/// The <c>samples/E2E.Sample</c> tests on xUnit, against the live Dariten demo at
/// https://dariten.vercel.app. The demo data is shared and anyone can edit it, so the
/// tests only read and check the app's structure, never specific balances or transactions.
/// </summary>
public sealed class DaritenTests : E2ETest
{
    [Fact]
    public async Task The_dashboard_links_to_the_transaction_register()
    {
        var token = Context.CancellationToken;
        await App.OpenAsync("/", token);
        await Expect.That(Screen.GetByRole("heading", "Dashboard")).ToBeVisibleAsync(cancellationToken: token);
        await Expect.That(Screen.GetByTestId("demo-banner")).ToContainTextAsync("Shared demo", cancellationToken: token);
        await Expect.That(Screen.GetByText("Net worth")).ToBeVisibleAsync(cancellationToken: token);

        await Screen.GetByRole("link", "Transactions").ClickAsync(cancellationToken: token);
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync(cancellationToken: token);
        await Expect.That(Screen.GetByRole("button", "Filter")).ToBeVisibleAsync(cancellationToken: token);
    }

    [Fact]
    [Trait("TestCategory", "RealModel")]
    public async Task An_agent_filters_the_register_by_category()
    {
        var token = Context.CancellationToken;
        await App.OpenAsync("/transactions", token);
        await Agent.ActAsync("filter the register to show only the Groceries category", cancellationToken: token);
        await Agent.AssertAsync("every transaction listed is in the Groceries category, or the register says no transactions match", cancellationToken: token);
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync(cancellationToken: token);
    }
}
