// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

internal static class BillingWorld
{
    public static DocumentWorld Create(string button = "Upgrade to Pro")
    {
        return new DocumentWorld().Map("/settings/billing", page =>
        {
            page.Heading("Billing");
            var status = page.Status("Pro", hidden: true);
            var invoice = page.Paragraph("Invoice preview");
            page.Button(button, () =>
            {
                status.Hidden = false;
                invoice.Text = "Prorated amount: $12";
            });
        });
    }
}
