// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Mobile.Eas;

/// <summary>
/// The Expo project id a run's sessions belong to when <see cref="EasSimulatorsOptions.ProjectId"/> names none:
/// <c>extra.eas.projectId</c> of the app config in the e2e project root, the id <c>eas init</c> writes.
/// </summary>
internal static class ExpoProject
{
    /// <summary>The dynamic app config names Expo resolves, in its order; one of them wins over <c>app.json</c>.</summary>
    private static readonly string[] DynamicConfigs = ["app.config.ts", "app.config.mts", "app.config.cts", "app.config.mjs", "app.config.cjs", "app.config.js"];

    /// <summary>The static app config names Expo resolves, in its order.</summary>
    private static readonly string[] StaticConfigs = ["app.config.json", "app.json"];

    /// <summary>How long <c>expo config</c> may evaluate a dynamic config; it takes about a second.</summary>
    private static readonly TimeSpan ExpoConfigTimeout = TimeSpan.FromSeconds(60);

    private const string PassIt = "pass `projectId` to EasSimulators, or run `eas init` to link the app to an EAS project";

    /// <summary>
    /// <c>extra.eas.projectId</c> from the app config in <paramref name="projectRoot"/>. A dynamic config
    /// (<c>app.config.ts</c> and its siblings) is evaluated by the project's own <c>expo config</c>, with the run's
    /// environment, since it can compute the id; a static one (<c>app.config.json</c>, <c>app.json</c>) is read as JSON.
    /// </summary>
    public static async Task<string> ProjectIdAsync(string projectRoot, IReadOnlyDictionary<string, string?> env, CancellationToken cancellationToken)
    {
        if (Array.Find(DynamicConfigs, name => File.Exists(Path.Combine(projectRoot, name))) is { } dynamic)
        {
            return ProjectIdOf(await EvaluatedConfigAsync(projectRoot, env, cancellationToken).ConfigureAwait(false), $"{dynamic} (through `expo config`)");
        }

        if (Array.Find(StaticConfigs, name => File.Exists(Path.Combine(projectRoot, name))) is not { } fixedName)
        {
            throw new InvalidOperationException($"no Expo app config (app.json or app.config.*) in {projectRoot}; {PassIt}");
        }

        var path = Path.Combine(projectRoot, fixedName);
        JsonNode? config;
        try
        {
            config = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception cause) when (cause is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"could not read the Expo app config at {path}: {cause.Message}", cause);
        }

        // Expo reads the app config from under `expo` when the file has that key, else the whole file.
        return ProjectIdOf(config is JsonObject root && root["expo"] is JsonObject expo ? expo : config, fixedName);
    }

    /// <summary>The public app config <c>expo config</c> prints for <paramref name="projectRoot"/>, run with the project's own <c>expo</c> package.</summary>
    private static async Task<JsonNode?> EvaluatedConfigAsync(string projectRoot, IReadOnlyDictionary<string, string?> env, CancellationToken cancellationToken)
    {
        var cli = ExpoCli(projectRoot)
            ?? throw new InvalidOperationException($"the app config in {projectRoot} is dynamic and `expo` is not installed there to evaluate it; install it, or {PassIt}");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = projectRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(cli);
        start.ArgumentList.Add("config");
        start.ArgumentList.Add("--json");
        start.ArgumentList.Add("--type");
        start.ArgumentList.Add("public");

        // As eas-cli runs it: the id comes from the config and the run's environment, never from a `.env` file.
        start.Environment.Clear();
        foreach (var (name, value) in env)
        {
            if (value is not null)
            {
                start.Environment[name] = value;
            }
        }

        start.Environment["EXPO_NO_DOTENV"] = "1";

        string output;
        string error;
        int exitCode;
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ExpoConfigTimeout);
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                output = await stdout.ConfigureAwait(false);
                error = await stderr.ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                throw new InvalidOperationException($"`expo config` timed out after {ExpoConfigTimeout.TotalSeconds} seconds in {projectRoot}");
            }
        }
        catch (Exception cause) when (cause is System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException($"`expo config` failed in {projectRoot}: {cause.Message}", cause);
        }

        if (exitCode != 0)
        {
            var detail = error.Trim().Length > 0 ? error.Trim() : $"exited with code {exitCode}";
            throw new InvalidOperationException($"`expo config` failed in {projectRoot}: {detail}");
        }

        try
        {
            return JsonNode.Parse(output);
        }
        catch (JsonException cause)
        {
            throw new InvalidOperationException($"`expo config` in {projectRoot} printed no JSON config", cause);
        }
    }

    /// <summary>
    /// The <c>expo</c> CLI in the <c>node_modules</c> of <paramref name="projectRoot"/> or a parent, as Expo resolves it:
    /// never through <c>NODE_PATH</c> or Node's global folders, which hold some other project's <c>expo</c>.
    /// </summary>
    private static string? ExpoCli(string projectRoot)
    {
        for (var directory = projectRoot; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            var cli = Path.Combine(directory, "node_modules", "expo", "bin", "cli");
            if (File.Exists(cli))
            {
                return cli;
            }
        }

        return null;
    }

    /// <summary><c>extra.eas.projectId</c> of an app config, named after the file it came from when it is missing.</summary>
    private static string ProjectIdOf(JsonNode? config, string source)
    {
        if (config is JsonObject root && root["extra"] is JsonObject extra && extra["eas"] is JsonObject eas
            && eas["projectId"] is JsonValue value && value.TryGetValue<string>(out var id) && id.Trim().Length > 0)
        {
            return id.Trim();
        }

        throw new InvalidOperationException($"{source} has no `extra.eas.projectId`; {PassIt}");
    }
}
