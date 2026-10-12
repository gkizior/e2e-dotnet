// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile.Tests.Engine;

public sealed class LifecycleTests
{
    private static readonly string[] BootRunnerOpen = ["devices.boot", "command.prepare", "apps.open"];

    private static JsonArray Inventory(params (string Platform, string Id, string Name, bool Booted)[] devices) =>
        [.. devices.Select(device => (JsonNode?)new JsonObject { ["platform"] = device.Platform, ["id"] = device.Id, ["name"] = device.Name, ["booted"] = device.Booted })];

    private static List<string> Methods(Harness harness, string method) => [.. harness.Fake.Methods().Where(name => name == method)];

    [Fact]
    public async Task Boots_under_a_session_named_after_the_target_and_worker_slot_launches_the_app_only_when_asked_and_closes_on_dispose()
    {
        var h = Harness.Create();
        var session = await h.Boot();
        await session.RestartAsync(CancellationToken.None);
        Assert.Equal(["e2e-ios-simulator-0"], h.Sessions);
        Assert.Equal(["devices.boot", "apps.open"], h.Fake.Methods());
        JsonAssert.Args("""{"platform":"ios"}""", h.Fake.LastArgs("devices.boot"));
        JsonAssert.Args("""{"app":"Settings","platform":"ios","relaunch":true}""", h.Fake.LastArgs("apps.open"));

        await session.CloseAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal("sessions.close", h.Fake.Methods()[^1]);
        await session.CloseAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Single(Methods(h, "sessions.close"));

        // A closed session is gone, and the engine boots another: the engine outlives a worker.
        await h.Boot("second");
        Assert.Equal(["e2e-ios-simulator-0", "e2e-second-0"], h.Sessions);
    }

    [Fact]
    public async Task Honours_an_explicit_session_and_device_and_does_not_open_anything_without_a_pinned_app()
    {
        var h = Harness.Create(options => options with { Session = "qa-run", Device = "iPhone 16e" }, pinned: false);
        await h.Boot();
        Assert.Equal(["qa-run-0"], h.Sessions);
        Assert.Equal(["devices.boot"], h.Fake.Methods());
        JsonAssert.Args("""{"platform":"ios","device":"iPhone 16e"}""", h.Fake.LastArgs("devices.boot"));
    }

    [Fact]
    public async Task Hands_each_worker_slot_its_own_device_from_a_pool_under_a_slot_suffixed_session()
    {
        string[] pool = ["iPhone 17", "iPhone 17 Pro"];
        var first = Harness.Create(options => options with { Devices = pool });
        var firstSession = await first.Boot("ios", 0);
        Assert.Equal(["e2e-ios-0"], first.Sessions);
        JsonAssert.Args("""{"platform":"ios","device":"iPhone 17"}""", first.Fake.LastArgs("devices.boot"));
        await firstSession.RestartAsync(CancellationToken.None);
        JsonAssert.Args("""{"app":"Settings","platform":"ios","device":"iPhone 17","relaunch":true}""", first.Fake.LastArgs("apps.open"));

        var second = Harness.Create(options => options with { Devices = pool, Session = "qa" });
        await second.Boot("ios", 1);
        Assert.Equal(["qa-1"], second.Sessions);
        JsonAssert.Args("""{"platform":"ios","device":"iPhone 17 Pro"}""", second.Fake.LastArgs("devices.boot"));

        var third = Harness.Create(options => options with { Devices = pool });
        var outside = await Assert.ThrowsAsync<EngineException>(() => third.Boot("ios", 2));
        Assert.Equal("ENGINE_FAILURE", outside.Code);
        Assert.Contains("worker slot 2 is outside a device pool of 2", outside.Message, StringComparison.Ordinal);
        Assert.Empty(third.Fake.Methods());
    }

    [Fact]
    public async Task Declares_one_worker_per_device_and_prepares_each_slot_the_run_uses_a_boot_then_the_pinned_app_opened_once_to_bring_the_runner_up()
    {
        string[] pool = ["iPhone 17", "iPhone 17 Pro", "iPhone Air"];
        var h = Harness.Create(options => options with { Devices = pool });
        Assert.Equal(3, h.Engine.Pool.Size);
        var lines = new List<string>();
        await h.Engine.PrepareAsync(h.PrepareInfo(2, log: lines.Add), CancellationToken.None);
        Assert.Equal(["e2e-ios-0", "e2e-ios-1"], h.Sessions);
        Assert.Equal(["devices.boot", "command.prepare", "apps.open", "devices.boot", "command.prepare", "apps.open"], h.Fake.Methods());
        JsonAssert.Args("""{"platform":"ios","device":"iPhone 17"}""", h.Fake.Calls[0].Args);
        JsonAssert.Args("""{"action":"ios-runner","platform":"ios","device":"iPhone 17"}""", h.Fake.Calls[1].Args);
        JsonAssert.Args("""{"app":"Settings","platform":"ios","device":"iPhone 17"}""", h.Fake.Calls[2].Args);
        JsonAssert.Args("""{"platform":"ios","device":"iPhone 17 Pro"}""", h.Fake.Calls[3].Args);
        Assert.Equal(["booting iPhone 17 (1 of 2)", "booting iPhone 17 Pro (2 of 2)"], lines);

        // Without a pinned app there is nothing to open, so it boots and starts the runner only. So does a build
        // `appPath` nobody has installed and no bundle id, since the engine installs nothing on its own and the
        // suite's `device.installApp()` comes later. A pinned app whose build is not on yet is not opened either,
        // and the log says why.
        var bare = Harness.Create(options => options with { Device = "iPhone 16e" }, pinned: false);
        await bare.Engine.PrepareAsync(bare.PrepareInfo(1), CancellationToken.None);
        Assert.Equal(["devices.boot", "command.prepare"], bare.Fake.Methods());
        var build = Harness.Create(options => options with { Device = "iPhone 16e" }, pinned: false, app: app => app with { AppPath = "build/App.app" });
        await build.Engine.PrepareAsync(build.PrepareInfo(1), CancellationToken.None);
        Assert.Equal(["devices.boot", "command.prepare"], build.Fake.Methods());
        var pinnedBuild = Harness.Create(options => options with { Device = "iPhone 16e" }, app: app => app with { AppPath = "build/App.app" });
        var pinnedLines = new List<string>();
        await pinnedBuild.Engine.PrepareAsync(pinnedBuild.PrepareInfo(1, log: pinnedLines.Add), CancellationToken.None);
        Assert.Equal(["devices.boot", "command.prepare"], pinnedBuild.Fake.Methods());
        Assert.Matches(@"Settings awaits the suite's device.installApp\(\)", pinnedLines[1]);

        var single = Harness.Create(options => options with { Device = "iPhone 16e", Session = "qa" });
        Assert.Equal(1, single.Engine.Pool.Size);
        await single.Engine.PrepareAsync(single.PrepareInfo(1), CancellationToken.None);
        Assert.Equal(["qa-0"], single.Sessions);
        JsonAssert.Args("""{"platform":"ios","device":"iPhone 16e"}""", single.Fake.LastArgs("devices.boot"));
    }

    [Fact]
    public async Task Warms_each_device_with_a_plain_open_and_no_launch_arguments()
    {
        var h = Harness.Create(
            options => options with { Device = "iPhone 16e" },
            app: app => app with
            {
                LaunchArguments = ["-e2e", "YES"],
                Permissions = new Dictionary<DevicePermission, PermissionState> { [DevicePermission.Camera] = PermissionState.Grant },
            });
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None);
        Assert.Equal(BootRunnerOpen, h.Fake.Methods());
        JsonAssert.Args("""{"app":"Settings","platform":"ios","device":"iPhone 16e"}""", h.Fake.LastArgs("apps.open"));
        Assert.Equal("""[{"device":"iPhone 16e"}]""", result.Env[Harness.PoolVariableIn(result.Env, "IOS")]);

        // The worker's first fresh launch presets the permissions on the app by name, with no open first.
        var session = await h.Boot("ios", 0, Harness.WorkerEnv(result));
        var before = h.Fake.Calls.Count;
        await session.RestartAsync(CancellationToken.None);
        var after = h.Fake.Calls.Skip(before).ToList();
        Assert.Equal(["settings.update", "apps.open"], after.Select(call => call.Method));
        JsonAssert.Args("""{"setting":"permission","permission":"camera","state":"grant","platform":"ios","device":"iPhone 16e","app":"Settings"}""", after[0].Args);
        JsonAssert.Args("""{"app":"Settings","platform":"ios","device":"iPhone 16e","relaunch":true,"launchArgs":["-e2e","YES"]}""", after[1].Args);
    }

    [Fact]
    public async Task Logs_an_app_that_does_not_open_in_prepare_instead_of_failing_the_run_and_hands_the_slot_over_unbound()
    {
        var h = Harness.Create(options => options with { Device = "iPhone 16e" });
        h.Fake.Respond(DeviceMethods.AppsOpen, _ => throw new InvalidOperationException("runner still installing"));
        var lines = new List<string>();
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(1, log: lines.Add), CancellationToken.None);
        Assert.Matches(@"Settings did not open.*runner still installing", lines[1]);

        // Nothing put the session on the app, and the binding says so: the worker's first launch binds it itself.
        Assert.Equal("""[{"device":"iPhone 16e"}]""", result.Env[Harness.PoolVariableIn(result.Env, "IOS")]);
    }

    [Fact]
    public async Task Starts_the_iOS_runner_in_prepare_leaves_one_that_does_not_start_to_the_first_attempt_and_ends_the_run_on_a_wedged_one()
    {
        var h = Harness.Create(options => options with { Device = "iPhone 16e" });
        h.Fake.Respond(DeviceMethods.CommandPrepare, _ => throw new InvalidOperationException("runner still building"));
        var lines = new List<string>();
        await h.Engine.PrepareAsync(h.PrepareInfo(1, log: lines.Add), CancellationToken.None);
        Assert.Matches(@"runner not prepared.*runner still building", lines[1]);

        // The warm-up open would start the runner again under open's shorter budget, so the slot is left unopened.
        Assert.Equal(["devices.boot", "command.prepare"], h.Fake.Methods());

        h.Fake.Respond(DeviceMethods.CommandPrepare, _ => throw new AgentDeviceError("RUNNER_BUSY", "the runner is still finishing a command"));
        var busy = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(1, log: lines.Add), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", busy.Code);

        // Android has no XCTest runner to start.
        var android = Harness.Create(options => options with { Device = "emulator-5556", Platform = MobilePlatform.Android });
        await android.Engine.PrepareAsync(android.PrepareInfo(1, targetName: "android"), CancellationToken.None);
        Assert.DoesNotContain("command.prepare", android.Fake.Methods());
    }

    [Fact]
    public async Task Discovers_every_booted_device_of_the_platform_when_no_device_is_named_boots_as_many_as_the_run_has_slots_and_reports_the_pool()
    {
        var inventory = Inventory(
            ("android", "Pixel_9", "Pixel 9", true),
            ("ios", "2BBF3F07-AF66-4F95-82AB-BF442506FC89", "iPhone 16e", true),
            ("ios", "8A2DC8D6-7B20-44FA-ADBB-47D3EAE6E8F3", "iPhone 17", true),
            ("ios", "CAB49ABB-4A25-48B3-99C9-949153F95902", "iPhone 17 Pro Max", false));
        var h = Harness.Create();
        h.Fake.Respond(DeviceMethods.DevicesList, _ => inventory);
        Assert.Null(h.Engine.Pool.Size);
        var lines = new List<string>();
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(4, log: lines.Add), CancellationToken.None);
        Assert.Equal(2, result.Workers);
        JsonAssert.Args("""{"platform":"ios"}""", h.Fake.LastArgs("devices.list"));

        // Both booted iPhones, by UDID, under slot sessions. The Android device and the shut-down iPhone are not the pool's.
        var boots = h.Fake.Calls.Where(call => call.Method == "devices.boot").ToList();
        Assert.Equal(2, boots.Count);
        JsonAssert.Args("""{"platform":"ios","udid":"2BBF3F07-AF66-4F95-82AB-BF442506FC89"}""", boots[0].Args);
        JsonAssert.Args("""{"platform":"ios","udid":"8A2DC8D6-7B20-44FA-ADBB-47D3EAE6E8F3"}""", boots[1].Args);
        Assert.Equal(["e2e-ios-0", "e2e-ios-0", "e2e-ios-1"], h.Sessions);
        Assert.Matches(@"2 booted ios device\(s\); driving 2", lines[0]);

        // The workers read the pool from the environment prepare's result adds, never from a second inventory.
        var variable = Harness.PoolVariableIn(result.Env, "IOS");
        Assert.Equal(
            """[{"deviceId":"2BBF3F07-AF66-4F95-82AB-BF442506FC89"},{"deviceId":"8A2DC8D6-7B20-44FA-ADBB-47D3EAE6E8F3"}]""",
            result.Env[variable]);
        var worker = Harness.Create();
        await worker.Boot("ios", 1, new Dictionary<string, string?> { [variable] = result.Env[variable] });
        Assert.Equal(["devices.boot"], worker.Fake.Methods());
        JsonAssert.Args("""{"platform":"ios","udid":"8A2DC8D6-7B20-44FA-ADBB-47D3EAE6E8F3"}""", worker.Fake.LastArgs("devices.boot"));
        Assert.Equal(["e2e-ios-1"], worker.Sessions);
    }

    [Fact]
    public async Task With_fewer_slots_than_booted_devices_drives_only_that_many_with_none_booted_one_slot_boots_what_agent_device_picks()
    {
        var h = Harness.Create();
        h.Fake.Respond(DeviceMethods.DevicesList, _ => Inventory(("ios", "A", "A", true), ("ios", "B", "B", true)));
        var first = await h.Engine.PrepareAsync(h.PrepareInfo(1, targetName: "ios"), CancellationToken.None);
        Assert.Equal(1, first.Workers);
        var boots = h.Fake.Calls.Where(call => call.Method == "devices.boot").ToList();
        Assert.Single(boots);
        JsonAssert.Args("""{"platform":"ios","udid":"A"}""", boots[0].Args);
        Assert.Equal("""[{"deviceId":"A"}]""", first.Env[Harness.PoolVariableIn(first.Env, "IOS")]);

        // The same engine prepared again, for another target, discovers afresh and hands back that target's own variable.
        h.Fake.Respond(DeviceMethods.DevicesList, _ => Inventory(("ios", "C", "C", true)));
        var second = await h.Engine.PrepareAsync(h.PrepareInfo(1, targetName: "ios.a"), CancellationToken.None);
        Assert.Equal(2, Methods(h, "devices.list").Count);
        Assert.Equal("""[{"deviceId":"C"}]""", second.Env[Harness.PoolVariableIn(second.Env, "IOS_A")]);

        // Names that sanitize alike keep distinct variables.
        var third = await h.Engine.PrepareAsync(h.PrepareInfo(1, targetName: "ios-a"), CancellationToken.None);
        Assert.NotEqual(second.Env.Keys.Single(), third.Env.Keys.Single());
        Assert.Matches("^E2E_AGENT_DEVICE_POOL_IOS_A_", third.Env.Keys.Single());

        var cold = Harness.Create();
        cold.Fake.Respond(DeviceMethods.DevicesList, _ => new JsonArray());
        var coldResult = await cold.Engine.PrepareAsync(cold.PrepareInfo(1, targetName: "ios"), CancellationToken.None);
        Assert.Equal(1, coldResult.Workers);
        JsonAssert.Args("""{"platform":"ios"}""", cold.Fake.LastArgs("devices.boot"));

        // One slot bound to no device in particular: the daemon picks.
        Assert.Equal("""[{}]""", coldResult.Env[Harness.PoolVariableIn(coldResult.Env, "IOS")]);
    }

    [Fact]
    public async Task Drives_a_discovered_Android_emulator_by_its_serial_and_reads_a_configured_serial_UDID_or_name_as_agent_device_does()
    {
        var h = Harness.Create(options => options with { Platform = MobilePlatform.Android });
        h.Fake.Respond(DeviceMethods.DevicesList, _ => Inventory(("android", "emulator-5554", "test", true)));
        var lines = new List<string>();
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(1, targetName: "android", log: lines.Add), CancellationToken.None);

        // The inventory names the device. The boot selects it by the id agent-device lists it under, an adb serial.
        Assert.Matches(@"1 booted android device\(s\); driving 1: test", lines[0]);
        JsonAssert.Args("""{"platform":"android","serial":"emulator-5554"}""", h.Fake.LastArgs("devices.boot"));
        var variable = Harness.PoolVariableIn(result.Env, "ANDROID");
        Assert.Equal("""[{"deviceId":"emulator-5554"}]""", result.Env[variable]);
        var worker = Harness.Create(options => options with { Platform = MobilePlatform.Android });
        await worker.Boot("android", 0, Harness.WorkerEnv(result));
        JsonAssert.Args("""{"platform":"android","serial":"emulator-5554"}""", worker.Fake.LastArgs("devices.boot"));

        var bySerial = Harness.Create(options => options with { Device = "emulator-5556", Platform = MobilePlatform.Android });
        await bySerial.Boot("android");
        JsonAssert.Args("""{"platform":"android","serial":"emulator-5556"}""", bySerial.Fake.LastArgs("devices.boot"));
        var byName = Harness.Create(options => options with { Device = "Pixel_9", Platform = MobilePlatform.Android });
        await byName.Boot("android");
        JsonAssert.Args("""{"platform":"android","device":"Pixel_9"}""", byName.Fake.LastArgs("devices.boot"));
        var byUdid = Harness.Create(options => options with { Device = "2BBF3F07-AF66-4F95-82AB-BF442506FC89" });
        await byUdid.Boot();
        JsonAssert.Args("""{"platform":"ios","udid":"2BBF3F07-AF66-4F95-82AB-BF442506FC89"}""", byUdid.Fake.LastArgs("devices.boot"));
    }

    [Fact]
    public async Task Carries_the_configured_settle_window_on_back_and_none_when_settle_is_false_and_both_budgets_are_non_negative()
    {
        var slow = Harness.Create(options => options with { SettleMilliseconds = 600 });
        await (await slow.Boot()).BackAsync(CancellationToken.None);
        JsonAssert.Args("""{"settle":true,"settleQuietMs":600}""", slow.Fake.LastArgs("command.back"));

        var none = Harness.Create(options => options with { Settle = false });
        await (await none.Boot()).BackAsync(CancellationToken.None);
        JsonAssert.Args("{}", none.Fake.LastArgs("command.back"));

        Assert.Contains("non-negative integer", Assert.Throws<ConfigurationException>(() => Harness.Create(options => options with { SettleMilliseconds = -1 })).Message, StringComparison.Ordinal);
        Assert.Contains("non-negative integer", Assert.Throws<ConfigurationException>(() => Harness.Create(options => options with { TransitionMilliseconds = -5 })).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_an_empty_pool_at_config_time()
    {
        Assert.Matches("empty pool", Assert.Throws<ConfigurationException>(() => Harness.Create(options => options with { Devices = [] })).Message);
    }

    [Fact]
    public void Rejects_a_link_as_the_bundle_id_at_config_time_before_prepare_could_open_it_on_a_device()
    {
        foreach (var link in new[] { "file:///etc/passwd", "https://example.com/verify?token=s3cret", "myapp://orders/42" })
        {
            var error = Assert.Throws<ConfigurationException>(() => MobileEngine.ValidateApp(new MobileApp { BundleId = link }, null, "ios", MobilePlatform.Ios));
            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Contains("device.openLink", error.Message, StringComparison.Ordinal);
        }

        var secret = Assert.Throws<ConfigurationException>(() => MobileEngine.ValidateApp(new MobileApp { BundleId = "https://example.com/verify?token=s3cret" }, null, "ios", MobilePlatform.Ios));
        Assert.DoesNotContain("s3cret", secret.Message, StringComparison.Ordinal);
        MobileEngine.ValidateApp(new MobileApp { BundleId = "Notes: Pro" }, null, "ios", MobilePlatform.Ios);
    }

    [Fact]
    public async Task Installs_nothing_on_its_own_device_installApp_with_no_path_installs_the_engine_build_and_pins_what_it_installed()
    {
        var h = Harness.Create(pinned: false, app: app => app with { AppPath = "./build/App.app" });
        h.Fake.Respond(DeviceMethods.AppsInstall, _ => new JsonObject { ["app"] = "./build/App.app", ["appPath"] = "/project/build/App.app", ["platform"] = "ios", ["bundleId"] = "com.example.app" });
        var session = await h.Boot();
        Assert.Equal(["devices.boot"], h.Fake.Methods());

        Assert.Equal(new InstalledApp("com.example.app", "com.example.app"), await session.InstallAppAsync());
        JsonAssert.Args("""{"platform":"ios","appPath":"/project/build/App.app"}""", h.Fake.LastArgs("apps.install"));

        // From here on the installed bundle is the pinned app: app.open() launches it, reset clears it.
        await session.RestartAsync(CancellationToken.None);
        JsonAssert.Args("""{"app":"com.example.app","platform":"ios","relaunch":true}""", h.Fake.LastArgs("apps.open"));
        await session.ClearStateAsync(CancellationToken.None);
        JsonAssert.Args("""{"setting":"clear-app-state","state":"clear","app":"com.example.app"}""", h.Fake.LastArgs("settings.update"));
        Assert.Single(Methods(h, "apps.install"));

        // A build named by path that is the engine's own counts the same. Another path pins nothing.
        await session.InstallAppAsync("build/App.app");
        JsonAssert.Args("""{"platform":"ios","appPath":"/project/build/App.app"}""", h.Fake.LastArgs("apps.install"));
        await session.InstallAppAsync("other/Other.app");
        JsonAssert.Args("""{"platform":"ios","appPath":"/project/other/Other.app"}""", h.Fake.LastArgs("apps.install"));
        Assert.Equal("com.example.app", session.PinnedApp);
    }

    [Fact]
    public async Task Pins_the_package_an_Android_install_reports_and_refuses_the_build_path_as_an_app_id()
    {
        var h = Harness.Create(options => options with { Platform = MobilePlatform.Android }, pinned: false, app: app => app with { AppPath = "./build/app.apk" });
        h.Fake.Respond(DeviceMethods.AppsInstall, _ => new JsonObject { ["app"] = "/project/build/app.apk", ["appPath"] = "/project/build/app.apk", ["platform"] = "android", ["appId"] = "dev.example.app", ["package"] = "dev.example.app" });
        var session = await h.Boot();
        Assert.Equal(new InstalledApp("dev.example.app", "dev.example.app"), await session.InstallAppAsync());
        await session.RestartAsync(CancellationToken.None);
        JsonAssert.Args("""{"app":"dev.example.app","platform":"android","relaunch":true}""", h.Fake.LastArgs("apps.open"));

        // agent-device 0.21.18 echoes the path as `app` when it cannot read the package. That path is not something `open` can launch.
        var unnamed = Harness.Create(options => options with { Platform = MobilePlatform.Android }, pinned: false, app: app => app with { AppPath = "./build/app.apk" });
        unnamed.Fake.Respond(DeviceMethods.AppsInstall, _ => new JsonObject { ["app"] = "/project/build/app.apk", ["appPath"] = "/project/build/app.apk", ["platform"] = "android" });
        var unnamedSession = await unnamed.Boot();
        var reported = await Assert.ThrowsAsync<EngineException>(() => unnamedSession.InstallAppAsync());
        Assert.Equal("ENGINE_FAILURE", reported.Code);
        Assert.Contains("reported no bundle id or package", reported.Message, StringComparison.Ordinal);
        Assert.Null(unnamedSession.PinnedApp);
        var refused = await Assert.ThrowsAsync<EngineException>(() => unnamedSession.RestartAsync(CancellationToken.None));
        Assert.Equal("UNSUPPORTED_CAPABILITY", refused.Code);
        Assert.Contains("device.installApp()", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("apps.open", unnamed.Fake.Methods());

        // An `app` passed in is what the build opens by, reported or not.
        Assert.Equal(new InstalledApp("dev.example.app"), await unnamedSession.InstallAppAsync(null, new InstallAppOptions { App = "dev.example.app" }));
        Assert.Equal("dev.example.app", unnamedSession.PinnedApp);
    }

    [Fact]
    public async Task Installs_the_engine_build_under_the_pinned_app_and_device_and_keeps_opening_the_pinned_app()
    {
        var h = Harness.Create(
            options => options with { Platform = MobilePlatform.Android, Device = "Pixel 8" },
            app: app => app with { BundleId = "com.example.app", AppPath = "/builds/app.apk" });
        h.Fake.Respond(DeviceMethods.AppsInstall, _ => new JsonObject { ["app"] = "com.example.app", ["appPath"] = "/builds/app.apk", ["platform"] = "android" });
        var session = await h.Boot();
        await session.RestartAsync(CancellationToken.None);
        Assert.Equal(["devices.boot", "apps.open"], h.Fake.Methods());
        await session.InstallAppAsync();
        JsonAssert.Args("""{"platform":"android","device":"Pixel 8","app":"com.example.app","appPath":"/builds/app.apk"}""", h.Fake.LastArgs("apps.install"));
        JsonAssert.Args("""{"app":"com.example.app","platform":"android","device":"Pixel 8","relaunch":true}""", h.Fake.LastArgs("apps.open"));
    }

    [Fact]
    public async Task Refuses_installApp_without_a_build_and_reports_a_build_that_does_not_install_as_the_install_step()
    {
        var none = Harness.Create();
        var noneSession = await none.Boot();
        var missing = await Assert.ThrowsAsync<EngineException>(() => noneSession.InstallAppAsync());
        Assert.Equal("INVALID_STATE", missing.Code);

        var h = Harness.Create(pinned: false, app: app => app with { AppPath = "./missing.app" });
        h.Fake.Respond(DeviceMethods.AppsInstall, _ => throw new InvalidOperationException("no such file: missing.app"));
        var session = await h.Boot();
        var failed = await Assert.ThrowsAsync<EngineException>(() => session.InstallAppAsync());
        Assert.Equal("ENGINE_FAILURE", failed.Code);
        Assert.Equal(["devices.boot", "apps.install"], h.Fake.Methods());
    }

    [Fact]
    public async Task Gives_up_on_a_session_close_that_outlives_the_cleanup_budget()
    {
        var h = Harness.Create();
        h.Fake.RespondAsync(DeviceMethods.SessionsClose, (_, _) => new TaskCompletionSource<JsonNode?>().Task);
        var session = await h.Boot();
        using var exhausted = new CancellationTokenSource();
        await exhausted.CancelAsync();
        await session.CloseAsync(TimeSpan.Zero, exhausted.Token);
        Assert.Contains("sessions.close", h.Fake.Methods());
    }
}
