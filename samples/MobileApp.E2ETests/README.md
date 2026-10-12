# Mobile app sample

A small .NET MAUI app ([`samples/MobileApp`](../MobileApp)) and the tests that drive it on a device with `MobileEngine`. It is the .NET counterpart of upstream's `examples/with-swiftui`: a name field with a Greet button, plus a counter.

The tests cover both ways to drive the app:

- `GreetingTests` finds controls by test id (the MAUI `AutomationId`) and taps and fills them. Three tests use no model. Two let the agent do the same steps, and check the result with a locator.
- `DeviceTests` uses the `Device` fixture: send the app home and bring it back, relaunch it, switch to dark mode, and read the foreground app.

All tests are in the `RealModel` and `Device` categories. They need a device, so `dotnet test E2E.slnx --filter "TestCategory!=RealModel"` skips them, and CI does too. To run the other real-model tests without these, use `--filter "TestCategory=RealModel&TestCategory!=Device"`.

## Prerequisites

- macOS with Xcode and an iOS simulator runtime, or an Android emulator (set `E2E_MOBILE_PLATFORM=android`).
- Node 22.12 or newer. `MobileEngine` starts [agent-device](https://github.com/callstack/agent-device) through `npx`, and agent-device drives the device. Check the setup once with `npx --yes agent-device@0.21.22 doctor`.
- The MAUI workload: `dotnet workload install maui`.
- A model login for the agent tests: `dotnet run --project src/E2E.Cli -- login github-copilot`, or an API key in `e2e.config.json`.

## Build the app

```bash
dotnet build samples/MobileApp -f net10.0-ios -r iossimulator-arm64
```

This writes `samples/MobileApp/bin/Debug/net10.0-ios/iossimulator-arm64/MobileApp.app`, the path the tests install by default. The Android path was not run. To try it, build `-f net10.0-android -t:SignAndroidPackage`, boot an emulator, and run the tests with `E2E_MOBILE_PLATFORM=android`. Set `E2E_MOBILE_APP_PATH` to the `.apk` if it is not at the default path.

The app is not in `E2E.slnx`. A machine without the MAUI workload could not build it, and CI (Linux) has none. The test project is in `E2E.slnx`, because it is plain .NET and builds anywhere.

## Run the tests

Boot a simulator, then run the tests:

```bash
xcrun simctl boot "iPhone 17"
E2E_MOBILE_DEVICE="iPhone 17" dotnet test samples/MobileApp.E2ETests
```

Without `E2E_MOBILE_DEVICE`, the engine drives the booted device of the platform. The first test installs the build, and every test launches the app fresh. When you are done, run `xcrun simctl shutdown all`. The agent-device daemon keeps running after the tests, in `~/.agent-device/e2e-dotnet`. Stop it with its `daemon.json` pid if you want it gone.

| Variable | Meaning |
| --- | --- |
| `E2E_MOBILE_PLATFORM` | `ios` (default) or `android`. |
| `E2E_MOBILE_DEVICE` | Simulator or emulator name or UDID. |
| `E2E_MOBILE_APP_PATH` | A build to install in place of the default. |

## How the fixture works

`e2e.config.json` has no `targets`: the config only supports the web platform. `MobileAppTest` overrides `CreateEngine()` to return a `MobileEngine` with `MobileOptions` (platform, device, and the app's bundle id and build path). The settings that are not about the device, such as the agent, the model, and the cache, still come from the config.

A device target has no URL, so a test does not call `App.OpenAsync()`. The base class launches the app with `Device.OpenAppAsync(AppId, new OpenAppOptions { Relaunch = true })`. A test reaches the `Device` fixture through the engine's active session.

Agent steps take longer on a device than in a browser, so the base class raises `StepTimeout` to 90 seconds. The default of 30 seconds timed out a two-field step.
