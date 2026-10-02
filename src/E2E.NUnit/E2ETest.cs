// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using NUnit.Framework;

namespace E2E.NUnit;

/// <summary>
/// Base class for an NUnit test that drives the app. Setup starts one engine
/// session. Teardown commits the replay cache from the NUnit result: a pass
/// records verified acts, a failure deletes unverified ones, and a skip or a
/// cancelled test leaves the cache alone. The first attempt can replay. Retries
/// run live.
/// </summary>
public abstract class E2ETest
{
    private E2ESession? _session;

    protected App App => Session.App;

    protected Agent Agent => Session.Agent;

    protected Screen Screen => Session.Screen;

    protected E2E.TestContext Context => Session.Context;

    /// <summary>Browser engine. Override to supply a <see cref="DocumentEngine"/> or a custom engine.</summary>
    protected virtual IEngine CreateEngine() => new WebEngine();

    protected virtual IAgentModel? CreateModel() => null;

    protected virtual string? BaseUrl => null;

    protected virtual string CacheDirectory => Path.Combine(Directory.GetCurrentDirectory(), ".e2e", "cache");

    protected virtual bool CacheEnabled => true;

    protected virtual TimeSpan TestTimeout => TimeSpan.FromSeconds(60);

    protected virtual TimeSpan ActionTimeout => TimeSpan.FromSeconds(5);

    protected virtual TimeSpan AssertionTimeout => TimeSpan.FromSeconds(5);

    protected virtual TimeSpan StepTimeout => TimeSpan.FromSeconds(30);

    protected virtual int MaxModelCalls => 12;

    /// <summary>Cache identity for this test. The default is the NUnit full name, so each test keeps its own replay.</summary>
    protected virtual string CacheTitle(global::NUnit.Framework.TestContext.TestAdapter test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return string.IsNullOrWhiteSpace(test.FullName) ? test.Name : test.FullName;
    }

    private E2ESession Session => _session ?? throw new InvalidOperationException("The E2E session is available after setup, during the test.");

    [SetUp]
    public async Task StartE2ESessionAsync()
    {
        var current = global::NUnit.Framework.TestContext.CurrentContext;
        var attempt = current.CurrentRepeatCount + 1;
        var engine = CreateEngine();
        var title = CacheTitle(current.Test);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("CacheTitle returned an empty test name.");
        }

        _session = await E2ESession.StartAsync(
            new E2ESessionOptions
            {
                Engine = engine,
                Model = CreateModel(),
                BaseUrl = BaseUrl,
                Cache = CacheEnabled ? new FileStepCache(CacheDirectory) : null,
                CacheEnabled = CacheEnabled && attempt == 1,
                TestTitle = title,
                TestTimeout = TestTimeout,
                ActionTimeout = ActionTimeout,
                AssertionTimeout = AssertionTimeout,
                StepTimeout = StepTimeout,
                MaxModelCalls = MaxModelCalls,
                Attempt = attempt,
            },
            current.CancellationToken).ConfigureAwait(false);
    }

    [TearDown]
    public async Task FinishE2ESessionAsync()
    {
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

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
