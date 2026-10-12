// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Mobile.Eas;

/// <summary>An access token goes out as <c>Authorization: Bearer</c>, an eas-cli session secret as <c>expo-session</c>.</summary>
internal sealed record ExpoCredentials(string? AccessToken, string? SessionSecret)
{
    public const string TokenVariable = "EXPO_TOKEN";

    /// <summary>The header a request authenticates with.</summary>
    public (string Name, string Value) Header => AccessToken is not null ? ("Authorization", $"Bearer {AccessToken}") : ("expo-session", SessionSecret!);

    /// <summary>The key of the client these credentials make.</summary>
    public string Key => AccessToken is not null ? $"token:{AccessToken}" : $"session:{SessionSecret}";

    /// <summary>
    /// The credentials a run authenticates with, in eas-cli's order: <c>EXPO_TOKEN</c> when it is set, else the session
    /// <c>eas login</c> stored, so a machine signed in to eas-cli needs no token. Only the production login is read: the
    /// sessions API is always <c>api.expo.dev</c>, and eas-cli's staging and local logins belong to other hosts.
    /// Null when there is neither.
    /// </summary>
    public static async Task<ExpoCredentials?> ReadAsync(IReadOnlyDictionary<string, string?> env, CancellationToken cancellationToken)
    {
        if (EasEnvironment.Value(env, TokenVariable) is { } token)
        {
            return new ExpoCredentials(token, null);
        }

        var path = StateJsonPath(env);
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception cause) when (cause is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception cause) when (cause is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"could not read the eas-cli login at {path}: {cause.Message}", cause);
        }

        JsonNode? state;
        try
        {
            state = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            // No cause: a JSON parse error quotes the text around the fault, which can be the secret.
            throw new InvalidOperationException($"the eas-cli login at {path} is not valid JSON; run `eas login` again, or set {TokenVariable}");
        }

        var secret = (state as JsonObject)?["auth"] is JsonObject auth && auth["sessionSecret"] is JsonValue value && value.TryGetValue<string>(out var text2)
            ? text2.Trim()
            : "";
        return secret.Length == 0 ? null : new ExpoCredentials(null, secret);
    }

    /// <summary>eas-cli's production <c>state.json</c> under the run's home: <c>USERPROFILE</c> on Windows and <c>HOME</c> elsewhere.</summary>
    private static string StateJsonPath(IReadOnlyDictionary<string, string?> env)
    {
        var home = EasEnvironment.Value(env, OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".expo", "state.json");
    }
}

internal static class EasEnvironment
{
    /// <summary>A non-empty variable from the run's environment, trimmed, or null.</summary>
    public static string? Value(IReadOnlyDictionary<string, string?> env, string name) =>
        env.TryGetValue(name, out var value) && value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
