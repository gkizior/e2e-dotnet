// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

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

    private E2ESession(AttemptScope scope, CancellationTokenSource timeout, IEngineSession engine, App app, Agent agent, Screen screen, TestContext context)
    {
        _scope = scope;
        _timeout = timeout;
        _engine = engine;
        App = app;
        Agent = agent;
        Screen = screen;
        Context = context;
    }

    public App App { get; }

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

        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (options.TestTimeout > TimeSpan.Zero && options.TestTimeout != Timeout.InfiniteTimeSpan)
        {
            timeout.CancelAfter(options.TestTimeout);
        }

        IEngineSession engine;
        try
        {
            engine = await options.Engine.StartAsync(
                new EngineStartOptions { BaseUrl = options.BaseUrl, ActionTimeout = options.ActionTimeout },
                timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            timeout.Dispose();
            throw;
        }

        var scope = new AttemptScope
        {
            Session = engine,
            Model = options.Model,
            Cache = options.Cache,
            CacheEnabled = options.CacheEnabled && options.Attempt <= 1,
            TestTitle = options.TestTitle,
            EnginePlatform = options.Engine.Platform,
            EngineVersion = options.Engine.Version,
            Attempt = options.Attempt,
            ActionTimeout = options.ActionTimeout,
            StepTimeout = options.StepTimeout,
            MaxModelCalls = options.MaxModelCalls,
            Token = () => timeout.Token,
        };
        var app = new App(engine, options.BaseUrl, () => timeout.Token);
        var agent = new Agent(scope);
        var screen = new Screen(
            token => engine.ObserveAsync(token),
            (node, action, token) => engine.PerformAsync(node, action, token),
            () => timeout.Token,
            scope.MarkVerified,
            options.AssertionTimeout);
        var context = new TestContext
        {
            App = app,
            Agent = agent,
            Screen = screen,
            CancellationToken = timeout.Token,
        };
        return new E2ESession(scope, timeout, engine, app, agent, screen, context);
    }

    /// <summary>
    /// Writes verified acts and deletes unverified ones. A <see cref="SkipException"/>,
    /// or a model outage, leaves existing entries in place. A cancelled
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

        await _engine.DisposeAsync().ConfigureAwait(false);
        _timeout.Dispose();
    }

    private void Commit(Exception? error, CancellationToken cancellationToken)
    {
        if (!_scope.CacheEnabled || _scope.Cache is null || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        var preserve = error is SkipException || error is AgentException { Code: "MODEL_UNAVAILABLE" or "MODEL_PROVIDER_FAILED" };
        foreach (var act in _scope.Acts)
        {
            if (act.Verified && act.Entry is { Actions.Count: > 0 } && !act.ParamCollision)
            {
                _scope.Cache.Write(act.Key, act.Entry);
            }
            else if (!preserve)
            {
                _scope.Cache.Delete(act.Key);
            }
        }
    }
}

/// <summary>How to open one <see cref="E2ESession"/>. Retries set <see cref="Attempt"/> above 1 so the cache stays off.</summary>
public sealed class E2ESessionOptions
{
    public required IEngine Engine { get; init; }

    public IAgentModel? Model { get; init; }

    public string? BaseUrl { get; init; }

    public IStepCache? Cache { get; init; }

    public bool CacheEnabled { get; init; } = true;

    public required string TestTitle { get; init; }

    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxModelCalls { get; init; } = 12;

    /// <summary>1 is the first try. Later attempts do not read or write the replay cache.</summary>
    public int Attempt { get; init; } = 1;
}
