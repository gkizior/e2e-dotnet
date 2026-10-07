// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.ExceptionServices;
using E2E.Engine;
using Xunit;
using Xunit.Sdk;

namespace E2E.Xunit;

/// <summary>
/// Base class for an xUnit v3 test that drives the app. <c>InitializeAsync</c> starts one
/// engine session. <c>DisposeAsync</c> commits the replay cache from the xUnit result: a pass
/// or a skip records verified acts, a failure evicts the unverified ones it recorded or
/// replayed, and a cancelled test leaves the cache alone. xUnit has no retry, so every run is
/// a first attempt and can replay. A check in a derived <c>DisposeAsync</c> after a failure
/// verifies nothing; override it and call the base last.
/// Every <c>Expect.Soft</c> failure is kept until the test body ends, then
/// <see cref="ReportSoftFailures"/> fails the test with one error that lists them all.
/// <para>
/// Settings come from <see cref="Config"/>, the nearest <c>e2e.config.json</c> above the
/// test assembly or the working directory. Override a property to change one value for a class.
/// </para>
/// </summary>
public abstract class E2ETest : IAsyncLifetime
{
    private static readonly Lazy<E2EConfig> DiscoveredConfig = new(() => E2EConfig.Discover(), LazyThreadSafetyMode.ExecutionAndPublication);

    private E2ESession? _session;
    private ITest? _test;
    private E2EException? _thrown;

    protected App App => Session.App;

    protected Browser Browser => Session.Browser;

    protected Agent Agent => Session.Agent;

    protected Screen Screen => Session.Screen;

    protected E2E.TestContext Context => Session.Context;

    /// <summary>The discovered <c>e2e.config.json</c>, or the defaults when there is none. Loaded once per test run.</summary>
    protected virtual E2EConfig Config => DiscoveredConfig.Value;

    /// <summary>Secrets declared in the config, with <c>E2E_SECRET_{NAME}</c> overrides.</summary>
    protected Secrets Secrets => Config.Secrets;

    /// <summary>Browser engine. Override to supply a <see cref="DocumentEngine"/> or a custom engine.</summary>
    protected virtual IEngine CreateEngine() => new WebEngine();

    /// <summary>The model for <c>agents.default</c>, or null when the config sets no model.</summary>
    protected virtual IAgentModel? CreateModel() => Config.Agent.CreateModel();

    /// <summary>The judge for <c>agents.default</c>: <c>agents.default.judge</c>, or null to judge with <see cref="CreateModel"/>.</summary>
    protected virtual IAgentModel? CreateJudge() => Config.Agent.CreateJudge();

    /// <summary>
    /// The named agents besides <c>default</c>, picked per call with the <c>Agent</c> option.
    /// Defaults to every other <c>agents.&lt;name&gt;</c> entry in the config.
    /// </summary>
    protected virtual IReadOnlyDictionary<string, AgentOptions> CreateAgents()
    {
        return Config.Agents
            .Where(pair => !string.Equals(pair.Key, "default", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value.CreateOptions(), StringComparer.Ordinal);
    }

    /// <summary><c>agents.default.system</c>: text appended to the act rules.</summary>
    protected virtual string? AgentSystem => Config.Agent.System;

    /// <summary><c>agents.default.context</c>: project context told to every model call.</summary>
    protected virtual string? AgentContext => Config.Agent.Context;

    /// <summary><c>agents.default.judgmentTimeout</c>.</summary>
    protected virtual TimeSpan JudgmentTimeout => Config.Agent.JudgmentTimeout;

    /// <summary><c>agents.default.providerOptions</c>.</summary>
    protected virtual IReadOnlyDictionary<string, System.Text.Json.JsonElement>? ProviderOptions => Config.Agent.ProviderOptions;

    /// <summary><c>targets[0].app.url</c>.</summary>
    protected virtual string? BaseUrl => Config.Target.App.Url;

    /// <summary><c>cache.dir</c>, resolved against the config directory.</summary>
    protected virtual string CacheDirectory => Config.Cache.Directory;

    /// <summary><c>cache.mode</c>. Unset, it is read-write locally and read-only in CI.</summary>
    protected virtual CacheMode CacheMode => Config.Cache.Mode;

    /// <summary><c>cache.strict</c>.</summary>
    protected virtual bool CacheStrict => Config.Cache.Strict;

    protected virtual TimeSpan TestTimeout => Config.Timeout;

    protected virtual TimeSpan LaunchTimeout => Config.LaunchTimeout;

    protected virtual TimeSpan ActionTimeout => Config.ActionTimeout;

    protected virtual TimeSpan AssertionTimeout => Config.AssertionTimeout;

    protected virtual TimeSpan CleanupTimeout => Config.CleanupTimeout;

    /// <summary>How long one <c>ActAsync</c> may run unless its options say otherwise. A .NET-only setting.</summary>
    protected virtual TimeSpan StepTimeout => E2EDefaults.StepTimeout;

    /// <summary>How long a replay waits for each recorded target and for the recorded end state.</summary>
    protected virtual TimeSpan ReplayTimeout => E2EDefaults.ReplayTimeout;

    /// <summary><c>agents.default.maxModelCalls</c>.</summary>
    protected virtual int MaxModelCalls => Config.Agent.MaxModelCalls;

    /// <summary><c>agents.default.maxSteps</c>: actions one <c>ActAsync</c> may take. <c>ActOptions.MaxSteps</c> can only lower it.</summary>
    protected virtual int MaxSteps => Config.Agent.MaxSteps;

    /// <summary>Cache identity for this test. The default is the xUnit display name, so each test keeps its own replay.</summary>
    protected virtual string CacheTitle(ITest test)
    {
        ArgumentNullException.ThrowIfNull(test);
        return test.TestDisplayName;
    }

    /// <summary>
    /// Fails the test with the <c>ASSERTION_FAILED</c> that lists every <c>Expect.Soft</c> failure.
    /// It runs once the test body has ended and the cache has been committed.
    /// </summary>
    protected virtual void ReportSoftFailures(TestException failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        throw failures;
    }

    private E2ESession Session => _session ?? throw new InvalidOperationException("The E2E session is available after InitializeAsync, during the test.");

    public virtual async ValueTask InitializeAsync()
    {
        var current = global::Xunit.TestContext.Current;
        var test = current.Test ?? throw new InvalidOperationException("E2ETest starts its session inside a running xUnit test.");
        var title = CacheTitle(test);
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("CacheTitle returned an empty test name.");
        }

        var cacheMode = CacheMode;
        _session = await E2ESession.StartAsync(
            new E2ESessionOptions
            {
                Engine = CreateEngine(),
                Model = CreateModel(),
                Judge = CreateJudge(),
                AgentSystem = AgentSystem,
                AgentContext = AgentContext,
                JudgmentTimeout = JudgmentTimeout,
                ProviderOptions = ProviderOptions,
                Agents = CreateAgents(),
                BaseUrl = BaseUrl,
                Cache = cacheMode == CacheMode.Off ? null : new FileStepCache(CacheDirectory),
                CacheEnabled = cacheMode != CacheMode.Off,
                CacheMode = cacheMode,
                CacheStrict = CacheStrict,
                TestTitle = title,
                TestTimeout = TestTimeout,
                LaunchTimeout = LaunchTimeout,
                CleanupTimeout = CleanupTimeout,
                ActionTimeout = ActionTimeout,
                AssertionTimeout = AssertionTimeout,
                StepTimeout = StepTimeout,
                ReplayTimeout = ReplayTimeout,
                MaxModelCalls = MaxModelCalls,
                MaxSteps = MaxSteps,
                TestFailed = HasFailed,
            },
            current.CancellationToken).ConfigureAwait(false);
        _test = test;
        AppDomain.CurrentDomain.FirstChanceException += RememberThrown;
    }

    public virtual async ValueTask DisposeAsync()
    {
        AppDomain.CurrentDomain.FirstChanceException -= RememberThrown;
        var session = _session;
        _session = null;
        if (session is null)
        {
            return;
        }

        var current = global::Xunit.TestContext.Current;
        var soft = session.CloseSoftFailures();
        try
        {
            session.Complete(ErrorForCache(current.TestState) ?? soft, current.CancellationToken);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }

        GC.SuppressFinalize(this);
        if (soft is not null)
        {
            ReportSoftFailures(soft);
        }
    }

    // xUnit sets the test state when the body ends, so a derived DisposeAsync sees it before this one runs.
    private static bool HasFailed()
    {
        return global::Xunit.TestContext.Current.TestState?.Result == TestResult.Failed;
    }

    private Exception? ErrorForCache(TestResultState? state)
    {
        return state?.Result switch
        {
            TestResult.Passed => null,
            TestResult.Skipped => new E2E.SkipException("skipped"),
            _ => ThrownBy(state) ?? new TestException("TEST_FAILED", state?.ExceptionMessages is [var message, ..] && !string.IsNullOrWhiteSpace(message) ? message : "The test failed."),
        };
    }

    // The test state holds the failure as text, but the cache keeps its entries on a model
    // outage only when it sees the E2E code, so the exception itself is caught as it is thrown.
    private E2EException? ThrownBy(TestResultState? state)
    {
        var thrown = _thrown;
        return thrown is not null
            && state?.ExceptionTypes is [var type, ..]
            && state.ExceptionMessages is [var message, ..]
            && string.Equals(type, thrown.GetType().FullName, StringComparison.Ordinal)
            && string.Equals(message, thrown.Message, StringComparison.Ordinal)
                ? thrown
                : null;
    }

    private void RememberThrown(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (args.Exception is E2EException error && ReferenceEquals(global::Xunit.TestContext.Current.Test, _test))
        {
            _thrown = error;
        }
    }
}
