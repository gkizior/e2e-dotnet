// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.Mobile;

/// <summary>
/// The agent-device client the engine drives one session through: the seam unit tests script. A call is named
/// as upstream's client names it, <c>namespace.method</c> (<c>devices.boot</c>, <c>apps.open</c>,
/// <c>capture.snapshot</c>), and takes the options object that method takes there. It answers with the
/// <c>data</c> the daemon returned, or throws <see cref="AgentDeviceError"/> when the daemon refused.
/// </summary>
public interface IDeviceClient
{
    Task<JsonNode?> CallAsync(string method, JsonObject? options, CancellationToken cancellationToken);
}

/// <summary>Mints the client for one session, with the daemon and configuration a lease supplied, else against the local daemon.</summary>
public delegate IDeviceClient DeviceClientFactory(string session, DeviceConnection? connection);

/// <summary>The client methods the engine calls, named as upstream's client names them.</summary>
public static class DeviceMethods
{
    public const string DevicesList = "devices.list";
    public const string DevicesBoot = "devices.boot";
    public const string CommandPrepare = "command.prepare";
    public const string CommandBack = "command.back";
    public const string AppsOpen = "apps.open";
    public const string AppsClose = "apps.close";
    public const string AppsInstall = "apps.install";
    public const string AppsReinstall = "apps.reinstall";
    public const string SessionsList = "sessions.list";
    public const string SessionsClose = "sessions.close";
    public const string SettingsUpdate = "settings.update";
    public const string CaptureSnapshot = "capture.snapshot";
    public const string CaptureScreenshot = "capture.screenshot";
    public const string InteractionsPress = "interactions.press";
    public const string InteractionsLongPress = "interactions.longPress";
    public const string InteractionsHover = "interactions.hover";
    public const string InteractionsFill = "interactions.fill";
    public const string InteractionsType = "interactions.type";
    public const string InteractionsSwipe = "interactions.swipe";
    public const string InteractionsScroll = "interactions.scroll";
    public const string InteractionsPan = "interactions.pan";
    public const string InteractionsDrag = "interactions.drag";
    public const string CommandKeyboard = "command.keyboard";
    public const string CommandAlert = "command.alert";
    public const string CommandHome = "command.home";
    public const string CommandOrientation = "command.orientation";
    public const string CommandFold = "command.fold";
    public const string CommandAppState = "command.appState";
    public const string CommandClipboard = "command.clipboard";
}

/// <summary>Builds the options objects the client takes. A null value is left out, as upstream leaves out an undefined one.</summary>
internal static class DeviceArgs
{
    public static JsonObject Of(params (string Key, JsonNode? Value)[] properties)
    {
        var result = new JsonObject();
        foreach (var (key, value) in properties)
        {
            if (value is not null)
            {
                result[key] = value;
            }
        }

        return result;
    }
}
