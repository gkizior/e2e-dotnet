// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using NUnit.Framework;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace E2E.NUnit;

/// <summary>
/// Base class for an NUnit test that drives the app. Setup starts one engine
/// session. Teardown commits the replay cache from the NUnit result: a pass
/// or a skip records verified acts, a failure evicts the unverified ones it
/// recorded or replayed, and a cancelled test leaves the cache alone. The first
/// attempt can replay. <c>[Retry]</c> attempts run live and still record. Each
/// <c>[Repeat]</c> iteration is a first attempt. A check in a derived [TearDown]
/// after a failure verifies nothing.
/// Each <c>Expect.Soft</c> failure is recorded on the NUnit result, as inside
/// <c>Assert.EnterMultipleScope</c>, so the test fails when its body ends and
/// lists every soft failure. A soft failure outranks a skip: <c>Assert.Ignore</c> or
/// <c>Assert.Inconclusive</c> after one fails the test, and the skip reason stays in the message.
/// Settings come from <see cref="E2EFixture"/>.
/// </summary>
public abstract class E2ETest : E2EFixture
{
    /// <summary>Cache identity for this test. The default is the NUnit full name, so each test keeps its own replay.</summary>
    protected virtual string CacheTitle(global::NUnit.Framework.TestContext.TestAdapter test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return string.IsNullOrWhiteSpace(test.FullName) ? test.Name : test.FullName;
    }

    /// <summary>Records an <c>Expect.Soft</c> failure on the running NUnit test. The test body runs on and fails when it ends.</summary>
    protected virtual void RecordSoftFailure(TestException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        TestExecutionContext.CurrentContext.CurrentResult.RecordAssertion(AssertionStatus.Failed, failure.Message, failure.StackTrace);
    }

    [SetUp]
    public async Task StartE2ESessionAsync()
    {
        var current = global::NUnit.Framework.TestContext.CurrentContext;
        await StartSessionAsync(CacheTitle(current.Test), AttemptOf(current), RecordSoftFailure, HasFailed, current.CancellationToken).ConfigureAwait(false);
    }

    [TearDown]
    public async Task FinishE2ESessionAsync()
    {
        var session = TakeSession();
        if (session is null)
        {
            return;
        }

        FailSkippedAfterSoftFailure();
        var current = global::NUnit.Framework.TestContext.CurrentContext;
        try
        {
            session.Complete(ErrorForCache(current.Result), current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    // NUnit reports a test that skipped itself as skipped, whatever it recorded before.
    // A failed Expect.Soft before the skip fails the test, as upstream does.
    private static void FailSkippedAfterSoftFailure()
    {
        var result = TestExecutionContext.CurrentContext.CurrentResult;
        if (result.ResultState.Status is not (TestStatus.Skipped or TestStatus.Inconclusive))
        {
            return;
        }

        if (result.AssertionResults.Any(assertion => assertion.Status == AssertionStatus.Failed))
        {
            // NUnit's message already lists the soft failures and the skip reason.
            result.SetResult(ResultState.Failure, result.Message, result.StackTrace);
        }
    }

    // NUnit counts both [Retry] and [Repeat] in CurrentRepeatCount. Only a retry is a later attempt.
    private int AttemptOf(global::NUnit.Framework.TestContext current)
    {
        var name = current.Test.MethodName;
        var retried = name is not null && GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Any(method => string.Equals(method.Name, name, StringComparison.Ordinal) && method.IsDefined(typeof(RetryAttribute), true));
        return retried ? current.CurrentRepeatCount + 1 : 1;
    }

    // A derived [TearDown] runs before this fixture's teardown, after the result of the test body is known.
    private static bool HasFailed()
    {
        return global::NUnit.Framework.TestContext.CurrentContext.Result.Outcome.Status == global::NUnit.Framework.Interfaces.TestStatus.Failed;
    }

    private static Exception? ErrorForCache(global::NUnit.Framework.TestContext.ResultAdapter result)
    {
        var recorded = result.RecordedException;
        return result.Outcome.Status switch
        {
            global::NUnit.Framework.Interfaces.TestStatus.Passed or global::NUnit.Framework.Interfaces.TestStatus.Warning => null,
            global::NUnit.Framework.Interfaces.TestStatus.Skipped => recorded as SkipException ?? new SkipException(string.IsNullOrWhiteSpace(result.Message) ? "skipped" : result.Message),
            _ => recorded ?? new TestException("TEST_FAILED", string.IsNullOrWhiteSpace(result.Message) ? "The test failed." : result.Message),
        };
    }
}
