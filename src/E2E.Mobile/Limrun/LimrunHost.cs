// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.ComponentModel;
using System.Diagnostics;

namespace E2E.Mobile.Limrun;

/// <summary>What the provider needs of the machine it runs on: the Android tools, the daemon beside each device, and its lease. A test swaps it for a fake.</summary>
internal interface ILimrunHost
{
    Task AssertAndroidToolsAsync(IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken);

    Task<LimrunBinding> StartDriverAsync(LimrunDriverRequest request, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken);

    Task StopDriverAsync(string stateDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken);

    /// <summary>Starts renewing the binding's lease; the function it returns stops that and waits for it.</summary>
    Task<Func<Task>> StartHeartbeatAsync(LimrunBinding binding, Action<string> log, CancellationToken cancellationToken);
}

/// <summary>The real host: a local agent-device daemon per device, started with that device's credentials alone.</summary>
internal sealed class LimrunHost : ILimrunHost
{
    private static readonly DeviceClientFactory Connect = static (session, connection) => DeviceClientConfig.For(session, connection).Connect();

    public static LimrunHost Instance { get; } = new();

    public async Task AssertAndroidToolsAsync(IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("adb", "version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.Environment.Clear();
        foreach (var (key, value) in environment)
        {
            if (value is not null)
            {
                start.Environment[key] = value;
            }
        }

        try
        {
            using var process = Process.Start(start) ?? throw new Win32Exception("adb did not start");
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }

            await Task.WhenAll(output, error).ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"adb version exited with code {process.ExitCode}");
            }
        }
        catch (Exception cause) when (cause is Win32Exception or InvalidOperationException)
        {
            throw new InvalidOperationException("Limrun Android requires the Android SDK Platform-Tools adb executable on PATH. The device still runs in Limrun.", cause);
        }
    }

    public async Task<LimrunBinding> StartDriverAsync(LimrunDriverRequest request, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        // Starts the daemon this device's lease runs in, with the instance's address and token among its variables.
        await AgentDeviceDaemon.LocalAsync(request.StateDirectory, cancellationToken, environment).ConfigureAwait(false);
        return await LimrunDriver.AttachAsync(request, Connect, cancellationToken).ConfigureAwait(false);
    }

    public Task StopDriverAsync(string stateDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken) =>
        AgentDeviceDaemon.StopAsync(stateDirectory, environment, cancellationToken);

    public Task<Func<Task>> StartHeartbeatAsync(LimrunBinding binding, Action<string> log, CancellationToken cancellationToken) =>
        LimrunHeartbeat.StartAsync(binding, Connect, log, static (delay, token) => Task.Delay(delay, token), cancellationToken);
}
