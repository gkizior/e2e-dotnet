// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Agent;

/// <summary>A document engine that counts every observation the agent takes.</summary>
internal sealed class ObservationCountingEngine(DocumentWorld world) : IEngine
{
    private readonly DocumentEngine _inner = new(world);
    private int _observations;

    public int Observations => Volatile.Read(ref _observations);

    public string Platform => _inner.Platform;

    public string Version => _inner.Version;

    public EngineCapabilities Capabilities => _inner.Capabilities;

    public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        return new Session(this, await _inner.StartAsync(options, cancellationToken).ConfigureAwait(false));
    }

    private sealed class Session(ObservationCountingEngine engine, IEngineSession inner) : IEngineSession
    {
        public string Route => inner.Route;

        public Task OpenAsync(string url, CancellationToken cancellationToken) => inner.OpenAsync(url, cancellationToken);

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref engine._observations);
            return inner.ObserveAsync(cancellationToken);
        }

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => inner.PerformAsync(node, action, cancellationToken);

        public Task PressAsync(string key, CancellationToken cancellationToken) => inner.PressAsync(key, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
