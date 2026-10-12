// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Mobile.Limrun;
using Driver = E2E.Mobile.Limrun.LimrunDriver;

namespace E2E.Mobile.Tests.LimrunDriver;

/// <summary>The driver against a scripted agent-device client. Ports <c>packages/limrun/tests/unit/driver.test.ts</c>.</summary>
public sealed class LimrunDriverTests
{
    [Theory]
    [InlineData(MobilePlatform.Ios)]
    [InlineData(MobilePlatform.Android)]
    public async Task Uses_the_requested_app_when_installation_returns_no_identifier(MobilePlatform platform)
    {
        var client = new FakeDeviceClient();
        client.Respond(DeviceMethods.LeasesAllocate, _ => new JsonObject
        {
            ["retainOnClose"] = true,
            ["tenantId"] = "e2e",
            ["runId"] = "run",
            ["leaseId"] = "lease",
            ["backend"] = "ios-instance",
        });
        client.Respond(DeviceMethods.AppsInstall, _ => new JsonObject());

        var binding = await Driver.AttachAsync(
            new LimrunDriverRequest("/daemon", "run", 0, platform, "com.example.app", "/build"),
            (_, _) => client,
            CancellationToken.None);

        Assert.Equal("com.example.app", binding.InstalledApp);
        Assert.Equal($"limrun:{platform.WireName()}:lease", binding.DeviceId);
        JsonAssert.Args(
            $$"""{"tenant":"e2e","runId":"run","clientId":"worker-0","leaseBackend":"{{platform.WireName()}}-instance","leaseProvider":"limrun","retainOnClose":true}""",
            client.LastArgs(DeviceMethods.LeasesAllocate));
        JsonAssert.Args(
            $$"""{"appPath":"/build","{{(platform == MobilePlatform.Ios ? "udid" : "serial")}}":"limrun:{{platform.WireName()}}:lease","app":"com.example.app","platform":"{{platform.WireName()}}"}""",
            client.LastArgs(DeviceMethods.AppsInstall));
    }
}
