// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests.Engine;

public sealed class ManifestTests
{
    [Fact]
    public void Declares_observation_actions_location_keyboard_scroll_history_and_screenshot()
    {
        var engine = Harness.Create().Engine;
        Assert.Equal(
            EngineCapabilities.Observation | EngineCapabilities.Actions | EngineCapabilities.Location | EngineCapabilities.Keyboard
            | EngineCapabilities.Scroll | EngineCapabilities.History | EngineCapabilities.Screenshot,
            engine.Capabilities);
        Assert.Equal("ios", engine.Platform);
        Assert.Equal("android", Harness.Create(options => options with { Platform = MobilePlatform.Android }).Engine.Platform);
        Assert.NotEqual("unknown", engine.Version);
    }

    [Fact]
    public void Requires_a_bundle_id_or_a_build_on_the_target_and_refuses_a_URL_for_now()
    {
        const string target = "ios";
        MobileEngine.ValidateApp(new MobileApp { BundleId = "com.example.app" }, null, target, MobilePlatform.Ios);
        MobileEngine.ValidateApp(new MobileApp { AppPath = "./build/App.app" }, null, target, MobilePlatform.Ios);
        var missing = Assert.Throws<ConfigurationException>(() => MobileEngine.ValidateApp(new MobileApp(), null, target, MobilePlatform.Ios));
        Assert.Equal("INVALID_CONFIG", missing.Code);
        Assert.Contains("target \"ios\" needs app.bundleId or app.appPath", missing.Message, StringComparison.Ordinal);

        var url = Assert.Throws<ConfigurationException>(
            () => MobileEngine.ValidateApp(new MobileApp { BundleId = "com.example.app" }, "http://127.0.0.1:3000", target, MobilePlatform.Ios));
        Assert.Equal("INVALID_CONFIG", url.Code);
        Assert.Matches(@"cannot open a website in the device's browser yet.*a simulator shares this machine's loopback", url.Message);
        var android = Assert.Throws<ConfigurationException>(() => MobileEngine.ValidateApp(new MobileApp(), "http://127.0.0.1:3000", target, MobilePlatform.Android));
        Assert.Contains("127.0.0.1 on an Android emulator is the emulator itself (this machine is 10.0.2.2)", android.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.2.2", url.Message, StringComparison.Ordinal);
    }
}
