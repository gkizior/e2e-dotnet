// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using NUnit.Framework.Interfaces;

namespace E2E.NUnit.Tests.SkipAfterFailure;

/// <summary>
/// Ported from upstream's <c>skip-after-failure.test.ts</c> (tester-army/e2e#924), whose tests
/// run the CLI over a project. These run <see cref="SkippingFixture"/> through NUnit instead, with
/// <c>Assert.Ignore</c> and <c>Assert.Inconclusive</c> in place of <c>test.skip</c>. Not ported:
/// "shows the original failure when a retry skips" and the <c>failOnSkippedFailure</c> policy
/// that "keeps clean skips and passing soft assertions green" also sets (the port has no
/// <c>failOnSkippedFailure</c> and no <c>Skipped After Failure</c> list), "fails a serial member on a soft failure before its
/// skip without blaming another skipped member" (no serial suites), and "does not turn post-skip
/// cleanup diagnostics into a test failure" (the fixture takes no custom engine).
/// </summary>
public sealed class ARuntimeSkipAfterAFailureTests
{
    [SetUp]
    public void ResetFixture()
    {
        SkippingFixture.Attempts = 0;
        SkippingFixture.TeardownRan = false;
        SkippingFixture.Messages.Clear();
    }

    [Test]
    public void Fails_on_a_soft_failure_before_the_skip_keeping_the_skip_reason_beside_it()
    {
        var result = NestedRun.Run(nameof(SkippingFixture.Soft_then_skip));

        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Failed));
        Assert.That(result.Message, Does.Contain("toHaveText failed: expected text \"the count\""));
        Assert.That(result.Message, Does.Contain("toHaveText failed: expected text \"the version\""));
        Assert.That(result.Message, Does.Contain("feature disabled"));
        Assert.That(SkippingFixture.TeardownRan, Is.True);
    }

    [Test]
    public void Fails_on_a_soft_failure_before_an_inconclusive_result()
    {
        var result = NestedRun.Run(nameof(SkippingFixture.Soft_then_inconclusive));

        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Failed));
        Assert.That(result.Message, Does.Contain("toHaveText failed: expected text \"the count\""));
        Assert.That(result.Message, Does.Contain("feature disabled"));
    }

    [Test]
    public void Retries_a_soft_failure_before_the_skip_like_any_failure()
    {
        var result = NestedRun.Run(nameof(SkippingFixture.Soft_then_skip_with_retries));

        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Failed));
        Assert.That(SkippingFixture.Attempts, Is.EqualTo(3));
    }

    [Test]
    public void Shows_a_soft_failure_before_the_skip_when_a_retry_then_skips_cleanly()
    {
        var result = NestedRun.Run(nameof(SkippingFixture.Soft_skip_then_clean_skip));

        Assert.That(SkippingFixture.Attempts, Is.EqualTo(2));
        Assert.That(result.ResultState.Status, Is.EqualTo(TestStatus.Skipped));
        // NUnit retries only a failed attempt, so the second attempt shows the first one failed.
        Assert.That(SkippingFixture.Messages[0], Does.Contain("toHaveText failed: expected text \"the count\"").And.Contain("feature disabled"));
        Assert.That(SkippingFixture.Messages[1], Is.EqualTo("feature disabled"));
    }

    [Test]
    public void Keeps_clean_skips_and_passing_soft_assertions_green()
    {
        Assert.That(NestedRun.Run(nameof(SkippingFixture.Clean_skip)).ResultState.Status, Is.EqualTo(TestStatus.Skipped));

        Assert.That(NestedRun.Run(nameof(SkippingFixture.False_condition)).ResultState.Status, Is.EqualTo(TestStatus.Passed));

        SkippingFixture.Attempts = 0;
        var recovered = NestedRun.Run(nameof(SkippingFixture.Soft_failure_then_recovery));
        Assert.That(recovered.ResultState.Status, Is.EqualTo(TestStatus.Passed));
        Assert.That(SkippingFixture.Attempts, Is.EqualTo(2));
    }
}
