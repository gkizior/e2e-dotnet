// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.NUnit.Tests.SkipAfterFailure;
using NUnit.Framework.Interfaces;

namespace E2E.NUnit.Tests.ExpectSoft;

/// <summary>
/// Ported from upstream's <c>expect-soft.test.ts</c>, whose tests run a project through the CLI. These run
/// <see cref="SoftSuiteFixture"/> through NUnit instead, and read the NUnit result in place of the report. Not ported:
/// "produces a schema-valid report" (no JSON report), and "throws at once from an afterEach hook, where nothing collects soft
/// failures" (NUnit collects a soft failure of a [TearDown] too, so the throw is checked on the session, in
/// <c>E2E.Tests.ExpectSoft.ExpectSoftTests</c>). The soft matchers take locator and browser matchers only, so each test
/// uses a locator matcher where upstream also uses <c>expect.soft(1).toBe(2)</c>.
/// </summary>
public sealed class ExpectSoftInARunTests
{
    [Test]
    public void Runs_the_body_to_its_end_then_fails_the_attempt_with_every_soft_locator_failure_in_order()
    {
        // Partial: no "N soft assertions failed" header or numbering (NUnit lists each failure as in Assert.Multiple), no
        // value matcher failure (expect.soft(1).toBe(2)), and no step list.
        var result = NestedRun.Run(nameof(SoftSuiteFixture.Soft_failures_let_the_body_run_on));

        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Failed));
        Assert.That(result.Message, Does.Contain("toHaveText failed: expected text \"the count\""));
        Assert.That(result.Message, Does.Contain("toBeHidden failed"));
        Assert.That(result.Message.IndexOf("toHaveText failed", StringComparison.Ordinal), Is.LessThan(result.Message.IndexOf("toBeHidden failed", StringComparison.Ordinal)));
        // The hard matcher after the failures ran and passed, so exactly the two soft failures are recorded.
        Assert.That(result.AssertionResults, Has.Count.EqualTo(2));
    }

    [Test]
    public void Keeps_the_body_error_and_the_soft_failures_in_one_failure()
    {
        // Partial: NUnit lists the soft failure first and the body error after it, where upstream keeps the body error primary.
        var result = NestedRun.Run(nameof(SoftSuiteFixture.A_body_error_stays_primary_over_the_soft_failures));

        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Failed));
        Assert.That(result.Message, Does.Contain("the body gave up"));
        Assert.That(result.Message, Does.Contain("toHaveText failed: expected text \"the count\""));
    }

    [Test]
    public void Notes_the_soft_failures_of_a_body_that_timed_out_beside_the_timeout()
    {
        // Partial: NUnit reports the timeout as the CancelAfter message, not as a TEST_TIMEOUT code.
        var result = NestedRun.Run(nameof(SoftSuiteFixture.A_timed_out_body_still_settles_its_soft_failures));

        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Failed));
        Assert.That(result.Message, Does.Contain("toHaveText failed: expected text \"the count\""));
    }
}
