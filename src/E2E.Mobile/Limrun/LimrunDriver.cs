// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.Mobile.Limrun;

/// <summary>What the driver attaches to a Limrun device: the daemon's state directory, the run, and the build to install.</summary>
internal sealed record LimrunDriverRequest(string StateDirectory, string RunId, int Slot, MobilePlatform Platform, string? App, string? AppPath);

/// <summary>The device the driver attached: how a client reaches it, the id agent-device lists it under, and the app it installed.</summary>
internal sealed record LimrunBinding(JsonObject Client, string DeviceId, string? InstalledApp = null);

/// <summary>
/// Attaches one worker to its Limrun device through the agent-device daemon that runs beside it: allocates the
/// device's lease, which the daemon's Limrun backend resolves from the instance address and token it started with,
/// and installs the target's build. Upstream does this in a child process so each daemon gets its own credentials
/// without changing the runner's environment. Here the daemon is its own process already, started with those
/// variables alone, so the driver runs in the runner.
/// </summary>
internal static class LimrunDriver
{
    /// <summary>The tenant every lease is scoped to.</summary>
    private const string Tenant = "e2e";

    public static async Task<LimrunBinding> AttachAsync(LimrunDriverRequest request, DeviceClientFactory connect, CancellationToken cancellationToken)
    {
        var platform = request.Platform.WireName();
        var allocator = connect("default", new DeviceConnection { Client = new JsonObject { ["stateDir"] = request.StateDirectory } });
        var lease = await allocator.CallAsync(
            DeviceMethods.LeasesAllocate,
            DeviceArgs.Of(
                ("tenant", Tenant),
                ("runId", request.RunId),
                ("clientId", $"worker-{request.Slot}"),
                ("leaseBackend", $"{platform}-instance"),
                ("leaseProvider", "limrun"),
                ("retainOnClose", true)),
            cancellationToken).ConfigureAwait(false) as JsonObject ?? throw new InvalidOperationException("agent-device returned no lease");

        // The daemon closes a lease with its session unless it is retained, and the lease must outlive the worker's sessions.
        if (lease["retainOnClose"] is not JsonValue retained || !retained.TryGetValue<bool>(out var retainedOnClose) || !retainedOnClose)
        {
            throw new InvalidOperationException("agent-device did not retain the Limrun lease across session close");
        }

        var client = new JsonObject
        {
            ["stateDir"] = request.StateDirectory,
            ["tenant"] = Required(lease, "tenantId"),
            ["runId"] = Required(lease, "runId"),
            ["leaseId"] = Required(lease, "leaseId"),
            ["leaseBackend"] = Required(lease, "backend"),
            ["leaseProvider"] = "limrun",
        };
        foreach (var key in new[] { "deviceKey", "clientId" })
        {
            if (Text(lease[key]) is { } value)
            {
                client[key] = value;
            }
        }

        var binding = new LimrunBinding(client, $"limrun:{platform}:{Required(lease, "leaseId")}");
        if (request.AppPath is null)
        {
            return binding;
        }

        var device = connect("default", new DeviceConnection { Client = client });
        var installed = await device.CallAsync(
            DeviceMethods.AppsInstall,
            DeviceArgs.Of(
                ("appPath", request.AppPath),
                (request.Platform == MobilePlatform.Ios ? "udid" : "serial", binding.DeviceId),
                ("app", request.App),
                ("platform", platform)),
            cancellationToken).ConfigureAwait(false);
        var id = Text(installed?["appId"]) ?? Text(installed?["bundleId"]) ?? Text(installed?["package"]) ?? request.App
            ?? throw new InvalidOperationException("Limrun installed the build but agent-device returned no app identifier");
        return binding with { InstalledApp = id };
    }

    private static string Required(JsonObject lease, string name) =>
        Text(lease[name]) ?? throw new InvalidOperationException($"Invalid lease response from daemon: no {name}");

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
}
