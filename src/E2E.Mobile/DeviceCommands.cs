// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json.Nodes;

namespace E2E.Mobile;

/// <summary>One daemon request: the command, its positional arguments, its flags, and the structured input a few commands take beside them.</summary>
public sealed record DeviceCommand(string Name, IReadOnlyList<string> Positionals, JsonObject Flags, JsonObject? Input = null);

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
                var input = flags["setting"]?.GetValue<string>() == "permission" ? Structured(flags, "app") : null;
                return new DeviceCommand("settings", Settings(flags), flags, input);
            case DeviceMethods.CaptureSnapshot:
                if (flags.Remove("interactiveOnly", out var interactive) && interactive is not null)
                {
                    flags["snapshotInteractiveOnly"] = interactive;
                }

                return new DeviceCommand("snapshot", [], flags);
            case DeviceMethods.CaptureScreenshot:
                return new DeviceCommand("screenshot", Take(flags, "path"), flags);
            case DeviceMethods.InteractionsPress:
                return new DeviceCommand("press", Target(flags), flags);
            case DeviceMethods.InteractionsLongPress:
                return new DeviceCommand("longpress", [.. Target(flags), .. Number(flags, "durationMs", remove: false)], flags);
            case DeviceMethods.InteractionsHover:
                return new DeviceCommand("hover", Target(flags), flags);
            case DeviceMethods.InteractionsFill:
                return new DeviceCommand("fill", [.. Target(flags), .. Take(flags, "text")], flags);
            case DeviceMethods.InteractionsType:
                return new DeviceCommand("type", Take(flags, "text"), flags);
            case DeviceMethods.InteractionsSwipe:
                return new DeviceCommand("swipe", [], flags, Structured(flags, "from", "to"));
            case DeviceMethods.InteractionsScroll:
                return new DeviceCommand("scroll", [.. Take(flags, "direction"), .. Number(flags, "amount")], flags);
            case DeviceMethods.InteractionsPan:
                return Gesture("pan", flags, ("origin", Point(flags, "x", "y")), ("delta", Point(flags, "dx", "dy")));
            case DeviceMethods.InteractionsDrag:
                return Gesture("drag", flags, ("source", Remove(flags, "source")), ("destination", Remove(flags, "destination")));
            case DeviceMethods.CommandKeyboard:
                return new DeviceCommand("keyboard", Take(flags, "action"), flags);
            case DeviceMethods.CommandAlert:
                return new DeviceCommand("alert", Take(flags, "action"), flags);
            case DeviceMethods.CommandHome:
                return new DeviceCommand("home", [], flags);
            case DeviceMethods.CommandOrientation:
                return new DeviceCommand("orientation", Take(flags, "orientation"), flags);
            case DeviceMethods.CommandFold:
                return new DeviceCommand("fold", Take(flags, "pose"), flags);
            case DeviceMethods.CommandAppState:
                return new DeviceCommand("appstate", [], flags);
            case DeviceMethods.CommandClipboard:
                return new DeviceCommand("clipboard", Take(flags, "action", "text"), flags);
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

    /// <summary>The target of an interaction: an <c>@ref</c>, a selector, or a point, as the CLI takes it.</summary>
    private static string[] Target(JsonObject flags)
    {
        if (Take(flags, "ref") is { Length: > 0 } reference)
        {
            return reference;
        }

        if (Take(flags, "selector") is { Length: > 0 } selector)
        {
            return selector;
        }

        return [.. Number(flags, "x"), .. Number(flags, "y")];
    }

    /// <summary>Moves a number out of the flags as a positional argument, as the client writes it: as text. A missing one is skipped.</summary>
    private static string[] Number(JsonObject flags, string name, bool remove = true)
    {
        var found = remove ? flags.Remove(name, out var taken) ? taken : null : flags[name];
        return found is JsonValue value && value.TryGetValue<double>(out var number) ? [number.ToString("R", CultureInfo.InvariantCulture)] : [];
    }

    /// <summary>Moves the named options out of the flags into the structured input a command takes beside its positionals.</summary>
    private static JsonObject Structured(JsonObject flags, params string[] names)
    {
        var input = new JsonObject();
        foreach (var name in names)
        {
            if (Remove(flags, name) is { } value)
            {
                input[name] = value;
            }
        }

        return input;
    }

    private static JsonNode? Remove(JsonObject flags, string name) => flags.Remove(name, out var value) ? value : null;

    private static JsonObject Point(JsonObject flags, string x, string y) =>
        new() { ["x"] = Remove(flags, x), ["y"] = Remove(flags, y) };

    /// <summary>A <c>gesture</c> command: its kind is a flag and a field of the input, as the client writes both.</summary>
    private static DeviceCommand Gesture(string kind, JsonObject flags, params (string Name, JsonNode? Value)[] fields)
    {
        var input = new JsonObject { ["kind"] = kind };
        foreach (var (name, value) in fields)
        {
            input[name] = value;
        }

        if (flags["durationMs"] is { } duration)
        {
            input["durationMs"] = duration.DeepClone();
        }

        flags["kind"] = kind;
        return new DeviceCommand("gesture", [], flags, input);
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
            case "location":
                return ["location", .. Take(flags, "state"), .. Number(flags, "latitude"), .. Number(flags, "longitude")];
            case "wifi" or "airplane" or "appearance" or "faceid" or "touchid" or "fingerprint":
                return [setting[0], .. Take(flags, "state")];
            default:
                throw new NotSupportedException($"The device client does not support the {setting[0]} setting yet.");
        }
    }
}
