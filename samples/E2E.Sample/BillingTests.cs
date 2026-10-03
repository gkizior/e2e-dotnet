// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.NUnit;

namespace E2E.Sample;

[NonParallelizable]
public sealed class BillingTests : E2ETest
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "e2e-sample", Guid.NewGuid().ToString("n"));

    protected override IEngine CreateEngine() => new DocumentEngine(BillingApp.Create());

    protected override IAgentModel? CreateModel() => new BillingModel();

    protected override string CacheDirectory => CacheDir;

    protected override string CacheTitle(global::NUnit.Framework.TestContext.TestAdapter test) => "billing > a member upgrades to Pro";

    [Test]
    public async Task A_member_upgrades_to_Pro()
    {
        BillingModel.ActCalls = 0;
        await UpgradeAsync();
        Assert.That(BillingModel.ActCalls, Is.GreaterThan(0));
    }

    [Test]
    [DependsOnTest(nameof(A_member_upgrades_to_Pro))]
    public async Task The_second_run_replays_the_upgrade()
    {
        BillingModel.ActCalls = 0;
        await UpgradeAsync();
        Assert.That(BillingModel.ActCalls, Is.EqualTo(0));
    }

    private async Task UpgradeAsync()
    {
        await App.OpenAsync("/settings/billing");
        await Agent.ActAsync("upgrade the workspace to the Pro plan");
        await Agent.AssertAsync("the invoice preview shows a prorated amount");
        await Expect.That(Screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}

internal static class BillingApp
{
    public static DocumentWorld Create()
    {
        return new DocumentWorld().Map("/settings/billing", page =>
        {
            page.Heading("Billing");
            var status = page.Status("Pro", hidden: true);
            var invoice = page.Paragraph("Invoice preview");
            page.Button("Upgrade to Pro", () =>
            {
                status.Hidden = false;
                invoice.Text = "Prorated amount: $12";
            });
        });
    }
}

internal sealed class BillingModel : IAgentModel
{
    public static int ActCalls { get; set; }

    public string Name => "sample";

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var text = string.Join('\n', request.Messages.Select(message => message.Content));
        if (text.Contains("Statement:", StringComparison.Ordinal))
        {
            var passed = text.Contains("Prorated", StringComparison.Ordinal);
            return Task.FromResult(passed
                ? ModelResponses.Done("passed", "The invoice preview shows a prorated amount.")
                : ModelResponses.Done("failed", "The invoice preview has no prorated amount.", "ASSERTION_FAILED"));
        }

        ActCalls++;
        if (text.Contains("tapped", StringComparison.Ordinal))
        {
            return Task.FromResult(ModelResponses.Done("passed", "Upgraded the workspace to Pro."));
        }

        return Task.FromResult(ModelResponses.Tap("button", "Upgrade to Pro"));
    }
}
