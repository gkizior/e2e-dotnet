// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace E2E.XUnit.V3;

/// <summary>
/// Base class for an xUnit v3 test that drives the app. <see cref="InitializeAsync"/> starts one
/// engine session. <see cref="DisposeAsync"/> commits the replay cache from the xUnit result: a
/// pass or a skip records verified acts, a failure evicts the unverified ones it recorded or
/// replayed, and a cancelled test leaves the cache alone. xUnit has no retry, so every test is a
/// first attempt and can replay. A check in a derived <c>DisposeAsync</c> after a failure verifies nothing.
/// Each <c>Expect.Soft</c> failure is kept on the session. The test body runs on, and the test
/// fails when it is disposed, with every soft failure in one message.
/// Settings come from <see cref="E2EFixture"/>.
/// </summary>
public abstract class E2ETest : E2EFixture, IAsyncLifetime
{
    /// <summary>
    /// Cache identity for this test. The default is the class and method name, with every theory
    /// argument in full, so each test and theory row keeps its own replay. The xUnit display name
    /// is not used, because xUnit shortens long arguments in it and a display setting can change it.
    /// </summary>
    protected virtual string CacheTitle(ITest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        var name = test.TestCase.TestClassName + "." + test.TestCase.TestMethodName;
        return test is IXunitTest { TestMethodArguments.Length: > 0 } row
            ? name + "(" + string.Join(", ", row.TestMethodArguments.Select(argument => Convert.ToString(argument, CultureInfo.InvariantCulture))) + ")"
            : name;
    }

    public virtual async ValueTask InitializeAsync()
    {
        var current = Xunit.TestContext.Current;
        var test = current.Test ?? throw new InvalidOperationException("The E2E session starts inside a running xUnit test.");
        await StartSessionAsync(CacheTitle(test), 1, null, HasFailed, current.CancellationToken).ConfigureAwait(false);
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        var session = TakeSession();
        if (session is null)
        {
            return;
        }

        var current = Xunit.TestContext.Current;
        var soft = session.CloseSoftFailures();
        try
        {
            session.Complete(ErrorForCache(current.TestState, soft), current.CancellationToken);
        }
        finally
        {
            await DisposeSessionAsync(session, soft).ConfigureAwait(false);
        }

        if (soft is not null)
        {
            throw soft;
        }
    }

    // A cleanup error must not hide the soft failures, so both are reported.
    private static async Task DisposeSessionAsync(E2ESession session, TestException? soft)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (soft is not null)
        {
            throw new AggregateException(soft, ex);
        }
    }

    // The xUnit result is set when the test body ends, before the test class is disposed.
    private static bool HasFailed()
    {
        return Xunit.TestContext.Current.TestState?.Result == TestResult.Failed;
    }

    // xUnit keeps only the failure's type and message. The session itself knows when an agent call hit
    // a model outage or a stale replay, so the cache still keeps its entries then.
    private static Exception? ErrorForCache(TestResultState? state, TestException? soft)
    {
        // A soft failure outranks a skip: xUnit fails the test at dispose, so the cache sees a failure.
        if (state?.Result is TestResult.Passed || (soft is not null && state?.Result is TestResult.Skipped))
        {
            return soft;
        }

        var type = state?.ExceptionTypes?.FirstOrDefault();
        var message = state?.ExceptionMessages?.FirstOrDefault();
        if (state?.Result == TestResult.Skipped || string.Equals(type, typeof(SkipException).FullName, StringComparison.Ordinal))
        {
            return new SkipException(string.IsNullOrWhiteSpace(message) ? "skipped" : message);
        }

        return new TestException("TEST_FAILED", string.IsNullOrWhiteSpace(message) ? "The test failed." : message);
    }
}
