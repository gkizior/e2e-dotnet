// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>Orientations <see cref="Device.SetOrientationAsync"/> accepts.</summary>
public enum DeviceOrientation
{
    Portrait,
    PortraitUpsideDown,
    LandscapeLeft,
    LandscapeRight,
}

/// <summary>The biometric sensors a simulator or emulator can simulate.</summary>
public enum BiometricSensor
{
    FaceId,
    TouchId,
    Fingerprint,
}

/// <summary>The state <see cref="Device.SetNetworkAsync"/> puts the device's Wi-Fi in.</summary>
public enum NetworkState
{
    Online,
    Offline,
}

/// <summary>The system appearance <see cref="Device.SetAppearanceAsync"/> sets.</summary>
public enum Appearance
{
    Light,
    Dark,
}

/// <summary>What <see cref="Device.AlertAsync"/> does with the visible system alert.</summary>
public enum AlertAction
{
    Accept,
    Dismiss,
}

/// <summary>The outcome of one simulated biometric attempt.</summary>
public enum BiometricResult
{
    Match,
    NonMatch,
}

/// <summary>The app the session is on.</summary>
/// <param name="Name">The app's name, or its package on Android.</param>
/// <param name="BundleId">The bundle id or package, when the device reported one.</param>
public sealed record ForegroundApp(string Name, string? BundleId = null);

/// <summary>A simulated location.</summary>
public readonly record struct Coordinates(double Latitude, double Longitude);

/// <summary>The names agent-device knows the fixture's arguments by.</summary>
internal static class DeviceFixtureNames
{
    public static string WireName(this DeviceOrientation orientation) => orientation switch
    {
        DeviceOrientation.Portrait => "portrait",
        DeviceOrientation.PortraitUpsideDown => "portrait-upside-down",
        DeviceOrientation.LandscapeLeft => "landscape-left",
        DeviceOrientation.LandscapeRight => "landscape-right",
        _ => throw new ArgumentOutOfRangeException(nameof(orientation), orientation, "Unknown orientation."),
    };

    public static string WireName(this BiometricSensor sensor) => sensor switch
    {
        BiometricSensor.FaceId => "faceid",
        BiometricSensor.TouchId => "touchid",
        BiometricSensor.Fingerprint => "fingerprint",
        _ => throw new ArgumentOutOfRangeException(nameof(sensor), sensor, "Unknown biometric sensor."),
    };
}

/// <summary>
/// The device fixture: deterministic device management this engine contributes. It is not an agent tool. A test calls
/// these directly to arrange or assert device state. Every method reads the cancellation token of its own call, so a
/// fixture follows a harness from the test body into its teardown.
/// </summary>
public sealed class Device
{
    // Why iOS refuses to take the device offline, before any device command: agent-device's iOS wifi and airplane
    // settings only override a simulator's status bar, and a physical iPhone takes no settings at all. A success would
    // let a test read the repainted indicator as the app going offline.
    private const string IosOffline =
        "on iOS agent-device can only repaint a simulator's status bar, since the simulator shares the Mac's network, and cannot switch an iPhone's radios, so the app would stay online";

    private readonly DeviceSession _session;

    internal Device(DeviceSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Switches an Android device's Wi-Fi. Cellular data stays on, so an emulator hands the app over to its cellular
    /// link; <see cref="SetAirplaneModeAsync"/> with <c>true</c> cuts both. On iOS <see cref="NetworkState.Offline"/> is
    /// <c>UNSUPPORTED_CAPABILITY</c>, since a simulator shares the Mac's network and simctl can only repaint its status
    /// bar; <see cref="NetworkState.Online"/> sends nothing there, so a cleanup hook shared with Android passes.
    /// </summary>
    public Task SetNetworkAsync(NetworkState state, CancellationToken cancellationToken = default)
    {
        if (_session.Options.Platform == MobilePlatform.Ios)
        {
            return state == NetworkState.Offline
                ? Task.FromException(DeviceSupport.Unsupported($"device.setNetwork('offline') turns off an Android device's Wi-Fi; {IosOffline}"))
                : Task.CompletedTask;
        }

        return Setting("device.setNetwork", "wifi", state == NetworkState.Offline ? "off" : "on", cancellationToken);
    }

    /// <summary>
    /// Switches an Android device's airplane mode, Wi-Fi and cellular together; Android 11 or newer. On iOS, as for
    /// <see cref="SetNetworkAsync"/>, <c>true</c> is <c>UNSUPPORTED_CAPABILITY</c> and <c>false</c> sends nothing.
    /// </summary>
    public Task SetAirplaneModeAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (_session.Options.Platform == MobilePlatform.Ios)
        {
            return enabled
                ? Task.FromException(DeviceSupport.Unsupported($"device.setAirplaneMode(true) cuts an Android device's radios; {IosOffline}"))
                : Task.CompletedTask;
        }

        return Setting("device.setAirplaneMode", "airplane", enabled ? "on" : "off", cancellationToken);
    }

    /// <summary>
    /// Grants, denies, or resets one permission for the pinned app, which need not be running. A change terminates a
    /// running app on iOS, a revoke one on Android, so it goes before the <c>app.open()</c> a test starts with.
    /// </summary>
    public Task SetPermissionAsync(DevicePermission permission, PermissionState state, CancellationToken cancellationToken = default) =>
        _session.SetPermissionAsync(permission, state, cancellationToken);

    /// <summary>Sets the simulated location, switching location services on first on Android.</summary>
    public async Task SetLocationAsync(Coordinates coordinates, CancellationToken cancellationToken = default)
    {
        // Android reads a fix only while location services are on, and ClearLocation switched them off for good on the
        // emulator (the setting outlives the app). iOS has no such switch.
        if (_session.Options.Platform == MobilePlatform.Android)
        {
            await Setting("device.setLocation", "location", "on", cancellationToken).ConfigureAwait(false);
        }

        await _session.CommandAsync(
            "device.setLocation",
            DeviceMethods.SettingsUpdate,
            DeviceArgs.Of(
                ("setting", JsonValue.Create("location")),
                ("state", JsonValue.Create("set")),
                ("latitude", JsonValue.Create(coordinates.Latitude)),
                ("longitude", JsonValue.Create(coordinates.Longitude))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Turns simulated location off. On Android that is location services off, until the next <see cref="SetLocationAsync"/>.</summary>
    public Task ClearLocationAsync(CancellationToken cancellationToken = default) =>
        Setting("device.clearLocation", "location", "off", cancellationToken);

    /// <summary>Sets the system appearance.</summary>
    public Task SetAppearanceAsync(Appearance appearance, CancellationToken cancellationToken = default) =>
        Setting("device.setAppearance", "appearance", appearance == Appearance.Dark ? "dark" : "light", cancellationToken);

    /// <summary>Rotates the device.</summary>
    public Task SetOrientationAsync(DeviceOrientation orientation, CancellationToken cancellationToken = default) =>
        _session.ScreenCommandAsync(
            "device.setOrientation",
            (client, token) => client.CallAsync(DeviceMethods.CommandOrientation, DeviceArgs.Of(("orientation", JsonValue.Create(orientation.WireName()))), token),
            cancellationToken);

    /// <summary>
    /// Puts a foldable simulator or emulator in a hinge pose. On the iPhone Duo it resolves once agent-device reads the
    /// angle back from CoreDevice at that pose, typically 10 to 16 seconds later. <see cref="FoldPose.Closed"/> lights
    /// the outer display, <see cref="FoldPose.HalfOpen"/> (a 130 degree book) and <see cref="FoldPose.Open"/> light the
    /// inner one. On a foldable Android emulator it sets the emulator console posture and resolves once the guest
    /// commits the matching device state, within a few seconds. Either way the app window changes size, so the next
    /// observation reads a new screen. Any device without a hinge is <c>UNSUPPORTED_CAPABILITY</c>.
    /// </summary>
    public Task FoldAsync(FoldPose pose, CancellationToken cancellationToken = default) => _session.FoldAsync(pose, cancellationToken);

    /// <summary>Simulates one biometric attempt on the open app.</summary>
    public Task SetBiometricsAsync(BiometricSensor sensor, BiometricResult result, CancellationToken cancellationToken = default) =>
        Setting("device.setBiometrics", sensor.WireName(), result == BiometricResult.Match ? "match" : "nonmatch", cancellationToken);

    /// <summary>Enrolls or unenrolls the simulator's Face ID or Touch ID.</summary>
    public Task EnrollBiometricsAsync(BiometricSensor sensor, bool enrolled, CancellationToken cancellationToken = default) =>
        sensor == BiometricSensor.Fingerprint
            ? Task.FromException(new TestException("INVALID_ARGUMENT", "enrollBiometrics takes the simulator's Face ID or Touch ID"))
            : Setting("device.enrollBiometrics", sensor.WireName(), enrolled ? "enroll" : "unenroll", cancellationToken);

    /// <summary>
    /// Installs a build (an iOS <c>.app</c> bundle or an Android <c>.apk</c>, resolved against the project root) on the
    /// device. Without a path it installs the target's <c>AppPath</c>. The engine installs nothing on its own, so a suite
    /// that runs against a build calls this once per device, in a fixture or a test. <see cref="InstallAppOptions.Reinstall"/>
    /// removes the app first so it starts with no data. A plain install replaces the binary and keeps its data. Resolves
    /// to the identity to <see cref="OpenAppAsync"/> it by, which becomes the app <c>app.open()</c> launches when the
    /// target's build is installed and no bundle id is pinned.
    /// </summary>
    public Task<InstalledApp> InstallAppAsync(string? appPath = null, InstallAppOptions? options = null, CancellationToken cancellationToken = default) =>
        _session.InstallAppAsync(appPath, options, cancellationToken);

    /// <summary>
    /// Brings an app to the foreground. <see cref="OpenAppOptions.Relaunch"/> restarts it fresh. Its launch arguments and
    /// permissions apply to this launch alone. The target's own apply to a relaunch of the pinned app, and to no
    /// foreground-only open.
    /// </summary>
    public Task OpenAppAsync(string app, OpenAppOptions? options = null, CancellationToken cancellationToken = default) =>
        _session.OpenAppAsync(app, options, cancellationToken);

    /// <summary>
    /// Opens a deep link (<c>myapp://orders/42</c>) or a web link (<c>https://...</c>) into <paramref name="app"/> (default:
    /// the pinned app), which the session observes afterwards. iOS launches the app for a web link and then opens the URL.
    /// A web link the app does not claim goes to Safari, and the next observation brings the app back. Android starts the
    /// link on that package. Without an app, Android lets the OS route the link. iOS needs one (<c>INVALID_ARGUMENT</c>
    /// without). <c>file:</c>, <c>data:</c>, <c>javascript:</c>, <c>view-source:</c>, <c>blob:</c>, and <c>filesystem:</c>
    /// links are <c>POLICY_DENIED</c>.
    /// </summary>
    public Task OpenLinkAsync(string url, string? app = null, CancellationToken cancellationToken = default) =>
        _session.OpenLinkAsync(DeviceLinks.LinkTarget(url), app, cancellationToken);

    /// <summary>
    /// Terminates the session's app and ends the session, so the next <see cref="OpenAppAsync"/> launches it fresh. Until
    /// then the screen is unobservable.
    /// </summary>
    public Task CloseAppAsync(CancellationToken cancellationToken = default) => _session.CloseAppAsync(cancellationToken);

    /// <summary>
    /// Resets the simulator's keychain, every app's, since simctl has no per-app reset: what <c>app.clearState()</c>
    /// leaves behind (a session token, a Firebase sign-in). iOS simulator only. Android is <c>UNSUPPORTED_CAPABILITY</c>.
    /// </summary>
    public Task ClearKeychainAsync(CancellationToken cancellationToken = default) => _session.ClearKeychainAsync(cancellationToken);

    /// <summary>
    /// The app the session is on. iOS answers from the session, not the device, so it names the app the session opened
    /// even after <see cref="HomeAsync"/>. Android reads the device's foreground activity.
    /// </summary>
    public async Task<ForegroundApp> ForegroundAppAsync(CancellationToken cancellationToken = default)
    {
        var state = await _session.CommandAsync("device.foregroundApp", DeviceMethods.CommandAppState, new JsonObject(), cancellationToken).ConfigureAwait(false);
        if (Text(state?["package"]) is { } package)
        {
            return new ForegroundApp(package, package);
        }

        return new ForegroundApp(Text(state?["appName"]) ?? "", Text(state?["appBundleId"]));
    }

    /// <summary>Sends the device to its home screen.</summary>
    public Task HomeAsync(CancellationToken cancellationToken = default) =>
        _session.ScreenCommandAsync("device.home", (client, token) => client.CallAsync(DeviceMethods.CommandHome, new JsonObject(), token), cancellationToken);

    /// <summary>Navigates back once (in-app back).</summary>
    public Task BackAsync(CancellationToken cancellationToken = default) =>
        _session.ScreenCommandAsync("device.back", (client, token) => client.CallAsync(DeviceMethods.CommandBack, _session.SettleArgs(), token), cancellationToken);

    /// <summary>Accepts or dismisses the visible system alert.</summary>
    public Task AlertAsync(AlertAction action, CancellationToken cancellationToken = default) =>
        _session.ScreenCommandAsync(
            "device.alert",
            (client, token) => client.CallAsync(DeviceMethods.CommandAlert, DeviceArgs.Of(("action", JsonValue.Create(action == AlertAction.Accept ? "accept" : "dismiss"))), token),
            cancellationToken);

    /// <summary>Dismisses the soft keyboard.</summary>
    public Task DismissKeyboardAsync(CancellationToken cancellationToken = default) => _session.DismissKeyboardAsync(cancellationToken);

    /// <summary>Reads the clipboard.</summary>
    public async Task<string> ClipboardAsync(CancellationToken cancellationToken = default)
    {
        var result = await _session.CommandAsync(
            "device.clipboard",
            DeviceMethods.CommandClipboard,
            DeviceArgs.Of(("action", JsonValue.Create("read"))),
            cancellationToken).ConfigureAwait(false);
        return Text(result?["action"]) == "read" ? Text(result?["text"]) ?? "" : "";
    }

    /// <summary>Writes the clipboard.</summary>
    public Task SetClipboardAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _session.CommandAsync(
            "device.setClipboard",
            DeviceMethods.CommandClipboard,
            DeviceArgs.Of(("action", JsonValue.Create("write")), ("text", JsonValue.Create(text))),
            cancellationToken);
    }

    private Task Setting(string label, string setting, string state, CancellationToken cancellationToken) =>
        _session.CommandAsync(
            label,
            DeviceMethods.SettingsUpdate,
            DeviceArgs.Of(("setting", JsonValue.Create(setting)), ("state", JsonValue.Create(state))),
            cancellationToken);

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
