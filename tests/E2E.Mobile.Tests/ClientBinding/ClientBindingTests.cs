// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E.Mobile.Tests.ClientBinding;

public sealed class ClientBindingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Keeps_an_explicit_provider_binding_when_ambient_daemon_settings_exist(bool remote)
    {
        using var ambient = new AmbientDaemon("http://127.0.0.1:1", "unrelated-token", "http");
        var binding = new SlotBinding
        {
            Client = new JsonObject { ["stateDir"] = "/leased-daemon", ["leaseId"] = "lease-1" },
            Daemon = remote ? new DeviceDaemon("https://leased.example", "lease-token") : null,
        };
        var created = new List<DeviceClientConfig>();
        var engine = new MobileEngine(
            new MobileOptions { Platform = MobilePlatform.Ios, TargetName = "ios", Device = "leased-device" },
            (session, connection) =>
            {
                created.Add(DeviceClientConfig.For(session, connection));
                return new FakeDeviceClient();
            });
        await engine.StartAsync(
            new MobileStartInfo { RunId = "run", TargetName = "ios", ProjectRoot = "/project", Env = new Dictionary<string, string?> { [DeviceBindings.Variable("ios")] = DeviceBindings.Encode([binding]) } },
            CancellationToken.None);

        var config = Assert.Single(created);
        Assert.Equal("e2e-ios-0", config.Session);
        JsonAssert.Args("""{"stateDir":"/leased-daemon","leaseId":"lease-1"}""", config.Settings);
        Assert.Equal(remote ? "https://leased.example" : "", config.DaemonBaseUrl);
        Assert.Equal(remote ? "lease-token" : "", config.DaemonAuthToken);
        Assert.Equal(remote ? null : "auto", config.DaemonTransport);
        Assert.Equal("http://127.0.0.1:1", Environment.GetEnvironmentVariable("AGENT_DEVICE_DAEMON_BASE_URL"));
    }

    /// <summary>The user's own agent-device daemon settings in the process environment, which a lease must never inherit or disturb.</summary>
    private sealed class AmbientDaemon : IDisposable
    {
        private readonly string?[] _before;

        public AmbientDaemon(string baseUrl, string token, string transport)
        {
            _before = [Get("AGENT_DEVICE_DAEMON_BASE_URL"), Get("AGENT_DEVICE_DAEMON_AUTH_TOKEN"), Get("AGENT_DEVICE_DAEMON_TRANSPORT")];
            Environment.SetEnvironmentVariable("AGENT_DEVICE_DAEMON_BASE_URL", baseUrl);
            Environment.SetEnvironmentVariable("AGENT_DEVICE_DAEMON_AUTH_TOKEN", token);
            Environment.SetEnvironmentVariable("AGENT_DEVICE_DAEMON_TRANSPORT", transport);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("AGENT_DEVICE_DAEMON_BASE_URL", _before[0]);
            Environment.SetEnvironmentVariable("AGENT_DEVICE_DAEMON_AUTH_TOKEN", _before[1]);
            Environment.SetEnvironmentVariable("AGENT_DEVICE_DAEMON_TRANSPORT", _before[2]);
        }

        private static string? Get(string name) => Environment.GetEnvironmentVariable(name);
    }
}
