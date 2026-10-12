// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.Mobile;

/// <summary>One daemon request: the command, its positional arguments, and its flags.</summary>
public sealed record DeviceCommand(string Name, IReadOnlyList<string> Positionals, JsonObject Flags);

/// <summary>
/// The translation of a client call (<c>namespace.method</c> with its options object) into the command, positionals,
/// and flags the daemon takes on <c>agent_device.command</c>. Upstream's client does it in minified writers that read
/// each command's input. The commands here are the ones the port calls so far; a method not listed is refused until
/// the code that calls it lands. The flag names are the daemon's own: the ones its CLI accepts.
/// </summary>
public static class DeviceCommands
{
    public static DeviceCommand Translate(string method, JsonObject? options)
    {
        var flags = options is null ? new JsonObject() : (JsonObject)options.DeepClone();
        switch (method)
        {
            case DeviceMethods.DevicesList:
                return new DeviceCommand("devices", [], flags);
            case DeviceMethods.DevicesBoot:
                return new DeviceCommand("boot", [], flags);
            case DeviceMethods.CommandPrepare:
                return new DeviceCommand("prepare", Take(flags, "action"), flags);
            case DeviceMethods.CommandBack:
                return new DeviceCommand("back", [], flags);
            case DeviceMethods.AppsOpen:
                return new DeviceCommand("open", Take(flags, "app", "url"), flags);
            case DeviceMethods.AppsClose:
                return new DeviceCommand("close", Take(flags, "app"), flags);
            case DeviceMethods.AppsInstall:
                return new DeviceCommand("install", Take(flags, "app", "appPath"), flags);
            case DeviceMethods.AppsReinstall:
                return new DeviceCommand("reinstall", Take(flags, "app", "appPath"), flags);
            case DeviceMethods.SessionsList:
                return new DeviceCommand("session_list", [], flags);
            case DeviceMethods.SessionsClose:
                return new DeviceCommand("close", [], flags);
            case DeviceMethods.SettingsUpdate:
                return new DeviceCommand("settings", Settings(flags), flags);
            case DeviceMethods.CaptureSnapshot:
                if (flags.Remove("interactiveOnly", out var interactive) && interactive is not null)
                {
                    flags["snapshotInteractiveOnly"] = interactive;
                }

                return new DeviceCommand("snapshot", [], flags);
            case DeviceMethods.CaptureScreenshot:
                return new DeviceCommand("screenshot", Take(flags, "path"), flags);
            default:
                throw new NotSupportedException($"The device client does not support {method} yet.");
        }
    }

    /// <summary>Moves the named options out of the flags, in order, as positional arguments. A missing one is skipped.</summary>
    private static string[] Take(JsonObject flags, params string[] names)
    {
        var positionals = new List<string>();
        foreach (var name in names)
        {
            if (!flags.Remove(name, out var node) || node is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                continue;
            }

            positionals.Add(text);
        }

        return [.. positionals];
    }

    /// <summary>
    /// <c>settings permission &lt;state&gt; &lt;permission&gt;</c>, <c>settings clear-app-state [app]</c>, and
    /// <c>settings reset-keychain clear</c>, as the daemon's usage lists them.
    /// </summary>
    private static string[] Settings(JsonObject flags)
    {
        var setting = Take(flags, "setting");
        if (setting.Length == 0)
        {
            throw new ArgumentException("settings.update needs a setting.");
        }

        switch (setting[0])
        {
            case "permission":
                var arguments = Take(flags, "state", "permission");
                return arguments.Length == 2 ? ["permission", .. arguments] : throw new ArgumentException("settings permission needs a state and a permission.");
            case "clear-app-state":
                flags.Remove("state");
                return ["clear-app-state", .. Take(flags, "app")];
            case "reset-keychain":
                return ["reset-keychain", .. Take(flags, "state")];
            default:
                throw new NotSupportedException($"The device client does not support the {setting[0]} setting yet.");
        }
    }
}
