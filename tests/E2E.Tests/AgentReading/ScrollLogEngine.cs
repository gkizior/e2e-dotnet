// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentReading;

/// <summary>A document engine that logs every scroll into view, and counts every viewport swipe and every list swipe.</summary>
internal sealed class ScrollLogEngine(DocumentWorld world) : IEngine
{
    private readonly DocumentEngine _inner = new(world);

    /// <summary>The name, or the text of a node with no name, of each node brought into view.</summary>
    public List<string?> ScrolledIntoView { get; } = [];

    public int ViewportSwipes { get; private set; }

    public int ListSwipes { get; private set; }

    public string Platform => _inner.Platform;

    public string Version => _inner.Version;

    public EngineCapabilities Capabilities => _inner.Capabilities;

    public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        return new Session(this, await _inner.StartAsync(options, cancellationToken).ConfigureAwait(false));
    }

    private sealed class Session(ScrollLogEngine engine, IEngineSession inner) : IEngineSession
    {
        public string Route => inner.Route;

        public Task OpenAsync(string url, CancellationToken cancellationToken) => inner.OpenAsync(url, cancellationToken);

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken) => inner.ObserveAsync(cancellationToken);

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
        {
            switch (action)
            {
                case LocatorAction.ScrollIntoView:
                    engine.ScrolledIntoView.Add(node.Name ?? node.Text);
                    break;
                case LocatorAction.Swipe:
                    engine.ListSwipes++;
                    break;
            }

            return inner.PerformAsync(node, action, cancellationToken);
        }

        public Task PressAsync(string key, CancellationToken cancellationToken) => inner.PressAsync(key, cancellationToken);

        public Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken)
        {
            engine.ViewportSwipes++;
            return inner.SwipeAsync(direction, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
