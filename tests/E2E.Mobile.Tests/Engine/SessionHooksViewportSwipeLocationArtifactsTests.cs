// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;
using static E2E.Mobile.Tests.RawNodes;
using static E2E.Mobile.Tests.Screens;

namespace E2E.Mobile.Tests.Engine;

public sealed class SessionHooksViewportSwipeLocationArtifactsTests
{
    private static async Task<string> Location(DeviceSession session) => (await session.ObserveAsync(CancellationToken.None)).Route;

    private static JsonObject PinOnly() => FakeDeviceClient.NodesOnly([N("e1", type: "SecureTextField", label: "PIN", value: "1234")]);

    /// <summary>Answers the screenshot with a file of these bytes and remembers where the engine asked for it.</summary>
    private static void Writes(Harness h, byte[] png, List<string> files) =>
        h.Fake.Respond(DeviceMethods.CaptureScreenshot, options =>
        {
            var file = options!["path"]!.GetValue<string>();
            files.Add(file);
            File.WriteAllBytes(file, png);
            return new JsonObject { ["path"] = file };
        });

    [Fact]
    public async Task Locates_the_observation_on_the_foreground_app_and_the_screen_title_and_omits_what_it_does_not_know()
    {
        var h = Harness.Create();
        var session = await OpenAttempt(h);
        Assert.Equal("com.apple.Preferences / General", await Location(session));

        // No title bar: the app alone. The app identity is remembered from the launch when the snapshot omits it.
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.NodesOnly([N("@e1", type: "button", label: "Go")]));
        Assert.Equal("com.apple.Preferences", await Location(session));
        var other = FakeDeviceClient.SettingsSnapshot();
        other["appBundleId"] = "other.app";
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => other);
        Assert.Equal("other.app / General", await Location(session));

        // Nothing pinned, nothing opened, no title: no location, and never an invented URL.
        var cold = Harness.Create(pinned: false);
        var coldSession = await OpenAttempt(cold);
        cold.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.NodesOnly([N("@e1", type: "button", rect: new Rect(0, 0, 10, 10))]));
        Assert.Equal("", await Location(coldSession));
    }

    [Fact]
    public async Task Numbers_screenshots_per_attempt_masks_secure_fields_in_them_and_refuses_an_unmaskable_one()
    {
        var h = Harness.Create();

        // A third of the 390x844 viewport: the mask scales the Password field's bounds (y=270, 44 tall) to the image.
        var files = new List<string>();
        Writes(h, PngCodec.Encode(PngCodec.Solid(130, 282, 3, 200)), files);
        var session = await OpenAttempt(h);
        var screenshot = await session.ScreenshotAsync(CancellationToken.None);
        Assert.Equal(130d / 390, screenshot.Scale, 6);
        var written = PngCodec.Decode(screenshot.Png);
        byte[] At(int x, int y)
        {
            var start = ((y * written.Width) + x) * written.Channels;
            return written.Pixels[start..(start + 3)];
        }

        Assert.Equal([0, 0, 0], At(33, 96));
        Assert.Equal([200, 200, 200], At(33, 80));

        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => PinOnly());
        var leak = await Assert.ThrowsAsync<EngineException>(() => session.ScreenshotAsync(CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", leak.Code);
        Assert.Equal(files.Count, files.Distinct().Count());
        foreach (var file in files)
        {
            Assert.False(File.Exists(file));
            Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
        }
    }

    [Fact]
    public async Task Removes_temporary_screenshots_after_a_capture_failure()
    {
        var h = Harness.Create();
        string? file = null;
        h.Fake.Respond(DeviceMethods.CaptureScreenshot, options =>
        {
            file = options!["path"]!.GetValue<string>();
            File.WriteAllBytes(file, [1, 2, 3]);
            throw new AgentDeviceError("COMMAND_FAILED", "capture failed after writing");
        });
        var session = await OpenAttempt(h);
        var failure = await Assert.ThrowsAsync<EngineException>(() => session.ScreenshotAsync(CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.NotNull(file);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    [Fact]
    public async Task Cleans_up_a_screenshot_that_fails_after_cancellation()
    {
        var h = Harness.Create();
        var started = new TaskCompletionSource<string>();
        var finish = new TaskCompletionSource();
        h.Fake.RespondAsync(DeviceMethods.CaptureScreenshot, async (options, _) =>
        {
            var file = options!["path"]!.GetValue<string>();
            started.SetResult(file);
            await finish.Task;
            File.WriteAllBytes(file, [1, 2, 3]);
            throw new AgentDeviceError("COMMAND_FAILED", "late capture failure");
        });
        var session = await OpenAttempt(h);
        using var controller = new CancellationTokenSource();
        var pending = session.ScreenshotAsync(controller.Token);
        var file = await started.Task;
        await controller.CancelAsync();
        var cancelled = await Assert.ThrowsAsync<EngineException>(() => pending);
        Assert.Equal("CANCELLED", cancelled.Code);

        // The capture was only abandoned: it fails after the caller left, and its temporary files go with it.
        finish.SetResult();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while ((File.Exists(file) || Directory.Exists(Path.GetDirectoryName(file))) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }
}
