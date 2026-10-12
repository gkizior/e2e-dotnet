// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>What the engine asks a provider for: one device for one worker slot of a run.</summary>
public sealed record DeviceRequest
{
    /// <summary>Platform the target's device must run.</summary>
    public required MobilePlatform Platform { get; init; }

    /// <summary>The run id, for naming the session at the provider.</summary>
    public required string RunId { get; init; }

    /// <summary>The target the device serves.</summary>
    public required string TargetName { get; init; }

    /// <summary>Worker slot the device serves, <c>0</c> to <c>Slots - 1</c>. <see cref="Slots"/> leases are acquired at once.</summary>
    public required int Slot { get; init; }

    /// <summary>Devices the run acquires for this target, in all.</summary>
    public required int Slots { get; init; }

    /// <summary>
    /// The agent-device version this engine's client speaks, for a provider that picks the daemon version it
    /// starts: a daemon of another version may answer the same commands differently.
    /// </summary>
    public required string AgentDeviceVersion { get; init; }

    /// <summary>The target's bundle id: the bundle id or package <c>app.open()</c> launches, when the config names one.</summary>
    public string? App { get; init; }

    /// <summary>
    /// The build the engine would install, resolved to an absolute path, when the config names one. A provider that
    /// installs it itself reports <see cref="DeviceLease.InstalledApp"/> on the lease and the engine skips its own install.
    /// </summary>
    public string? AppPath { get; init; }

    /// <summary>The directory the config's relative paths resolve against.</summary>
    public required string ProjectRoot { get; init; }

    /// <summary>The run's environment: where a provider reads its token from.</summary>
    public required IReadOnlyDictionary<string, string?> Env { get; init; }

    /// <summary>Reports one line of progress to the run's reporter, such as a session URL to watch.</summary>
    public required Action<string> Log { get; init; }
}

/// <summary>
/// One leased device, as <see cref="IDeviceProvider.AcquireAsync"/> returns it and
/// <see cref="IDeviceProvider.ReleaseAsync"/> gets it back. A provider may derive from it and keep further fields:
/// release gets that same object, while only the declared fields travel to the worker.
/// </summary>
public class DeviceLease
{
    /// <summary>The provider's handle on the lease (a session id), named in progress lines and handed back to release.</summary>
    public string Id { get; init; } = "";

    /// <summary>The agent-device daemon the worker connects to instead of its local one. A lease names a daemon, client configuration, or both.</summary>
    public DeviceDaemon? Daemon { get; init; }

    /// <summary>
    /// agent-device client configuration the worker's client is created with. JSON data only. <c>session</c>,
    /// <c>daemonBaseUrl</c>, <c>daemonAuthToken</c>, and <c>daemonTransport</c> are refused: the engine and
    /// <see cref="Daemon"/> own those.
    /// </summary>
    public JsonObject? Client { get; init; }

    /// <summary>Device to select inside that daemon by name, when it hosts more than one.</summary>
    public string? Device { get; init; }

    /// <summary>Device to select inside that daemon by the id agent-device lists it under: a simulator UDID or an Android serial.</summary>
    public string? DeviceId { get; init; }

    /// <summary>
    /// Bundle id or package of the app the provider installed from the request's <see cref="DeviceRequest.AppPath"/>,
    /// and only then. A lease reporting one for a request without <c>AppPath</c> fails the run.
    /// </summary>
    public string? InstalledApp { get; init; }
}

/// <summary>Handed to release, once per lease at the end of the run.</summary>
public sealed record DeviceReleaseContext
{
    public required string RunId { get; init; }

    public required string TargetName { get; init; }

    /// <summary>The run's environment, the same acquire saw.</summary>
    public required IReadOnlyDictionary<string, string?> Env { get; init; }

    public required Action<string> Log { get; init; }
}

/// <summary>
/// The seam a hosted device service plugs into: a provider leases one device per worker slot for the run and
/// hands back the agent-device daemon that drives it. The engine acquires in prepare, drives each lease from its
/// worker, and releases in finish, on every exit path. Nothing here knows any vendor.
/// <para>
/// Upstream also lets a provider record the device in place of agent-device. That part is not ported yet.
/// </para>
/// </summary>
public interface IDeviceProvider
{
    /// <summary>Label in progress lines and error messages.</summary>
    string Name { get; }

    Task<DeviceLease> AcquireAsync(DeviceRequest request, CancellationToken cancellationToken);

    Task ReleaseAsync(DeviceLease lease, DeviceReleaseContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Devices a provider leases, one per slot, all at once: hosted sessions start in parallel and are billed from
/// the moment they do, so nothing waits on a sibling. Every lease granted is held for finish before the outcome is
/// known, so a slot that fails to lease fails the run and the others are still released.
/// </summary>
internal sealed class LeasedDevices(IDeviceProvider provider, MobilePlatform platform) : IDeviceSource
{
    private const string RejectedLease = "returned a lease without an id and a daemon baseUrl or JSON client configuration (no `session` or daemon keys)";

    /// <summary>Leases granted so far, per target and slot, filled as each acquire settles. A lease the engine then rejects is among them.</summary>
    private readonly Dictionary<string, DeviceLease?[]> _held = [];

    public int? Size => null;

    public IReadOnlyList<SlotBinding>? Fallback => null;

    public async Task<IReadOnlyList<SlotBinding>> BindAsync(MobilePrepareInfo info, CancellationToken cancellationToken)
    {
        var app = info.App.BundleId;
        var appPath = info.App.AppPath is null ? null : Path.GetFullPath(info.App.AppPath, info.ProjectRoot);
        var held = new DeviceLease?[info.Slots];
        lock (_held)
        {
            _held[info.TargetName] = held;
        }

        info.Log($"leasing {info.Slots} {platform.WireName()} device(s) from {provider.Name}");
        var leases = await AllOrFirstFailureAsync(
            Enumerable.Range(0, info.Slots).Select<int, Func<Task<DeviceLease>>>(slot => async () =>
            {
                var lease = await provider.AcquireAsync(
                    new DeviceRequest
                    {
                        Platform = platform,
                        RunId = info.RunId,
                        TargetName = info.TargetName,
                        Slot = slot,
                        Slots = info.Slots,
                        AgentDeviceVersion = AgentDeviceVersion.Pinned,
                        App = app,
                        AppPath = appPath,
                        ProjectRoot = info.ProjectRoot,
                        Env = info.Env,
                        Log = line => info.Log($"{provider.Name} ({slot + 1} of {info.Slots}): {line}"),
                    },
                    cancellationToken).ConfigureAwait(false);
                if (lease is null || lease.Id.Length == 0)
                {
                    throw new InvalidOperationException(RejectedLease);
                }

                // Held before it is checked: the provider bills for what it granted, and release is owed this same object however the check goes.
                held[slot] = lease;

                if (!IsDeviceLease(lease))
                {
                    throw new InvalidOperationException(RejectedLease);
                }

                if (lease.InstalledApp is not null && appPath is null)
                {
                    throw new InvalidOperationException("reported an installed app for a request without `appPath`");
                }

                // The pool opens this on the device in prepare. agent-device would open a link as a URL.
                if (lease.InstalledApp is not null && DeviceLinks.IsLink(lease.InstalledApp))
                {
                    throw new InvalidOperationException("reported an installed app that is a link, not a bundle id or package");
                }

                info.Log($"{provider.Name}: leased {lease.Id}{(lease.Device is null ? "" : $" ({lease.Device})")}");
                return lease;
            }),
            $"device provider \"{provider.Name}\" could not lease a device").ConfigureAwait(false);
        return
        [
            .. leases.Select(lease => new SlotBinding
            {
                LeaseId = lease.Id,
                Device = lease.Device,
                DeviceId = lease.DeviceId,
                Daemon = lease.Daemon,
                Client = lease.Client,
                InstalledApp = lease.InstalledApp,
            }),
        ];
    }

    public async Task FinishAsync(MobileFinishInfo info, CancellationToken cancellationToken)
    {
        DeviceLease?[]? granted;
        lock (_held)
        {
            _held.Remove(info.TargetName, out granted);
        }

        var leases = granted?.OfType<DeviceLease>().ToList();
        if (leases is null || leases.Count == 0)
        {
            return;
        }

        var context = new DeviceReleaseContext
        {
            RunId = info.RunId,
            TargetName = info.TargetName,
            Env = info.Env,
            Log = line => info.Log($"{provider.Name}: {line}"),
        };
        await AllOrFirstFailureAsync(
            leases.Select<DeviceLease, Func<Task<object?>>>(lease => async () =>
            {
                await provider.ReleaseAsync(lease, context, cancellationToken).ConfigureAwait(false);
                return null;
            }),
            $"device provider \"{provider.Name}\" could not release a device").ConfigureAwait(false);
        info.Log($"{provider.Name}: released {leases.Count} device(s)");
    }

    /// <summary>A granted lease checked field by field: the engine trusts nothing it did not write.</summary>
    private static bool IsDeviceLease(DeviceLease lease) =>
        (lease.Daemon is not null || lease.Client is not null)
        && (lease.Client is null || DeviceBindings.IsClientConfig(lease.Client));

    /// <summary>Runs every task, then reports the first failure: nothing is skipped because a sibling failed.</summary>
    private static async Task<List<T>> AllOrFirstFailureAsync<T>(IEnumerable<Func<Task<T>>> tasks, string describe)
    {
        // A task that throws before its first await is a failed task like any other, not an escape.
        var running = tasks.Select(async task => await task().ConfigureAwait(false)).ToList();
        try
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Every task has finished; the first failure in order is reported below.
        }

        if (running.Find(task => task.IsFaulted || task.IsCanceled) is { } failed)
        {
            var cause = failed.Exception?.GetBaseException() ?? new OperationCanceledException();
            throw new EngineException(EngineErrorCodes.EngineFailure, $"{describe}: {DeviceErrors.Message(cause)}", retryable: false, cause);
        }

        return [.. running.Select(task => task.Result)];
    }
}
