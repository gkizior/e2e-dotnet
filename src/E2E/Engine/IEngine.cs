// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

/// <summary>Capabilities the harness reads off an engine. An engine never calls a model.</summary>
[Flags]
public enum EngineCapabilities
{
    None = 0,
    Observation = 1,
    Actions = 2,
    Location = 4,
    Keyboard = 8,
}

/// <summary>Passed to <see cref="IEngine.StartAsync"/> once per test attempt.</summary>
public sealed class EngineStartOptions
{
    public string? BaseUrl { get; init; }

    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// The body of one target. Core knows this contract and never an engine's internals.
/// <see cref="Version"/> is the engine contract version recorded in the replay cache
/// (<c>major.minor</c>). Bumping it misses existing recordings.
/// </summary>
public interface IEngine
{
    string Platform { get; }

    string Version { get; }

    EngineCapabilities Capabilities { get; }

    Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken);
}

/// <summary>One attempt's session with the app. Disposed at the end of the attempt, after <c>afterEach</c>.</summary>
public interface IEngineSession : IAsyncDisposable
{
    /// <summary>Path of the current screen, without query or fragment.</summary>
    string Route { get; }

    Task OpenAsync(string url, CancellationToken cancellationToken);

    Task<Observation> ObserveAsync(CancellationToken cancellationToken);

    Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken);

    /// <summary>Sends a key to whatever holds focus. The harness has already validated the key.</summary>
    Task PressAsync(string key, CancellationToken cancellationToken);
}
