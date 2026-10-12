// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;

namespace E2E.Mobile.Eas;

/// <summary>The clock and sleep the provider polls with: a test swaps them so a run of minutes takes no time.</summary>
internal sealed record EasClock(Func<long> NowMilliseconds, Func<TimeSpan, CancellationToken, Task> Delay)
{
    public static EasClock Real { get; } = new(() => Environment.TickCount64, (delay, token) => Task.Delay(delay, token));
}

/// <summary>
/// EAS Simulators for <see cref="MobileOptions.Provider"/>: one hosted simulator per worker slot, started when the run
/// starts and stopped when it ends, each driven through the agent-device daemon EAS runs beside it. Every session is
/// named after its target and slot and tagged with the run, in the project <see cref="EasSimulatorsOptions.ProjectId"/>
/// names or the app config links. It authenticates with <c>EXPO_TOKEN</c> from the run's environment, else with the
/// session <c>eas login</c> stored, as eas-cli does.
/// </summary>
public sealed class EasSimulators : IDeviceProvider
{
    /// <summary>
    /// EAS stops a session no agent-device command reached for this long: the backstop for a run that died before the
    /// engine could release its lease.
    /// </summary>
    private const int DefaultMaxIdleTimeMinutes = 10;

    /// <summary>How often a starting session is polled; a simulator takes about three minutes to queue, boot, and install a build.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>A session queued this long waits for capacity, not for a machine; every session queues briefly.</summary>
    private static readonly TimeSpan QueueNotice = TimeSpan.FromMinutes(2);

    /// <summary>A session that left the queue and is still not ready after this long is stuck booting or installing; eas-cli gives up at the same point.</summary>
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(15);

    /// <summary>State polls that may fail in a row, a blip at api.expo.dev, before the session is given up.</summary>
    private const int PollFailuresTolerated = 3;

    /// <summary>
    /// When EAS stops each ready session a run holds for idling, by run and session id, across every provider of the run:
    /// targets prepare one after another in the runner process, so a target's ready sessions sit idle while the next
    /// target's queue.
    /// </summary>
    private static readonly Dictionary<string, Dictionary<string, long>> IdleDeadlines = [];

    /// <summary>
    /// The project id each run reads from an app config, by run and project root, across every provider of the run, so two
    /// targets of one app read it once. A failed read is not kept.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task<string>> ProjectIds = new(StringComparer.Ordinal);

    private readonly EasSimulatorsOptions _options;
    private readonly int? _maxIdleTimeMinutes;
    private readonly HttpClient _http;
    private readonly EasClock _clock;
    private readonly ConcurrentDictionary<string, EasSessions> _clients = new(StringComparer.Ordinal);

    public EasSimulators(EasSimulatorsOptions? options = null)
        : this(options, AgentDeviceDaemon.SharedHttp, EasClock.Real)
    {
    }

    internal EasSimulators(EasSimulatorsOptions? options, HttpClient http, EasClock clock)
    {
        _options = options ?? new EasSimulatorsOptions();
        _http = http;
        _clock = clock;
        if (_options.MaxIdleTimeMinutes < 1 || _options.MaxDurationMinutes < 1)
        {
            throw new ConfigurationException("INVALID_CONFIG", "easSimulators: `maxIdleTimeMinutes` and `maxDurationMinutes` must be at least 1");
        }

        if (_options.BuildId is not null && _options.ApplicationArchiveUrl is not null)
        {
            throw new ConfigurationException("INVALID_CONFIG", "easSimulators: pass `buildId` or `applicationArchiveUrl`, not both");
        }

        if (_options.MaxIdleTimeMinutes is { } idle && _options.MaxDurationMinutes is { } duration && idle >= duration)
        {
            throw new ConfigurationException("INVALID_CONFIG", "easSimulators: `maxIdleTimeMinutes` must be below `maxDurationMinutes`, as EAS requires");
        }

        var idleMinutes = _options.MaxIdleTimeMinutes
            ?? (_options.MaxDurationMinutes is { } limit ? Math.Min(DefaultMaxIdleTimeMinutes, limit - 1) : DefaultMaxIdleTimeMinutes);

        // EAS takes no idle limit of zero; a session that short runs to its duration.
        _maxIdleTimeMinutes = idleMinutes >= 1 ? idleMinutes : null;
    }

    public string Name => "eas-simulators";

    /// <summary>A lease that remembers the client it was created with, so release stops it as the same account even if the eas-cli login changed during the run.</summary>
    private sealed class EasLease : DeviceLease
    {
        public required EasSessions Sessions { get; init; }
    }

    public async Task<DeviceLease> AcquireAsync(DeviceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.AppPath is not null && (_options.BuildId is not null || _options.ApplicationArchiveUrl is not null))
        {
            throw new InvalidOperationException("EAS installs the app from `buildId` or `applicationArchiveUrl`; leave the target's `app.appPath` out");
        }

        var appId = await ProjectIdAsync(request, cancellationToken).ConfigureAwait(false);
        var client = await ClientForAsync(request.Env, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // Not the request's token: an interrupt that lands after EAS created the session would leave it unknown, and billed.
        using var createTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var created = await client.CreateAsync(
            new EasSessionParams
            {
                AppId = appId,
                Platform = request.Platform,
                Name = $"e2e {request.TargetName} {request.Slot + 1} of {request.Slots}",
                Tags = [.. _options.Tags, "e2e", $"e2e-run:{request.RunId}", $"e2e-target:{request.TargetName}"],
                BuildId = _options.BuildId,
                ApplicationArchiveUrl = _options.ApplicationArchiveUrl,
                DeviceIdentifier = _options.Device,
                MaxIdleTimeMinutes = _maxIdleTimeMinutes,
                MaxRunTimeMinutes = _options.MaxDurationMinutes,
                PackageVersion = _options.AgentDeviceVersion ?? request.AgentDeviceVersion,
            },
            createTimeout.Token).ConfigureAwait(false);
        request.Log($"simulator session {created.Id}, {created.Url}");
        try
        {
            var session = await ReadyAsync(client, created.Id, request, cancellationToken).ConfigureAwait(false);
            if (session.OpenPreviewUrl is not null)
            {
                request.Log($"watch at {session.OpenPreviewUrl}");
            }

            if (_maxIdleTimeMinutes is { } idle)
            {
                lock (IdleDeadlines)
                {
                    if (!IdleDeadlines.TryGetValue(request.RunId, out var held))
                    {
                        held = [];
                        IdleDeadlines[request.RunId] = held;
                    }

                    held[created.Id] = _clock.NowMilliseconds() + (long)idle * 60_000;
                }
            }

            return new EasLease
            {
                Id = created.Id,
                Daemon = new DeviceDaemon(session.DaemonUrl!, session.DaemonToken),
                Sessions = client,
            };
        }
        catch (Exception cause)
        {
            // EAS bills a session from the moment it starts, and the engine holds only leases `acquire` returned.
            string outcome;
            try
            {
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await client.StopAsync(created.Id, stopTimeout.Token).ConfigureAwait(false);
                outcome = "stopped it";
            }
            catch (Exception stopCause)
            {
                outcome = $"stopping it failed, so EAS stops it at its idle or duration limit ({stopCause.Message})";
            }

            var reason = cancellationToken.IsCancellationRequested ? "cancelled" : cause.Message;
            throw new InvalidOperationException($"simulator session {created.Id} did not become ready: {reason}; {outcome}, {created.Url}", cause);
        }
    }

    public async Task ReleaseAsync(DeviceLease lease, DeviceReleaseContext context, CancellationToken cancellationToken)
    {
        foreach (var key in ProjectIds.Keys.Where(key => key.StartsWith($"{context.RunId}\0", StringComparison.Ordinal)))
        {
            ProjectIds.TryRemove(key, out _);
        }

        lock (IdleDeadlines)
        {
            if (IdleDeadlines.TryGetValue(context.RunId, out var held))
            {
                held.Remove(lease.Id);
                if (held.Count == 0)
                {
                    IdleDeadlines.Remove(context.RunId);
                }
            }
        }

        var client = lease is EasLease own ? own.Sessions : await ClientForAsync(context.Env, cancellationToken).ConfigureAwait(false);
        await client.StopAsync(lease.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> ProjectIdAsync(DeviceRequest request, CancellationToken cancellationToken)
    {
        if (_options.ProjectId is { } projectId)
        {
            return projectId;
        }

        var key = $"{request.RunId}\0{request.ProjectRoot}";
        var resolved = ProjectIds.GetOrAdd(key, _ => ExpoProject.ProjectIdAsync(request.ProjectRoot, request.Env, cancellationToken));
        try
        {
            return await resolved.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Dropped before the failure is reported, so the next request reads the config again.
            ProjectIds.TryRemove(new KeyValuePair<string, Task<string>>(key, resolved));
            throw;
        }
    }

    private async Task<EasSessions> ClientForAsync(IReadOnlyDictionary<string, string?> env, CancellationToken cancellationToken)
    {
        var credentials = await ExpoCredentials.ReadAsync(env, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{ExpoCredentials.TokenVariable} is not set and eas-cli is not logged in; run `eas login` or set {ExpoCredentials.TokenVariable}");
        return _clients.GetOrAdd(credentials.Key, _ => new EasSessions(credentials, _http));
    }

    /// <summary>
    /// Polls a session until its agent-device daemon is reachable. Says once when it has queued for two minutes, and gives
    /// up when it is still queued once another session of the run has idled to its limit, boots for longer than eas-cli
    /// waits, ends, or the API fails several polls in a row. The idle clock counts from ready: warm-up commands may reset
    /// it at EAS, so the give up can come that much early, never late.
    /// </summary>
    private async Task<EasSessionState> ReadyAsync(EasSessions client, string id, DeviceRequest request, CancellationToken cancellationToken)
    {
        var queuedSince = _clock.NowMilliseconds();
        long? startedAt = null;
        var noticed = false;
        var failures = 0;
        while (true)
        {
            EasSessionState state;
            try
            {
                state = await client.StateAsync(id, cancellationToken).ConfigureAwait(false);
                failures = 0;
            }
            catch (Exception)
            {
                failures++;
                if (cancellationToken.IsCancellationRequested || failures > PollFailuresTolerated)
                {
                    throw;
                }

                await _clock.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            switch (state.Phase)
            {
                case EasPhase.Ready:
                    return state;
                case EasPhase.Ended:
                    throw new InvalidOperationException($"session {state.EndedStatus}");
            }

            var now = _clock.NowMilliseconds();
            if (state.Phase == EasPhase.Queued)
            {
                if (EarliestIdleDeadline(request.RunId) is { } deadline && now >= deadline)
                {
                    throw new InvalidOperationException(
                        "still queued while another session of this run idled to its `maxIdleTimeMinutes`, where EAS stops it; lower `workers` or raise `maxIdleTimeMinutes`");
                }

                if (!noticed && now - queuedSince >= QueueNotice.TotalMilliseconds)
                {
                    noticed = true;
                    request.Log("still queued at EAS after two minutes; tests start once every worker has a simulator");
                }
            }
            else
            {
                startedAt ??= now;
                if (now - startedAt >= StartupTimeout.TotalMilliseconds)
                {
                    throw new InvalidOperationException($"still starting after {StartupTimeout.TotalMinutes} minutes");
                }
            }

            await _clock.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static long? EarliestIdleDeadline(string runId)
    {
        lock (IdleDeadlines)
        {
            return IdleDeadlines.TryGetValue(runId, out var held) && held.Count > 0 ? held.Values.Min() : null;
        }
    }
}
