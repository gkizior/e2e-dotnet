// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using E2E.XUnit.V3;

namespace E2E.XUnit.V3.Tests;

public sealed class CacheTitleTests : E2ETest
{
    protected override IEngine CreateEngine() => new DocumentEngine(new DocumentWorld());

    protected override CacheMode CacheMode => CacheMode.Off;

    // xUnit shortens these arguments to the same display name, so the title must not come from it.
    [Theory]
    [InlineData("an annual plan for the workspace with seats and tax, billed in EUR")]
    [InlineData("an annual plan for the workspace with seats and tax, billed in USD")]
    public void Each_theory_row_keeps_its_full_argument(string plan)
    {
        var title = CacheTitle(Xunit.TestContext.Current.Test!);
        Assert.Equal("E2E.XUnit.V3.Tests.CacheTitleTests.Each_theory_row_keeps_its_full_argument(" + plan + ")", title);
    }
}
