// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.Xunit;

namespace E2E.Xunit.Tests;

public sealed class SoftFailureTests : E2ETest
{
    private TestException? _reported;
    private bool _bodyEnded;

    protected override IEngine CreateEngine()
    {
        return new DocumentEngine(new DocumentWorld().Map("/", page =>
        {
            page.Heading("Billing");
            page.Status("Pro", hidden: true);
        }));
    }

    protected override CacheMode CacheMode => CacheMode.Off;

    protected override TimeSpan AssertionTimeout => TimeSpan.FromMilliseconds(100);

    protected override void ReportSoftFailures(TestException failures) => _reported = failures;

    [Fact]
    public async Task Soft_failures_are_reported_once_the_body_ends()
    {
        await App.OpenAsync("https://billing.test/", Context.CancellationToken);
        await Expect.Soft(Screen.GetByRole("status", "Pro")).ToBeVisibleAsync(cancellationToken: Context.CancellationToken);
        await Expect.Soft(Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync(cancellationToken: Context.CancellationToken);

        Assert.Null(_reported);
        _bodyEnded = true;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Assert.True(_bodyEnded);
        Assert.NotNull(_reported);
        Assert.Equal("ASSERTION_FAILED", _reported.Code);
        Assert.StartsWith("1 soft assertion failed", _reported.Message, StringComparison.Ordinal);
    }
}
