// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
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

    /// <summary>The viewport swipe, <see cref="LocatorAction.Swipe"/>, and <see cref="LocatorAction.ScrollIntoView"/>.</summary>
    Scroll = 16,

    /// <summary><see cref="IEngineSession.BackAsync"/>.</summary>
    History = 32,
}

/// <summary>Passed to <see cref="IEngine.StartAsync"/> once per test attempt.</summary>
public sealed class EngineStartOptions
{
    public string? BaseUrl { get; init; }

    public TimeSpan ActionTimeout { get; init; } = E2EDefaults.ActionTimeout;
}

/// <summary>
/// The body of one target. Core knows this contract and never an engine's internals.
/// <see cref="Version"/> is the engine version. It is not part of the replay cache key:
/// a recording re-finds its nodes at replay.
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

    /// <summary>
    /// Scrolls the viewport about three quarters of a screen. Engines that declare
    /// <see cref="EngineCapabilities.Scroll"/> implement it.
    /// </summary>
    Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken) => Unsupported("scroll");

    /// <summary>
    /// Goes back one entry in the session history. With no earlier entry it does nothing.
    /// Engines that declare <see cref="EngineCapabilities.History"/> implement it.
    /// </summary>
    Task BackAsync(CancellationToken cancellationToken) => Unsupported("app.back");

    /// <summary>Closes the current document and opens a blank one, keeping cookies and storage.</summary>
    Task RestartAsync(CancellationToken cancellationToken) => Unsupported("app.restart");

    /// <summary>Discards cookies, storage, and history, and opens a blank document.</summary>
    Task ClearStateAsync(CancellationToken cancellationToken) => Unsupported("app.clearState");

    private static Task Unsupported(string operation) =>
        Task.FromException(new EngineException("UNSUPPORTED_CAPABILITY", operation + " is not supported by this engine."));
}
