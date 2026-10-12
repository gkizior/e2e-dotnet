// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ScriptedScreen;

/// <summary>
/// The engine over a <see cref="ScriptedScene"/>, for tests that run through a session: it opens the scene on
/// <c>OpenAsync</c> and records every call the session's hooks make, in order.
/// </summary>
internal sealed class ScriptedEngine(ScriptedScene scene) : IEngine
{
    /// <summary>The session hooks the app fixture reached, as <c>open &lt;url&gt;</c>, <c>back</c>, <c>restart</c>, and <c>reset</c>.</summary>
    public List<string> SessionCalls { get; } = [];

    public string Platform => "scripted";

    public string Version => "1";

    public EngineCapabilities Capabilities =>
        EngineCapabilities.Observation | EngineCapabilities.Actions | EngineCapabilities.Location
        | EngineCapabilities.Scroll | EngineCapabilities.History;

    public Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken) =>
        Task.FromResult<IEngineSession>(new Session(scene, SessionCalls));

    private sealed class Session(ScriptedScene scene, List<string> calls) : IEngineSession
    {
        public string Route => "/";

        public Task OpenAsync(string url, CancellationToken cancellationToken)
        {
            calls.Add("open " + url);
            scene.Open();
            return Task.CompletedTask;
        }

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken) => Task.FromResult(scene.Observe());

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
        {
            scene.Perform(node.Ref, action);
            return Task.CompletedTask;
        }

        public Task PressAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task BackAsync(CancellationToken cancellationToken)
        {
            calls.Add("back");
            return Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            calls.Add("restart");
            return Task.CompletedTask;
        }

        public Task ClearStateAsync(CancellationToken cancellationToken)
        {
            calls.Add("reset");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
