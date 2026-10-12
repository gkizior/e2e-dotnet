// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>Hinge poses <see cref="DeviceSession.FoldAsync"/> accepts: <see cref="Closed"/> lights the outer display, <see cref="HalfOpen"/> and <see cref="Open"/> the inner one.</summary>
public enum FoldPose
{
    Closed,
    HalfOpen,
    Open,
}

/// <summary>How <see cref="DeviceSession.InstallAppAsync"/> puts a build on the device.</summary>
public sealed record InstallAppOptions
{
    /// <summary>
    /// Bundle id or package of the build. <see cref="Reinstall"/> needs one: agent-device removes that app before
    /// installing. Defaults to the pinned app.
    /// </summary>
    public string? App { get; init; }

    /// <summary>Remove the installed app first so the build starts with no data.</summary>
    public bool Reinstall { get; init; }
}

/// <summary>What an install put on the device, as agent-device identified it.</summary>
/// <param name="App">The bundle id or package to open the app by.</param>
/// <param name="BundleId">The identity agent-device reported, when it reported one.</param>
public sealed record InstalledApp(string App, string? BundleId = null);

/// <summary>How <see cref="DeviceSession.OpenAppAsync"/> launches an app.</summary>
public sealed record OpenAppOptions
{
    /// <summary>Terminate the app first, so it starts fresh. Without it a running app is brought forward as it is.</summary>
    public bool Relaunch { get; init; }

    /// <summary>
    /// Arguments this launch hands the app: its process arguments on iOS, <c>am start</c> arguments on Android.
    /// Replaces the target's <c>LaunchArguments</c> for a relaunch of the pinned app.
    /// </summary>
    public IReadOnlyList<string>? LaunchArguments { get; init; }

    /// <summary>
    /// Permissions the app holds before this launch, each granted, denied, or reset. Replaces the target's
    /// <c>Permissions</c> for a relaunch of the pinned app.
    /// </summary>
    public IReadOnlyDictionary<DevicePermission, PermissionState>? Permissions { get; init; }
}

public sealed partial class DeviceSession
{
    /// <summary>
    /// Opens an app in the session, remembering its identity for the path anchor. A link is refused before the device
    /// sees it: agent-device would open it as a URL, and <see cref="OpenLinkAsync"/> is the path that carries the
    /// navigation rule. The engine's own launch options belong to a fresh launch of the pinned app: a foreground-only
    /// open of a running app takes no arguments, and a permission change there would terminate the app it means to keep.
    /// </summary>
    public async Task OpenAppAsync(string app, OpenAppOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(app);
        DeviceLinks.AssertAppId(app);
        options ??= new OpenAppOptions();
        var relaunch = options.Relaunch;
        var configured = relaunch && app == PinnedApp;
        var launchArguments = options.LaunchArguments ?? (configured ? _app.LaunchArguments : null);
        var permissions = options.Permissions ?? (configured ? _app.Permissions : null);
        if (permissions is not null)
        {
            // Before the launch, never after: iOS terminates a running app whose permission changed, and Android one
            // whose permission was revoked, so a change after the launch would leave the test on no screen.
            foreach (var (permission, state) in permissions)
            {
                await SetPermissionAsync(permission, state, app, cancellationToken).ConfigureAwait(false);
            }
        }

        await LaunchAsync(app, relaunch, launchArguments, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One <c>open</c> of an app, remembered as the session's app and as the screen's replacement.</summary>
    private async Task LaunchAsync(string app, bool relaunch, IReadOnlyList<string>? launchArguments, CancellationToken cancellationToken)
    {
        var result = await CommandAsync(
            $"open {app}",
            DeviceMethods.AppsOpen,
            Selection.ToArgs(
                ("app", JsonValue.Create(app)),
                ("relaunch", relaunch ? JsonValue.Create(true) : null),
                ("launchArgs", launchArguments is { Count: > 0 } ? new JsonArray([.. launchArguments.Select(argument => (JsonNode?)JsonValue.Create(argument))]) : null)),
            cancellationToken).ConfigureAwait(false);
        Launched(Text(result?["appBundleId"]) ?? Text(result?["appName"]) ?? app);
    }

    /// <summary>
    /// What a launch leaves behind. The identity is the app agent-device named. A link the OS routed keeps the last
    /// known one until the next snapshot reports the foreground app.
    /// </summary>
    private void Launched(string? identity)
    {
        if (identity is not null)
        {
            _appIdentity = identity;
        }

        ScreenReplaced();
    }

    /// <summary>One permission change for <c>device.setPermission</c>: on the pinned app, else on the app agent-device's session is on.</summary>
    public Task SetPermissionAsync(DevicePermission permission, PermissionState state, CancellationToken cancellationToken = default) =>
        SetPermissionAsync(permission, state, PinnedApp, cancellationToken);

    /// <summary>One permission change on <paramref name="app"/>, which needs no running app and no open in the session.</summary>
    private async Task SetPermissionAsync(DevicePermission permission, PermissionState state, string? app, CancellationToken cancellationToken)
    {
        await CommandAsync(
            $"permission {permission.WireName()} {state.WireName()}",
            DeviceMethods.SettingsUpdate,
            Selection.ToArgs(
                ("setting", JsonValue.Create("permission")),
                ("permission", JsonValue.Create(permission.WireName())),
                ("state", JsonValue.Create(state.WireName())),
                ("app", app is null ? null : JsonValue.Create(app))),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a link on the device, into <paramref name="app"/> (the pinned app when none is named). Bound to an app,
    /// agent-device hands the link to it and the session observes that app afterwards: iOS launches the app first for
    /// a web link and then opens the URL, Android starts a VIEW intent on the package. Without an app the OS routes the
    /// link to the scheme's app, a verified app link owner, or the browser. iOS needs the app: an unbound
    /// <c>open &lt;url&gt;</c> there leaves no app session, so the next snapshot would fail, and agent-device refuses a
    /// deep link without one on a physical device anyway. An unbound open reports the URL as its app name, which is no
    /// identity, and the package that took the link as its bundle id when the OS told it. The next snapshot names the
    /// foreground app either way.
    /// </summary>
    public async Task OpenLinkAsync(Uri url, string? app = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(url);
        var target = app ?? PinnedApp;
        if (target is null && Options.Platform == MobilePlatform.Ios)
        {
            throw new TestException(
                "INVALID_ARGUMENT",
                "openLink needs an app on iOS: pass `app`, or pin one with the target's app.bundleId or app.appPath; the session observes the app a link is opened into");
        }

        var result = await CommandAsync(
            "device.openLink",
            DeviceMethods.AppsOpen,
            target is null
                ? Selection.ToArgs(("app", JsonValue.Create(url.AbsoluteUri)))
                : Selection.ToArgs(("app", JsonValue.Create(target)), ("url", JsonValue.Create(url.AbsoluteUri))),
            cancellationToken).ConfigureAwait(false);
        Launched(Text(result?["appBundleId"]) ?? (target is null ? null : Text(result?["appName"]) ?? target));
    }

    /// <summary>
    /// Terminates the session's app, then ends the session. agent-device's bare <c>close</c> ends the session and
    /// leaves the app running, so the next <see cref="OpenAppAsync"/> would resume it mid-flow. A close that names the
    /// app terminates it first. The app is the one the session last observed, else the pinned one. With neither known
    /// there is nothing to terminate and only the session ends.
    /// </summary>
    public async Task CloseAppAsync(CancellationToken cancellationToken = default)
    {
        var app = _appIdentity ?? PinnedApp;
        await CommandAsync("device.closeApp", DeviceMethods.AppsClose, DeviceArgs.Of(("app", app is null ? null : JsonValue.Create(app))), cancellationToken).ConfigureAwait(false);
        ScreenReplaced();
    }

    /// <summary>
    /// Installs a build on the session's device. <see cref="InstallAppOptions.Reinstall"/> removes the app named by
    /// <see cref="InstallAppOptions.App"/> (else the pinned app) first, so the build starts with no data. A plain
    /// install replaces the binary and keeps its data. Without a path the target's <c>AppPath</c> is installed.
    /// </summary>
    public async Task<InstalledApp> InstallAppAsync(string? appPath = null, InstallAppOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new InstallAppOptions();
        var build = appPath ?? _app.AppPath
            ?? throw DeviceSupport.InvalidState("installApp needs a build: pass a path, or name one with the target's app.appPath");
        var resolved = Path.GetFullPath(build, _projectRoot);
        var engineBuild = appPath is null || (_app.AppPath is not null && resolved == Path.GetFullPath(_app.AppPath, _projectRoot));
        var app = options.App ?? (engineBuild ? _app.BundleId : null) ?? (options.Reinstall ? PinnedApp : null);
        if (options.Reinstall && app is null)
        {
            throw DeviceSupport.InvalidState("reinstall needs an app: pass `app`, or pin one with the target's app.bundleId or app.appPath");
        }

        var result = await CommandAsync(
            $"install {resolved}",
            options.Reinstall && app is not null ? DeviceMethods.AppsReinstall : DeviceMethods.AppsInstall,
            Selection.ToArgs(("app", app is null ? null : JsonValue.Create(app)), ("appPath", JsonValue.Create(resolved))),
            cancellationToken).ConfigureAwait(false);
        var identity = Text(result?["bundleId"]) ?? Text(result?["package"]);
        var opensBy = identity ?? app
            ?? throw new EngineException(
                EngineErrorCodes.EngineFailure,
                $"installed {resolved}, but agent-device reported no bundle id or package for it, so nothing can open it: name the app with the target's `app.bundleId` or installApp's `app` option",
                retryable: false);
        var installed = new InstalledApp(opensBy, identity);

        // The engine's own build, installed: without an app, this is what app.open() launches from here on.
        if (engineBuild)
        {
            InstalledApp = installed.App;
        }

        return installed;
    }

    /// <summary>
    /// Sends a foldable simulator's or emulator's hinge to a pose. agent-device confirms it before it answers: the
    /// hinge angle through CoreDevice on iOS, the device state the guest commits on Android. The pose moves the app to
    /// another panel, so it counts as an action like a rotation. A device without a hinge comes back from agent-device
    /// as <c>UNSUPPORTED_OPERATION</c>.
    /// </summary>
    public Task FoldAsync(FoldPose pose, CancellationToken cancellationToken = default) =>
        ScreenCommandAsync(
            "device.fold",
            (client, token) => client.CallAsync(DeviceMethods.CommandFold, DeviceArgs.Of(("pose", JsonValue.Create(pose.WireName()))), token),
            cancellationToken);

    /// <summary>
    /// Resets the simulator's keychain through agent-device. simctl resets the whole simulator's, not one app's, and only
    /// a simulator has one to reset: Android is refused before any device command.
    /// </summary>
    public Task ClearKeychainAsync(CancellationToken cancellationToken = default)
    {
        if (Options.Platform != MobilePlatform.Ios)
        {
            return Task.FromException(DeviceSupport.Unsupported("device.clearKeychain resets an iOS simulator keychain; Android has none to reset"));
        }

        return CommandAsync(
            "device.clearKeychain",
            DeviceMethods.SettingsUpdate,
            DeviceArgs.Of(("setting", JsonValue.Create("reset-keychain")), ("state", JsonValue.Create("clear"))),
            cancellationToken);
    }
}

/// <summary>The names agent-device knows a hinge pose by.</summary>
public static class FoldPoses
{
    public static string WireName(this FoldPose pose) => pose switch
    {
        FoldPose.Closed => "closed",
        FoldPose.HalfOpen => "half-open",
        FoldPose.Open => "open",
        _ => throw new ArgumentOutOfRangeException(nameof(pose), pose, "Unknown hinge pose."),
    };
}
