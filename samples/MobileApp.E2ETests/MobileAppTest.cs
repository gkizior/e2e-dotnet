// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Mobile;
using E2E.NUnit;

namespace MobileApp.E2ETests;

/// <summary>
/// Installs the MAUI app from <c>samples/MobileApp</c> on a device and launches it fresh before each test.
/// The tests need a booted iOS simulator (or an Android emulator), Node 22.12 or newer for agent-device, and a
/// build of the app. The agent tests also need a model login. See the README.
/// </summary>
[Category("RealModel")]
[Category("Device")]
public abstract class MobileAppTest : E2ETest
{
    /// <summary>The bundle id on iOS and the package on Android: <c>ApplicationId</c> in <c>MobileApp.csproj</c>.</summary>
    protected const string AppId = "dev.hardkoded.e2e.mobileapp";

    // The device keeps the build between tests, so only the first test of the run installs it.
    private static readonly SemaphoreSlim InstallLock = new(1, 1);
    private static bool s_installed;

    private MobileEngine? _engine;

    private static MobilePlatform DevicePlatform =>
        string.Equals(Environment.GetEnvironmentVariable("E2E_MOBILE_PLATFORM"), "android", StringComparison.OrdinalIgnoreCase)
            ? MobilePlatform.Android
            : MobilePlatform.Ios;

    /// <summary>The device fixture of the test's session: appearance, home, foreground app, and the other device controls.</summary>
    protected Device Device => (_engine ?? throw new InvalidOperationException("The engine starts with the test session.")).ActiveSessions.Single().Device;

    /// <summary>
    /// A <see cref="MobileEngine"/> for the pinned app. <c>E2E_MOBILE_DEVICE</c> names a simulator or emulator.
    /// Without it, the engine drives the booted device of the platform. <c>E2E_MOBILE_APP_PATH</c> names another build.
    /// </summary>
    protected override IEngine CreateEngine() => _engine = new MobileEngine(new MobileOptions
    {
        Platform = DevicePlatform,
        Device = Environment.GetEnvironmentVariable("E2E_MOBILE_DEVICE") is { Length: > 0 } device ? device : null,
        TargetName = "mobileapp",
        App = new E2E.Mobile.MobileApp { BundleId = AppId, AppPath = BuildPath() },
    });

    /// <summary>The agent can open another app, swipe from point to point, and answer a system alert.</summary>
    protected override IReadOnlyDictionary<string, AgentTool>? AgentTools => MobileTools.Create(_engine ?? throw new InvalidOperationException("The engine starts with the test session."));

    /// <summary>A device is slower than a browser: each action waits for the screen to settle, and the model sees a new snapshot after it.</summary>
    protected override TimeSpan StepTimeout => TimeSpan.FromSeconds(90);

    [SetUp]
    public async Task InstallAndOpenAppAsync()
    {
        await InstallLock.WaitAsync();
        try
        {
            if (!s_installed)
            {
                await Device.InstallAppAsync();
                s_installed = true;
            }
        }
        finally
        {
            InstallLock.Release();
        }

        // The core App.OpenAsync takes a URL. A device target launches the app through the device fixture.
        await Device.OpenAppAsync(AppId, new OpenAppOptions { Relaunch = true });
    }

    private static string BuildPath()
    {
        if (Environment.GetEnvironmentVariable("E2E_MOBILE_APP_PATH") is { Length: > 0 } path)
        {
            return path;
        }

        // Where `dotnet build samples/MobileApp -f <framework>` puts the build, relative to this project.
        return DevicePlatform == MobilePlatform.Android
            ? "../MobileApp/bin/Debug/net10.0-android/dev.hardkoded.e2e.mobileapp-Signed.apk"
            : "../MobileApp/bin/Debug/net10.0-ios/iossimulator-arm64/MobileApp.app";
    }
}
