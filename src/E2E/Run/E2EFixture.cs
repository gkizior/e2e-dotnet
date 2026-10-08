// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E;

/// <summary>
/// The part of a test base class that every test framework shares. It holds the settings and
/// starts one <see cref="E2ESession"/> per test. A framework adapter, such as <c>E2E.NUnit.E2ETest</c>,
/// calls <see cref="StartSessionAsync"/> before the test body and commits the session from the
/// framework's result after it.
/// <para>
/// Settings come from <see cref="Config"/>, the nearest <c>e2e.config.json</c> above the
/// test assembly or the working directory. Override a property to change one value for a fixture.
/// </para>
/// </summary>
public abstract class E2EFixture
{
    private static readonly Lazy<E2EConfig> DiscoveredConfig = new(() => E2EConfig.Discover(), LazyThreadSafetyMode.ExecutionAndPublication);

    private E2ESession? _session;

    protected App App => Session.App;

    protected Browser Browser => Session.Browser;

    protected Agent Agent => Session.Agent;

    protected Screen Screen => Session.Screen;

    protected TestContext Context => Session.Context;

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

    /// <summary>Anchors the files a test names, such as a route's fulfill path. Defaults to the config's directory.</summary>
    protected virtual string ProjectRoot => Config.ProjectRoot;

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

    private E2ESession Session => _session ?? throw new InvalidOperationException("The E2E session is available after setup, during the test.");

    /// <summary>
    /// Starts the session for one test. <paramref name="title"/> is the cache identity,
    /// <paramref name="attempt"/> is 1 for a first attempt, <paramref name="onSoftFailure"/> reports
    /// an <c>Expect.Soft</c> failure to the framework (null keeps them on the session), and
    /// <paramref name="testFailed"/> says whether the framework already failed the test.
    /// </summary>
    protected async Task StartSessionAsync(string title, int attempt, Action<TestException>? onSoftFailure, Func<bool> testFailed, CancellationToken cancellationToken)
    {
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
                ProjectRoot = ProjectRoot,
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
                Attempt = attempt,
                OnSoftFailure = onSoftFailure,
                TestFailed = testFailed,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Hands the running session to the adapter to complete and dispose, or null when setup never started one.</summary>
    protected E2ESession? TakeSession()
    {
        var session = _session;
        _session = null;
        return session;
    }
}
