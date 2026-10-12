// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Mobile.Eas;

/// <summary>What <see cref="EasSimulators"/> takes: the Expo project, the app each simulator starts with, and the simulator itself.</summary>
public sealed record EasSimulatorsOptions
{
    /// <summary>
    /// The Expo project the sessions belong to: its id, not its slug. Absent, <c>extra.eas.projectId</c> of the app
    /// config in the project root, the id <c>eas init</c> writes: <c>app.json</c> or <c>app.config.json</c> as written,
    /// a dynamic <c>app.config.ts</c> or <c>.js</c> as the project's <c>expo config</c> evaluates it.
    /// </summary>
    public string? ProjectId { get; init; }

    /// <summary>
    /// EAS Build EAS installs and launches on every simulator before the session is ready: a simulator build
    /// (<c>ios.simulator: true</c>) or an APK. Pair it with the target's bundle id and leave its build path out.
    /// </summary>
    public string? BuildId { get; init; }

    /// <summary>URL of an app archive EAS downloads and installs in place of an EAS Build; excludes <see cref="BuildId"/>.</summary>
    public string? ApplicationArchiveUrl { get; init; }

    /// <summary>Simulator to start: an iOS device name or UDID (<c>iPhone 17 Pro</c>), or an Android AVD hardware profile (<c>pixel_9</c>). Absent, EAS picks.</summary>
    public string? Device { get; init; }

    /// <summary>
    /// Minutes without an agent-device command after which EAS stops the session. Defaults to 10, or one less than
    /// <see cref="MaxDurationMinutes"/> when that is shorter, since EAS wants it below the duration. A session still
    /// queued once another session of the run has been ready for that one's limit fails the lease: EAS is stopping that one.
    /// </summary>
    public int? MaxIdleTimeMinutes { get; init; }

    /// <summary>Minutes a session may run once ready before EAS stops it. Absent, the account's cap: 40, or 115 on a high-priority plan.</summary>
    public int? MaxDurationMinutes { get; init; }

    /// <summary>
    /// The agent-device version EAS starts the session's daemon at. Defaults to the one this engine pins, so the remote
    /// daemon answers as the engine's client expects; EAS otherwise starts the latest.
    /// </summary>
    public string? AgentDeviceVersion { get; init; }

    /// <summary>Tags on every session, beside the run's own; EAS stores them lowercased.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];
}
