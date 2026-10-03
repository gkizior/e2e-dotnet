// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.NUnit;

namespace E2E.NUnit.Tests;

[NonParallelizable]
public sealed class ReplayTests : E2ETest
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "e2e-nunit", Guid.NewGuid().ToString("n"));
    private static int _actCalls;

    protected override IEngine CreateEngine() => BillingEngine();

    protected override string CacheDirectory => CacheDir;

    // These tests record and replay, so they keep writing in CI, where an unset mode is read-only.
    protected override CacheMode CacheMode => CacheMode.ReadWrite;

    protected override IAgentModel? CreateModel() => BillingModel();

    protected override string? BaseUrl => "https://billing.test";

    protected override string CacheTitle(global::NUnit.Framework.TestContext.TestAdapter test) =>
        test.MethodName == nameof(Each_repeat_is_a_first_attempt) ? "billing > repeated" : "billing > upgrades";

    [Test]
    public async Task First_run_calls_the_model()
    {
        _actCalls = 0;
        await UpgradeAsync();
        Assert.That(_actCalls, Is.GreaterThan(0));
    }

    [Test]
    [DependsOnTest(nameof(First_run_calls_the_model))]
    public async Task Second_run_replays_the_act()
    {
        _actCalls = 0;
        await UpgradeAsync();
        Assert.That(_actCalls, Is.EqualTo(0));
    }

    [Test]
    [Repeat(2)]
    public async Task Each_repeat_is_a_first_attempt()
    {
        // The first iteration records under its own title. The second one replays it.
        _actCalls = 0;
        await UpgradeAsync();
        var repeat = global::NUnit.Framework.TestContext.CurrentContext.CurrentRepeatCount;
        Assert.That(_actCalls, repeat == 0 ? Is.GreaterThan(0) : Is.EqualTo(0));
    }

    private async Task UpgradeAsync()
    {
        await App.OpenAsync("/settings/billing");
        await Agent.ActAsync("upgrade the workspace to the Pro plan");
        await Agent.AssertAsync("the invoice preview shows a prorated amount");
    }

    private static ScriptedModel BillingModel()
    {
        return new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("Statement:", StringComparison.Ordinal))
            {
                return text.Contains("Prorated", StringComparison.Ordinal)
                    ? ModelResponses.Done("passed", "The invoice is prorated.")
                    : ModelResponses.Done("failed", "No prorated amount.", "ASSERTION_FAILED");
            }

            _actCalls++;
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "Upgraded to Pro.");
            }

            return ModelResponses.Tap("button", "Upgrade to Pro");
        });
    }

    private static DocumentEngine BillingEngine()
    {
        var world = new DocumentWorld().Map("/settings/billing", page =>
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
        return new DocumentEngine(world);
    }
}
