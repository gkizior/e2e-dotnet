// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentObservationDeadline;

/// <summary>A document engine whose every capture after a swipe takes a while, and that counts those captures.</summary>
internal sealed class SlowCaptureEngine(DocumentWorld world, TimeSpan captureTime) : IEngine
{
    private readonly DocumentEngine _inner = new(world);
    private readonly TimeSpan _captureTime = captureTime;
    private volatile bool _scrolled;
    private int _captures;

    /// <summary>The captures taken after the swipe.</summary>
    public int Captures => Volatile.Read(ref _captures);

    public string Platform => _inner.Platform;

    public string Version => _inner.Version;

    public EngineCapabilities Capabilities => _inner.Capabilities;

    public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        return new Session(this, await _inner.StartAsync(options, cancellationToken).ConfigureAwait(false));
    }

    private sealed class Session(SlowCaptureEngine engine, IEngineSession inner) : IEngineSession
    {
        public string Route => inner.Route;

        public Task OpenAsync(string url, CancellationToken cancellationToken) => inner.OpenAsync(url, cancellationToken);

        public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            if (engine._scrolled)
            {
                Interlocked.Increment(ref engine._captures);
                await Task.Delay(engine._captureTime, cancellationToken).ConfigureAwait(false);
            }

            return await inner.ObserveAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => inner.PerformAsync(node, action, cancellationToken);

        public Task PressAsync(string key, CancellationToken cancellationToken) => inner.PressAsync(key, cancellationToken);

        public Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken)
        {
            engine._scrolled = true;
            return inner.SwipeAsync(direction, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
