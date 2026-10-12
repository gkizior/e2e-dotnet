// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile.Tests.Tools;

public sealed class AgentToolPackTests
{
    private static Harness Android() => Harness.Create(options => options with { Platform = MobilePlatform.Android });

    private static Task<string> Run(IReadOnlyDictionary<string, DeviceTool> tools, string name, string input) =>
        tools[name].ExecuteAsync((JsonObject)JsonNode.Parse(input)!, CancellationToken.None);

    [Fact]
    public void Scopes_every_tool_to_the_platforms_of_its_engines()
    {
        var ios = Harness.Create().Engine;
        var android = Android().Engine;
        var tools = MobileTools.Create(ios);
        Assert.Equal(["alert", "open_app", "swipe"], tools.Keys.Order());
        Assert.All(tools.Values, tool => Assert.Equal([MobilePlatform.Ios], tool.Platforms));
        Assert.All(MobileTools.Create(ios, android).Values, tool => Assert.Equal([MobilePlatform.Ios, MobilePlatform.Android], tool.Platforms));
    }

    [Fact]
    public async Task Dispatches_to_the_engine_whose_attempt_is_running_and_refuses_outside_an_attempt()
    {
        var iosHarness = Harness.Create();
        var androidHarness = Android();
        iosHarness.Fake.Respond(DeviceMethods.AppsOpen, _ => new JsonObject { ["appName"] = "Reminders" });
        androidHarness.Fake.Respond(DeviceMethods.AppsOpen, _ => new JsonObject { ["appName"] = "Clock" });
        var tools = MobileTools.Create(iosHarness.Engine, androidHarness.Engine);

        var idle = await Assert.ThrowsAsync<EngineException>(() => Run(tools, "alert", """{"action":"dismiss"}"""));
        Assert.Equal("INVALID_STATE", idle.Code);

        var androidSession = await androidHarness.Boot("android");
        Assert.Equal("Opened Clock.", await Run(tools, "open_app", """{"app":"Clock","relaunch":true}"""));
        Assert.Matches("Swiped", await Run(tools, "swipe", """{"from":{"x":300,"y":200},"to":{"x":20,"y":200}}"""));
        Assert.Equal("Alert dismissed.", await Run(tools, "alert", """{"action":"dismiss"}"""));
        Assert.Equal(["devices.boot", "apps.open", "interactions.swipe", "command.alert"], androidHarness.Fake.Methods());
        JsonAssert.Args("""{"app":"Clock","platform":"android","relaunch":true}""", androidHarness.Fake.LastArgs("apps.open"));
        Assert.Empty(iosHarness.Fake.Methods());
        await androidSession.CloseAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        await iosHarness.Boot("ios");
        Assert.Equal("Opened Reminders.", await Run(tools, "open_app", """{"app":"Reminders"}"""));
        JsonAssert.Args("""{"app":"Reminders","platform":"ios"}""", iosHarness.Fake.LastArgs("apps.open"));
    }

    [Fact]
    public async Task Refuses_a_link_the_model_hands_to_open_app_before_the_device_sees_it()
    {
        var h = Harness.Create();
        h.Fake.Respond(DeviceMethods.AppsOpen, _ => new JsonObject { ["appName"] = "Settings" });
        await h.Boot("ios");
        var tools = MobileTools.Create(h.Engine);
        var before = h.Fake.Calls.Count;
        foreach (var denied in new[] { "file:///etc/passwd", "data:text/html,hi", "javascript:alert(1)", "view-source:file:///etc/passwd" })
        {
            var failure = await Assert.ThrowsAsync<ConfigurationException>(() => Run(tools, "open_app", $$"""{"app":"{{denied}}"}"""));
            Assert.Equal("POLICY_DENIED", failure.Code);
        }

        foreach (var link in new[] { "https://example.com/verify", "myapp://orders/42" })
        {
            var failure = await Assert.ThrowsAsync<TestException>(() => Run(tools, "open_app", $$"""{"app":"{{link}}"}"""));
            Assert.Equal("INVALID_ARGUMENT", failure.Code);
            Assert.Contains("device.openLink", failure.Message, StringComparison.Ordinal);
        }

        Assert.Equal(before, h.Fake.Calls.Count);
        Assert.Contains("not a URL", tools["open_app"].Description, StringComparison.Ordinal);
        Assert.Equal("Opened com.apple.Preferences.", await Run(tools, "open_app", """{"app":"com.apple.Preferences"}"""));
        JsonAssert.Args("""{"app":"com.apple.Preferences","platform":"ios"}""", h.Fake.LastArgs("apps.open"));
    }
}
