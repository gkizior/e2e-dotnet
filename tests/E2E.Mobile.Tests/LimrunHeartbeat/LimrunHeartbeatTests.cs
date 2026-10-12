// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using E2E.Mobile.Limrun;
using Heartbeat = E2E.Mobile.Limrun.LimrunHeartbeat;

namespace E2E.Mobile.Tests.LimrunHeartbeat;

/// <summary>The lease heartbeat on a clock the test moves. Ports <c>packages/limrun/tests/unit/heartbeat.test.ts</c>.</summary>
public sealed class LimrunHeartbeatTests
{
    /// <summary>A daemon that answers every lease request, remembering the JSON-RPC requests it got.</summary>
    private sealed class FakeDaemon : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
            lock (Requests)
            {
                Requests.Add(body);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"jsonrpc":"2.0","id":1,"result":{"ok":true,"data":{"lease":{"leaseId":"lease-2"}}}}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    [Fact]
    public async Task Renews_an_idle_scoped_lease_beyond_its_expiry_window_and_stops_before_daemon_cleanup()
    {
        var ambient = new[] { "AGENT_DEVICE_DAEMON_BASE_URL", "AGENT_DEVICE_DAEMON_AUTH_TOKEN", "AGENT_DEVICE_DAEMON_TRANSPORT" };
        var before = ambient.Select(Environment.GetEnvironmentVariable).ToArray();
        Environment.SetEnvironmentVariable(ambient[0], "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable(ambient[1], "unrelated-token");
        Environment.SetEnvironmentVariable(ambient[2], "http");
        try
        {
            var daemon = new FakeDaemon();
            var clock = new ManualDelay();
            var binding = new LimrunBinding(
                new JsonObject { ["stateDir"] = "/worker-2", ["tenant"] = "e2e", ["runId"] = "run", ["leaseId"] = "lease-2" },
                "device-2");
            var created = new List<DeviceClientConfig>();

            // The real client over a fake daemon, so the request that reaches it is the one a real daemon would get.
            var stop = await Heartbeat.StartAsync(
                binding,
                (session, connection) =>
                {
                    created.Add(DeviceClientConfig.For(session, connection));
                    return new AgentDeviceRpcClient(session, connection!.Client, _ => Task.FromResult(new DaemonEndpoint(new Uri("http://127.0.0.1:1/"), "token")), new HttpClient(daemon));
                },
                _ => { },
                clock.Delay,
                CancellationToken.None);

            var config = Assert.Single(created);
            JsonAssert.Args("""{"stateDir":"/worker-2","tenant":"e2e","runId":"run","leaseId":"lease-2"}""", config.Settings);
            Assert.Equal("", config.DaemonBaseUrl);
            Assert.Equal("", config.DaemonAuthToken);
            Assert.Equal("auto", config.DaemonTransport);
            await clock.AdvanceAsync(TimeSpan.FromSeconds(140));
            Assert.Equal(8, daemon.Requests.Count);
            Assert.All(daemon.Requests, request => Assert.Equal("agent_device.lease.heartbeat", request["method"]!.GetValue<string>()));
            JsonAssert.Args("""{"tenantId":"e2e","runId":"run","leaseId":"lease-2","ttlMs":60000}""", daemon.Requests[^1]["params"]);
            await stop();
            await clock.AdvanceAsync(TimeSpan.FromSeconds(140));
            Assert.Equal(8, daemon.Requests.Count);
        }
        finally
        {
            for (var i = 0; i < ambient.Length; i++)
            {
                Environment.SetEnvironmentVariable(ambient[i], before[i]);
            }
        }
    }

    [Fact]
    public async Task Reports_transient_renewal_failures_retries_and_cancels_an_in_flight_beat_on_release()
    {
        var log = new List<string>();
        var clock = new ManualDelay();
        var client = new FakeDeviceClient();
        var beats = 0;
        var hung = new TaskCompletionSource<CancellationToken>();
        client.RespondAsync(DeviceMethods.LeasesHeartbeat, async (_, token) =>
        {
            switch (Interlocked.Increment(ref beats))
            {
                case 1:
                    return new JsonObject();
                case 2:
                    throw new InvalidOperationException("transport unavailable");
                default:
                    hung.TrySetResult(token);
                    await Task.Delay(Timeout.Infinite, token);
                    return null;
            }
        });
        var stop = await Heartbeat.StartAsync(
            new LimrunBinding(new JsonObject { ["leaseId"] = "lease" }, "device"),
            (_, _) => client,
            log.Add,
            clock.Delay,
            CancellationToken.None);

        await clock.AdvanceAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(["driver lease renewal failed: transport unavailable"], log);

        await clock.AdvanceAsync(TimeSpan.FromSeconds(20), expectNext: false);
        var inFlight = await hung.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await stop();

        Assert.True(inFlight.IsCancellationRequested);
        Assert.Single(log);
        await clock.AdvanceAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(3, beats);
    }
}
