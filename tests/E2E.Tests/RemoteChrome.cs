// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests;

/// <summary>
/// A local Chrome process with a random remote-debugging port, owned independently
/// of the engine's CDP transport: the host the engine attaches to.
/// </summary>
internal sealed partial class RemoteChrome : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _userDataDir;

    private RemoteChrome(Process process, string userDataDir)
    {
        _process = process;
        _userDataDir = userDataDir;
    }

    public string Endpoint { get; private set; } = "";

    /// <summary>Whether Chrome is still running; the engine never terminates the host.</summary>
    public bool Running => !_process.HasExited;

    /// <summary>Launches the remote host and releases its resources if it never becomes ready.</summary>
    public static async Task<RemoteChrome> LaunchAsync()
    {
        // The host runs the full Chromium build, which a headless install skips.
        await WebEngine.EnsureChromiumAsync(headed: true, CancellationToken.None);
        string executable;
        using (var playwright = await Playwright.CreateAsync())
        {
            executable = playwright.Chromium.ExecutablePath;
        }

        var userDataDir = Directory.CreateTempSubdirectory("e2e-cdp-host-").FullName;
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        // The sandbox flags are what a containerized CI runner needs to start Chrome
        // at all; they change nothing about the attach under test.
        foreach (var argument in new[]
        {
            "--headless=new",
            "--remote-debugging-port=0",
            "--no-first-run",
            "--no-default-browser-check",
            "--password-store=basic",
            "--use-mock-keychain",
            "--disable-gpu",
            "--disable-extensions",
            "--disable-component-extensions-with-background-pages",
            "--no-sandbox",
            "--disable-setuid-sandbox",
            "--disable-dev-shm-usage",
            "--user-data-dir=" + userDataDir,
            "about:blank",
        })
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var chrome = new RemoteChrome(process, userDataDir);
        var ready = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var buffered = new StringBuilder();
        process.ErrorDataReceived += (_, line) =>
        {
            if (line.Data is null)
            {
                return;
            }

            lock (buffered)
            {
                buffered.AppendLine(line.Data);
            }

            var match = EndpointPattern().Match(line.Data);
            if (match.Success)
            {
                ready.TrySetResult(match.Groups[1].Value);
            }
        };
        process.OutputDataReceived += (_, _) => { };

        // Surface Chrome's own stderr: it names the missing flag or library.
        process.Exited += (_, _) =>
        {
            lock (buffered)
            {
                ready.TrySetException(new InvalidOperationException("Chrome exited before it was ready:\n" + buffered.ToString().Trim()));
            }
        };
        try
        {
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();
            chrome.Endpoint = await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
            return chrome;
        }
        catch
        {
            await chrome.DisposeAsync();
            throw;
        }
    }

    /// <summary>Waits for Chrome to exit before removing the profile it writes.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }

            await _process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The process never started.
        }

        _process.Dispose();
        try
        {
            Directory.Delete(_userDataDir, recursive: true);
        }
        catch (IOException)
        {
            // A helper process still holds a file; the temp directory is reclaimed later.
        }
    }

    [GeneratedRegex(@"DevTools listening on (ws://\S+)")]
    private static partial Regex EndpointPattern();
}
