// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.ScreenshotAssertions;

/// <summary>
/// <c>ToHaveScreenshotAsync</c> through a session over a fake engine, so what is tested is the engine-neutral
/// path: pixels from the engine, scaled to the viewport, cut to a locator's box, masked, compared against the
/// PNG stored beside the test file, and rewritten under update. Ported from upstream's
/// <c>screenshot-assertions.test.ts</c>. Not ported: "keeps no screenshot when a secure field on screen went
/// unmasked" (the engine masks secure fields itself, so no proof of masking is read back), "reports the mismatch
/// it saw when a later capture comes back without pixels" (an engine returns a PNG or fails), "refuses a screen
/// scoped to a frame" (the port has no <c>browser.frameLocator</c>), and the unknown-option case of the
/// invalid-argument table (the type forbids it). The first test is renamed because the port has no
/// <c>comparable</c> observe option and no step to attach images to; the secret test covers the fill case only.
/// </summary>
public sealed class ToHaveScreenshotTests
{
    private static readonly string Suffix = ScreenProject.Suffix;

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    private static ScreenshotOptions Quick => new() { Timeout = OneSecond };

    private static RgbaImage ReadPng(string file) => ImageOps.DecodePng(File.ReadAllBytes(file));

    private static (byte R, byte G, byte B) PixelAt(RgbaImage image, int x, int y)
    {
        var at = ((y * image.Width) + x) * 4;
        return (image.Data[at], image.Data[at + 1], image.Data[at + 2]);
    }

    private static Task Home(Screen screen) => Expect.That(screen).ToHaveScreenshotAsync("home.png", Quick);

    private static int ButtonPixels => (int)(ScreenEngine.Button.Width * ScreenEngine.Button.Height);

    [Fact]
    public async Task Writes_a_missing_screenshot_and_fails_passes_against_it_next_run_fails_with_the_stored_actual_and_diff_images_once_the_screen_changes_and_rewrites_it_under_update_with_the_images_on_disk_in_place_of_a_step()
    {
        var engine = new ScreenEngine();
        using var project = new ScreenProject(engine);
        var stored = Path.Combine(project.Snapshots("home"), "home" + Suffix + ".png");

        var first = await project.RunAsync("home", "home", Home);
        Assert.Equal("ASSERTION_FAILED", first?.Code);
        Assert.Contains("no stored screenshot at tests/home.e2e.ts-snapshots/home" + Suffix + ".png", first!.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(stored));
        Assert.True(engine.Captures > 0);

        Assert.Null(await project.RunAsync("home", "home", Home));

        engine.Current = () => ScreenEngine.Shot(ScreenEngine.White, ScreenEngine.Red);
        var third = await project.RunAsync("home", "home", Home);
        Assert.Equal("ASSERTION_FAILED", third?.Code);
        Assert.Contains(ButtonPixels + " pixels", third!.Message, StringComparison.Ordinal);
        Assert.Contains("differ from tests/home.e2e.ts-snapshots/home" + Suffix + ".png", third.Message, StringComparison.Ordinal);
        var results = project.ResultsOf("home");
        foreach (var label in new[] { "diff", "actual", "expected" })
        {
            var file = Path.Combine(results, "screenshots", "home-" + label + ".png");
            Assert.True(File.Exists(file));
            Assert.Contains("\n" + label + ": " + Path.GetRelativePath(project.Directory, file).Replace(Path.DirectorySeparatorChar, '/'), third.Message, StringComparison.Ordinal);
        }

        Assert.Null(await project.RunAsync("home", "home", Home, update: true));
        Assert.Equal(ScreenEngine.Red, PixelAt(ReadPng(stored), (int)ScreenEngine.Button.X, (int)ScreenEngine.Button.Y));
        Assert.Null(await project.RunAsync("home", "home", Home));
    }

    [Fact]
    public async Task Keeps_screenshots_at_one_image_pixel_per_viewport_pixel_cuts_a_locator_to_its_box_paints_masks_and_names_an_unnamed_call_after_the_test()
    {
        var engine = new ScreenEngine { Current = () => ScreenEngine.Shot(ScreenEngine.White, ScreenEngine.Blue, 2) };
        using var project = new ScreenProject(engine);

        async Task Body(Screen screen)
        {
            await Expect.That(screen).ToHaveScreenshotAsync("full", new ScreenshotOptions { Mask = [screen.GetByTestId("clock")], MaskColor = "#00ff00" });
            await Expect.That(screen.GetByRole("button")).ToHaveScreenshotAsync();
            await Expect.That(screen.GetByRole("button")).ToHaveScreenshotAsync();
        }

        var directory = project.Snapshots("crop");
        await project.RunAsync("crop", "crops and masks", Body);
        var full = ReadPng(Path.Combine(directory, "full" + Suffix + ".png"));
        Assert.Equal((ScreenEngine.Width, ScreenEngine.Height), (full.Width, full.Height));
        Assert.Equal((0, 255, 0), PixelAt(full, 310, 15));
        // The first run stops at the first missing screenshot; two more write the button's two.
        await project.RunAsync("crop", "crops and masks", Body);
        await project.RunAsync("crop", "crops and masks", Body);
        foreach (var name in new[] { "crops-and-masks-1" + Suffix + ".png", "crops-and-masks-2" + Suffix + ".png" })
        {
            var button = ReadPng(Path.Combine(directory, name));
            Assert.Equal(((int)ScreenEngine.Button.Width, (int)ScreenEngine.Button.Height), (button.Width, button.Height));
            Assert.Equal(ScreenEngine.Blue, PixelAt(button, 0, 0));
        }

        Assert.Null(await project.RunAsync("crop", "crops and masks", Body));
    }

    [Fact]
    public async Task Tolerates_the_differences_its_options_allow_and_not_passes_on_a_screen_that_differs()
    {
        var engine = new ScreenEngine();
        using var project = new ScreenProject(engine);
        static async Task Body(Screen screen)
        {
            await Expect.That(screen).ToHaveScreenshotAsync("home", new ScreenshotOptions { MaxDiffPixels = 800, Timeout = OneSecond });
            await Expect.That(screen).ToHaveScreenshotAsync("home", new ScreenshotOptions { MaxDiffPixelRatio = 0.001, Timeout = OneSecond });
            await Expect.That(screen).Not.ToHaveScreenshotAsync("home", Quick);
        }

        await project.RunAsync("tolerant", "tolerant", Body);
        engine.Current = () => ScreenEngine.Shot(ScreenEngine.White, ScreenEngine.Red);
        Assert.Null(await project.RunAsync("tolerant", "tolerant", Body));
    }

    [Fact]
    public async Task Fails_on_a_screen_that_never_holds_still_with_the_last_two_screenshots_and_their_diff_attached()
    {
        var frame = 0;
        var engine = new ScreenEngine { Current = () => ScreenEngine.Shot(ScreenEngine.White, ++frame % 2 == 0 ? ScreenEngine.Blue : ScreenEngine.Red) };
        using var project = new ScreenProject(engine);
        var error = await project.RunAsync("home", "home", Home);
        Assert.Equal("ASSERTION_FAILED", error?.Code);
        Assert.Contains("never held still", error!.Message, StringComparison.Ordinal);
        foreach (var name in new[] { "home-previous.png", "home-actual.png", "home-diff.png" })
        {
            Assert.True(File.Exists(Path.Combine(project.ResultsOf("home"), "screenshots", name)));
        }

        Assert.False(Directory.Exists(project.Snapshots("home")));
    }

    [Fact]
    public async Task Keeps_no_screenshot_once_a_secret_was_filled()
    {
        using var project = new ScreenProject(new ScreenEngine());
        var error = await project.RunAsync("fill", "after a fill", async screen =>
        {
            await screen.GetByRole("button").FillAsync(Secret.Create("token", "a-very-secret-token"));
            await Expect.That(screen).ToHaveScreenshotAsync("home");
        });
        Assert.Equal("POLICY_DENIED", error?.Code);
        Assert.False(Directory.Exists(project.Snapshots("fill")));
        Assert.Equal(0, project.Engine.Captures);
    }

    [Fact]
    public async Task Writes_a_missing_screenshot_and_passes_under_update()
    {
        using var project = new ScreenProject(new ScreenEngine());
        Assert.Null(await project.RunAsync("home", "home", Home, update: true));
        Assert.True(File.Exists(Path.Combine(project.Snapshots("home"), "home" + Suffix + ".png")));
    }

    [Fact]
    public async Task Never_passes_a_retry_against_the_screenshot_its_first_attempt_wrote_and_in_CI_writes_none_into_the_project_attaching_it_under_the_path_it_belongs_at()
    {
        using var local = new ScreenProject(new ScreenEngine());
        var first = await local.RunAsync("home", "home", Home);
        var retry = await local.RunAsync("home", "home", Home, attempt: 2, newRun: false);
        Assert.Contains("wrote this run's there", first!.Message, StringComparison.Ordinal);
        Assert.Contains("written earlier in this run", retry!.Message, StringComparison.Ordinal);

        using var ci = new ScreenProject(new ScreenEngine());
        await ci.RunAsync("home", "home", Home, ci: true);
        var second = await ci.RunAsync("home", "home", Home, ci: true, attempt: 2, newRun: false);
        Assert.False(Directory.Exists(ci.Snapshots("home")));
        Assert.Contains("CI writes none", second!.Message, StringComparison.Ordinal);
        var kept = Path.Combine(ci.ResultsOf("home", 2), "snapshots", "tests", "home.e2e.ts-snapshots", "home" + Suffix + ".png");
        Assert.Contains("\nactual: " + Path.GetRelativePath(ci.Directory, kept).Replace(Path.DirectorySeparatorChar, '/'), second.Message, StringComparison.Ordinal);
        Assert.Equal(ScreenEngine.Width, ReadPng(kept).Width);
    }

    [Fact]
    public async Task Paints_a_mask_at_the_right_place_inside_a_locators_box()
    {
        using var project = new ScreenProject(new ScreenEngine());
        await project.RunAsync("mask", "masked button", screen =>
            Expect.That(screen.GetByRole("button")).ToHaveScreenshotAsync("button", new ScreenshotOptions { Mask = [screen.GetByRole("button")], MaskColor = "#00ff00" }));
        var button = ReadPng(Path.Combine(project.Snapshots("mask"), "button" + Suffix + ".png"));
        Assert.Equal(((int)ScreenEngine.Button.Width, (int)ScreenEngine.Button.Height), (button.Width, button.Height));
        Assert.Equal((0, 255, 0), PixelAt(button, 0, 0));
    }

    [Fact]
    public async Task Fails_on_a_screen_of_another_size_on_a_not_that_still_matches_and_with_one_screenshot_before_the_timeout_honors_threshold()
    {
        var engine = new ScreenEngine();
        using var project = new ScreenProject(engine);
        var half = new ScreenshotOptions { Timeout = TimeSpan.FromMilliseconds(500) };
        Task Size(Screen screen) => Expect.That(screen).ToHaveScreenshotAsync("small", half);
        Task Negated(Screen screen) => Expect.That(screen).Not.ToHaveScreenshotAsync("home", half);
        async Task Threshold(Screen screen)
        {
            await Expect.That(screen).ToHaveScreenshotAsync("home", half);
            await Expect.That(screen).ToHaveScreenshotAsync("home", new ScreenshotOptions { Threshold = 0, Timeout = TimeSpan.FromMilliseconds(500) });
        }

        // The first run writes every screenshot; the small one is then replaced by one of another size.
        await project.RunAsync("edges", "size", Size);
        await project.RunAsync("edges", "negated", Negated);
        await project.RunAsync("edges", "threshold", Threshold);
        var small = new RgbaImage(10, 10, Enumerable.Repeat((byte)255, 400).ToArray());
        File.WriteAllBytes(Path.Combine(project.Snapshots("edges"), "small" + Suffix + ".png"), ImageOps.EncodePng(small));

        engine.Current = () => ScreenEngine.Shot(ScreenEngine.White, (0, 0, 250));
        var size = await project.RunAsync("edges", "size", Size);
        Assert.Contains("a " + ScreenEngine.Width + "x" + ScreenEngine.Height + " screenshot where tests/edges.e2e.ts-snapshots/small" + Suffix + ".png is 10x10", size!.Message, StringComparison.Ordinal);
        var negated = await project.RunAsync("edges", "negated", Negated, newRun: false);
        Assert.Contains("the screen still matches", negated!.Message, StringComparison.Ordinal);
        // The default threshold lets the first call pass; the second, exact, finds every pixel of the button.
        var threshold = await project.RunAsync("edges", "threshold", Threshold, newRun: false);
        Assert.Contains(ButtonPixels + " pixels", threshold!.Message, StringComparison.Ordinal);

        // One capture outlasts the whole 1 s budget.
        engine.BeforeCapture = () => Task.Delay(1100);
        using var late = new ScreenProject(engine);
        var timedOut = await late.RunAsync("late", "home", Home);
        Assert.Contains("took 1 screenshot before the timeout", timedOut!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_the_mismatch_it_saw_when_a_later_capture_runs_past_the_deadline_and_times_out()
    {
        var engine = new ScreenEngine();
        using var project = new ScreenProject(engine);
        await project.RunAsync("home", "home", Home);
        var captures = 0;
        engine.Current = () => ScreenEngine.Shot(ScreenEngine.White, ScreenEngine.Red);
        engine.BeforeCapture = async () =>
        {
            if (++captures != 2)
            {
                return;
            }

            // The second capture starts inside the 1 s budget and ends past it, as a slow engine's does.
            await Task.Delay(1200);
            throw new EngineException(EngineErrorCodes.OperationTimeout, "the screenshot outlived its budget");
        };
        var error = await project.RunAsync("home", "home", Home);
        Assert.Equal("ASSERTION_FAILED", error?.Code);
        Assert.Contains(ButtonPixels + " pixels", error!.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(project.ResultsOf("home"), "screenshots", "home-diff.png")));
    }

    [Fact]
    public async Task Passes_no_repeat_against_a_screenshot_another_worker_wrote_in_the_same_run_and_names_an_unnamed_one_after_a_long_title_within_a_file_names_bytes()
    {
        using var project = new ScreenProject(new ScreenEngine());
        var title = string.Concat(Enumerable.Repeat("スクリーンショット", 10));
        static Task Unnamed(Screen screen) => Expect.That(screen).ToHaveScreenshotAsync(Quick);
        var first = await project.RunAsync("repeat", title, Unnamed);
        var second = await project.RunAsync("repeat", title, Unnamed, newRun: false);
        Assert.NotNull(first);
        Assert.NotNull(second);
        var file = Path.GetFileName(Directory.GetFiles(project.Snapshots("repeat")).Single());
        Assert.Matches("^[スクリーンショット]+-[0-9a-f]{8}-1-fake-", file);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(file) <= 150);
    }

    [Theory]
    [InlineData("../home", null, null, "name must be a file name")]
    [InlineData("home", 2.0, null, "threshold must be a number from 0 to 1")]
    [InlineData("home", null, "red", "maskColor must be a color written #rrggbb")]
    [InlineData("home", null, null, "mask must be an array of locators", true)]
    [InlineData("ああああああああああああああああああああああああああああああああああああああああああああああああああああああああああああ", null, null, "at most 150 bytes")]
    public async Task Refuses_an_invalid_call_before_it_reads_the_screen(string name, double? threshold, string? maskColor, string message, bool nullMask = false)
    {
        var engine = new ScreenEngine();
        using var project = new ScreenProject(engine);
        var error = await project.RunAsync("invalid", "invalid", screen =>
            Expect.That(screen).ToHaveScreenshotAsync(name, new ScreenshotOptions { Threshold = threshold, MaskColor = maskColor, Mask = nullMask ? [null!] : null }));
        Assert.Equal("INVALID_ARGUMENT", error?.Code);
        Assert.Contains(message, error!.Message, StringComparison.Ordinal);
        Assert.Equal(0, engine.Captures);
    }

    [Fact]
    public async Task Refuses_a_not_with_nothing_stored_to_differ_from()
    {
        using var project = new ScreenProject(new ScreenEngine());
        var error = await project.RunAsync("negated", "negated", screen => Expect.That(screen).Not.ToHaveScreenshotAsync("home"));
        Assert.Equal("ASSERTION_FAILED", error?.Code);
        Assert.Contains("no stored screenshot", error!.Message, StringComparison.Ordinal);
    }
}
