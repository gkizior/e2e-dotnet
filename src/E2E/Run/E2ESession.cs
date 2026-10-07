// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// One test attempt: an engine session, the fixtures, and the replay cache.
/// Call <see cref="Complete"/> after the test body. Disposing without
/// <see cref="Complete"/> records the attempt as failed.
/// </summary>
public sealed class E2ESession : IAsyncDisposable
{
    private readonly AttemptScope _scope;
    private readonly CancellationTokenSource _timeout;
    private readonly IEngineSession _engine;
    private int _state;
    private int _disposed;

    private E2ESession(AttemptScope scope, CancellationTokenSource timeout, IEngineSession engine, App app, Browser browser, Agent agent, Screen screen, TestContext context)
    {
        _scope = scope;
        _timeout = timeout;
        _engine = engine;
        App = app;
        Browser = browser;
        Agent = agent;
        Screen = screen;
        Context = context;
    }

    public App App { get; }

    public Browser Browser { get; }

    public Agent Agent { get; }

    public Screen Screen { get; }

    public TestContext Context { get; }

    public int Replayed => _scope.Replayed;

    public int HandedOff => _scope.HandedOff;

    public int Missed => _scope.Missed;

    public int ModelCalls => _scope.ModelCalls;

    public int InputTokens => _scope.InputTokens;

    public int OutputTokens => _scope.OutputTokens;

    public static async Task<E2ESession> StartAsync(E2ESessionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Engine);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.TestTitle);
        var agents = ResolveAgents(options);

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.TestTimeout > TimeSpan.Zero && options.TestTimeout != Timeout.InfiniteTimeSpan)
        {
            timeout.CancelAfter(options.TestTimeout);
        }

        IEngineSession engine;
        var launch = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        try
        {
            if (Bounded(options.LaunchTimeout))
            {
                launch.CancelAfter(options.LaunchTimeout);
            }

            engine = await options.Engine.StartAsync(
                new EngineStartOptions { BaseUrl = options.BaseUrl, ActionTimeout = options.ActionTimeout },
                launch.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (launch.IsCancellationRequested && !timeout.IsCancellationRequested)
        {
            timeout.Dispose();
            throw new EngineException(
                "ENVIRONMENT_UNAVAILABLE",
                "The engine did not start within the launch timeout (" + options.LaunchTimeout.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms).",
                ex);
        }
        catch
        {
            timeout.Dispose();
            throw;
        }
        finally
        {
            launch.Dispose();
        }

        // A later attempt never replays but still records, so the attempt number does not turn the cache off.
        var cacheOn = options.CacheEnabled && options.CacheMode != CacheMode.Off && options.Cache is not null;
        var scope = new AttemptScope
        {
            Session = engine,
            Agents = agents,
            Cache = options.Cache,
            CacheEnabled = cacheOn,
            CacheWrite = cacheOn && options.CacheMode == CacheMode.ReadWrite,
            CacheStrict = options.CacheStrict,
            CleanupTimeout = options.CleanupTimeout,
            TestTitle = options.TestTitle,
            EnginePlatform = options.Engine.Platform,
            EngineCapabilities = options.Engine.Capabilities,
            Attempt = options.Attempt,
            ActionTimeout = options.ActionTimeout,
            ReplayTimeout = options.ReplayTimeout,
            StepTimeout = options.StepTimeout,
            Token = () => timeout.Token,
            TestFailed = options.TestFailed ?? (static () => false),
        };
        var app = new App(engine, options.BaseUrl, () => timeout.Token);
        var browser = new Browser(engine, options.Engine.Platform, options.BaseUrl, options.AssertionTimeout, () => timeout.Token);
        var agent = new Agent(scope);
        var screen = new Screen(
            token => engine.ObserveAsync(token),
            (node, action, token) => engine.PerformAsync(node, action, token),
            () => timeout.Token,
            scope.MarkVerified,
            options.ActionTimeout,
            options.AssertionTimeout,
            new SoftFailures(options.OnSoftFailure));
        var context = new TestContext
        {
            App = app,
            Browser = browser,
            Platform = options.Engine.Platform,
            Agent = agent,
            Screen = screen,
            CancellationToken = timeout.Token,
        };
        return new E2ESession(scope, timeout, engine, app, browser, agent, screen, context);
    }

    /// <summary>
    /// Ends <c>expect.soft</c> collection and returns one <c>ASSERTION_FAILED</c>
    /// for every failure kept since the session started, or null. Call it when
    /// the test body settles and fail the test with the result. Later soft
    /// matchers throw as hard ones would.
    /// </summary>
    public TestException? CloseSoftFailures() => Screen.SoftFailures.Close();

    /// <summary>
    /// Writes verified acts and evicts the unverified ones this attempt recorded or
    /// replayed. A passed or skipped attempt confirms only what a later check verified.
    /// An act that missed the cache and never passed leaves its key alone. An entry a
    /// verified replay finished, or one that already holds the same flow, is not
    /// rewritten. A model outage leaves existing entries in place. A cancelled
    /// <paramref name="cancellationToken"/> writes nothing.
    /// </summary>
    public void Complete(Exception? error = null, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            return;
        }

        try
        {
            Commit(error, cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _state, 2);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
        {
            try
            {
                Commit(new TestException("TEST_FAILED", "The session ended before it was completed."), CancellationToken.None);
            }
            finally
            {
                Volatile.Write(ref _state, 2);
            }
        }

        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            var cleanup = _engine.DisposeAsync().AsTask();
            if (Bounded(_scope.CleanupTimeout))
            {
                await cleanup.WaitAsync(_scope.CleanupTimeout).ConfigureAwait(false);
            }
            else
            {
                await cleanup.ConfigureAwait(false);
            }
        }
        catch (TimeoutException ex)
        {
            throw new EngineException(
                "ENVIRONMENT_UNAVAILABLE",
                "The engine did not shut down within the cleanup timeout (" + _scope.CleanupTimeout.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + " ms).",
                ex);
        }
        finally
        {
            _timeout.Dispose();
        }
    }

    // The session's own agent settings are the default agent. Agents names the others.
    private static Dictionary<string, ResolvedAgent> ResolveAgents(E2ESessionOptions options)
    {
        var agents = new Dictionary<string, ResolvedAgent>(StringComparer.Ordinal)
        {
            ["default"] = ResolvedAgent.From("default", new AgentOptions
            {
                Model = options.Model,
                Judge = options.Judge,
                System = options.AgentSystem,
                Context = options.AgentContext,
                MaxSteps = options.MaxSteps,
                MaxModelCalls = options.MaxModelCalls,
                JudgmentTimeout = options.JudgmentTimeout,
                ProviderOptions = options.ProviderOptions,
            }),
        };
        if (options.Agents is null)
        {
            return agents;
        }

        foreach (var pair in options.Agents)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ConfigurationException("INVALID_CONFIG", "agent names must be non-empty");
            }

            if (string.Equals(pair.Key, "default", StringComparison.Ordinal))
            {
                throw new ConfigurationException(
                    "INVALID_CONFIG",
                    "Agents cannot hold \"default\"; the session's Model, Judge, AgentSystem, AgentContext, MaxSteps, MaxModelCalls, JudgmentTimeout, and ProviderOptions are the default agent");
            }

            agents[pair.Key] = ResolvedAgent.From(pair.Key, pair.Value);
        }

        return agents;
    }

    private static bool Bounded(TimeSpan timeout) => timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan;

    private void Commit(Exception? error, CancellationToken cancellationToken)
    {
        if (!_scope.CacheWrite || _scope.Cache is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // A stale recording under strict mode stays in place, so the next strict run fails the same way until someone re-records it.
        var preserve = error is AgentException { Code: "MODEL_UNAVAILABLE" or "MODEL_PROVIDER_FAILED" or "REPLAY_STALE" };
        foreach (var act in _scope.Acts)
        {
            var recorded = act.Completed && act.Entry is { Actions.Count: > 0 } && !act.ParamCollision;
            if (act.Verified && recorded)
            {
                if (!act.ReplayedWhole && !HoldsSameFlow(_scope.Cache, act.Key, act.Entry!))
                {
                    _scope.Cache.Write(act.Key, act.Entry!);
                }
            }
            else if (!preserve && (recorded || act.ConsumedReplay))
            {
                _scope.Cache.Delete(act.Key);
            }
        }
    }

    // Rewriting an identical flow would only churn a committed cache directory.
    private static bool HoldsSameFlow(IStepCache cache, string key, CacheEntry entry)
    {
        var existing = cache.Read(key).Entry;
        return existing is not null
            && string.Equals(
                JsonSerializer.Serialize(existing, JsonDefaults.Options),
                JsonSerializer.Serialize(entry, JsonDefaults.Options),
                StringComparison.Ordinal);
    }
}

/// <summary>How to open one <see cref="E2ESession"/>. Retries set <see cref="Attempt"/> above 1 so the cache records but does not replay.</summary>
public sealed class E2ESessionOptions
{
    public required IEngine Engine { get; init; }

    /// <summary>The default agent's model.</summary>
    public IAgentModel? Model { get; init; }

    /// <summary>The default agent's judge for <c>assert</c>, <c>waitFor</c>, and <c>extract</c>. Defaults to <see cref="Model"/>.</summary>
    public IAgentModel? Judge { get; init; }

    /// <summary>The default agent's upstream <c>system</c>: text appended to the act rules. Judges never see it.</summary>
    public string? AgentSystem { get; init; }

    /// <summary>The default agent's upstream <c>context</c>: told to every model call, at most 16384 UTF-8 bytes.</summary>
    public string? AgentContext { get; init; }

    /// <summary>The default agent's <c>judgmentTimeout</c>: the deadline of one <c>assert</c>, <c>waitFor</c>, or <c>extract</c>.</summary>
    public TimeSpan JudgmentTimeout { get; init; } = E2EDefaults.JudgmentTimeout;

    /// <summary>The default agent's provider options, keyed by provider. See <see cref="ModelRequest.ProviderOptions"/>.</summary>
    public IReadOnlyDictionary<string, JsonElement>? ProviderOptions { get; init; }

    /// <summary>
    /// Named agents besides the default one, picked per call with the <c>Agent</c> option.
    /// Each starts from the built-in defaults, not from the default agent.
    /// </summary>
    public IReadOnlyDictionary<string, AgentOptions>? Agents { get; init; }

    public string? BaseUrl { get; init; }

    public IStepCache? Cache { get; init; }

    public bool CacheEnabled { get; init; } = true;

    /// <summary><see cref="CacheMode.ReadOnly"/> replays but never writes or deletes recordings.</summary>
    public CacheMode CacheMode { get; init; } = CacheMode.ReadWrite;

    /// <summary>When true, a recording that no longer matches fails the act with <c>REPLAY_STALE</c>.</summary>
    public bool CacheStrict { get; init; }

    /// <summary>How long the engine may take to start. Upstream <c>launchTimeout</c>.</summary>
    public TimeSpan LaunchTimeout { get; init; } = E2EDefaults.LaunchTimeout;

    /// <summary>How long the engine may take to shut down on dispose. Upstream <c>cleanupTimeout</c>.</summary>
    public TimeSpan CleanupTimeout { get; init; } = E2EDefaults.CleanupTimeout;

    public required string TestTitle { get; init; }

    public TimeSpan TestTimeout { get; init; } = E2EDefaults.TestTimeout;

    public TimeSpan ActionTimeout { get; init; } = E2EDefaults.ActionTimeout;

    public TimeSpan AssertionTimeout { get; init; } = E2EDefaults.AssertionTimeout;

    /// <summary>How long one <c>ActAsync</c> may run unless <see cref="ActOptions.Timeout"/> says otherwise. Judgments use <see cref="JudgmentTimeout"/>.</summary>
    public TimeSpan StepTimeout { get; init; } = E2EDefaults.StepTimeout;

    /// <summary>How long a replay waits for each recorded target and for the recorded end state.</summary>
    public TimeSpan ReplayTimeout { get; init; } = E2EDefaults.ReplayTimeout;

    /// <summary>The default agent's model calls per agent call, 1 through 100.</summary>
    public int MaxModelCalls { get; init; } = E2EDefaults.MaxModelCalls;

    /// <summary>Actions one <c>ActAsync</c> may take, 1 through 100. <see cref="ActOptions.MaxSteps"/> can only lower it.</summary>
    public int MaxSteps { get; init; } = E2EDefaults.MaxSteps;

    /// <summary>
    /// Receives each <c>expect.soft</c> failure as it happens. Null keeps them
    /// on the session until <see cref="E2ESession.CloseSoftFailures"/>.
    /// </summary>
    public Action<TestException>? OnSoftFailure { get; init; }

    /// <summary>
    /// Reports whether the test has already failed. Once it returns true, a passing check no longer
    /// verifies acts, so a teardown assertion after a failure does not record them.
    /// </summary>
    public Func<bool>? TestFailed { get; init; }

    /// <summary>1 is the first try. Later attempts do not replay, and still record verified acts.</summary>
    public int Attempt { get; init; } = 1;
}
