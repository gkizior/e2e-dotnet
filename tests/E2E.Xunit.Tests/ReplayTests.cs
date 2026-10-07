// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace E2E.Xunit.Tests;

// The second test replays what the first one recorded, so they run in name order.
[TestMethodOrderer(typeof(ByMethodNameOrderer))]
public sealed class ReplayTests : E2ETest
{
    private static readonly string CacheDir = Path.Combine(Path.GetTempPath(), "e2e-xunit", Guid.NewGuid().ToString("n"));
    private static int _actCalls;

    protected override IEngine CreateEngine() => BillingEngine();

    protected override string CacheDirectory => CacheDir;

    // These tests record and replay, so they keep writing in CI, where an unset mode is read-only.
    protected override CacheMode CacheMode => CacheMode.ReadWrite;

    protected override IAgentModel? CreateModel() => BillingModel();

    protected override string? BaseUrl => "https://billing.test";

    protected override string CacheTitle(ITest test) => "billing > upgrades";

    [Fact]
    public async Task First_run_calls_the_model()
    {
        _actCalls = 0;
        await UpgradeAsync();
        Assert.True(_actCalls > 0);
    }

    [Fact]
    public async Task Second_run_replays_the_act()
    {
        _actCalls = 0;
        await UpgradeAsync();
        Assert.Equal(0, _actCalls);
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

public sealed class ByMethodNameOrderer : ITestMethodOrderer
{
    public IReadOnlyCollection<TTestMethod?> OrderTestMethods<TTestMethod>(IReadOnlyCollection<TTestMethod?> testMethods)
        where TTestMethod : notnull, ITestMethod
    {
        return [.. testMethods.OrderBy(testMethod => testMethod?.MethodName, StringComparer.Ordinal)];
    }
}
