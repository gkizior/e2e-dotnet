// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.Mobile.Limrun;

/// <summary>Renews a device's idle lease while other targets run: retaining the lease across sessions does not keep it from expiring.</summary>
internal static class LimrunHeartbeat
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan BeatTimeout = TimeSpan.FromSeconds(10);
    private const int TtlMilliseconds = 60_000;

    /// <summary>
    /// Renews the lease once, so a lease that cannot be renewed fails the acquisition, then every 20 seconds until the
    /// returned function is called. A later renewal that fails is reported and retried, never raised. The function
    /// cancels a renewal still in flight and returns when it has ended, so it can run before the daemon stops.
    /// </summary>
    public static async Task<Func<Task>> StartAsync(
        LimrunBinding binding,
        DeviceClientFactory connect,
        Action<string> log,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        var leaseId = binding.Client["leaseId"] is JsonValue value && value.TryGetValue<string>(out var id) && id.Length > 0
            ? id
            : throw new InvalidOperationException("Limrun driver returned no agent-device lease id");

        // The lease's own stateDir selects the local daemon, whatever remote daemon the environment names.
        var client = connect("default", new DeviceConnection { Client = binding.Client });
        var stopping = new CancellationTokenSource();

        async Task BeatAsync(CancellationToken outer)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(outer, stopping.Token);
            linked.CancelAfter(BeatTimeout);
            await client.CallAsync(DeviceMethods.LeasesHeartbeat, DeviceArgs.Of(("leaseId", leaseId), ("ttlMs", TtlMilliseconds)), linked.Token).ConfigureAwait(false);
        }

        try
        {
            await BeatAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            stopping.Dispose();
            throw;
        }

        var loop = Task.Run(async () =>
        {
            while (!stopping.IsCancellationRequested)
            {
                try
                {
                    await delay(Interval, stopping.Token).ConfigureAwait(false);
                    await BeatAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception cause) when (!stopping.IsCancellationRequested)
                {
                    log($"driver lease renewal failed: {cause.Message}");
                }
                catch (Exception)
                {
                    // Stopped: a renewal cancelled in flight is the expected end.
                }
            }
        });

        async Task StopAsync()
        {
            await stopping.CancelAsync().ConfigureAwait(false);
            await loop.ConfigureAwait(false);
            stopping.Dispose();
        }

        // A release that failed is retried, and stops the heartbeat again.
        Task? stopped = null;
        return () => stopped ??= StopAsync();
    }
}
