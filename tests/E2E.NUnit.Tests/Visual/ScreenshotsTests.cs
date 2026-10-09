// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;

namespace E2E.NUnit.Tests.Visual;

/// <summary>
/// <c>ToHaveScreenshotAsync</c> against the playground's <c>/swatches</c> page, ported from upstream's
/// <c>apps/testbed/tests/visual.e2e.ts</c>. The page draws solid blocks with no text, so each stored screenshot
/// is the same file on every operating system the suite runs on. "compares a block inside a frame" is renamed
/// because it finds the block through the screen, as the port has no <c>browser.frameLocator</c>.
/// </summary>
public sealed class ScreenshotsTests : E2ETest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """{ "cache": { "mode": "off" } }""",
        AppContext.BaseDirectory,
        _ => null);

    protected override E2EConfig Config => FixtureConfig;

    protected override string? BaseUrl => E2E.Playground.Testbed.Url;

    [SetUp]
    public Task OpenSwatchesPageAsync() => App.OpenAsync("/swatches");

    [Test]
    public Task Compares_a_block_with_a_changing_part_masked()
    {
        return Expect.That(Screen.GetByTestId("palette")).ToHaveScreenshotAsync("palette.png", new ScreenshotOptions { Mask = [Screen.GetByTestId("noise")] });
    }

    [Test]
    public Task Scrolls_a_block_below_the_fold_into_view_before_comparing_it()
    {
        return Expect.That(Screen.GetByTestId("far")).ToHaveScreenshotAsync("far.png");
    }

    [Test]
    public Task Compares_a_block_inside_a_frame_found_through_the_screen()
    {
        return Expect.That(Screen.GetByTestId("framed")).ToHaveScreenshotAsync("framed.png");
    }

    [Test]
    public Task Tells_one_block_from_anothers_stored_screenshot()
    {
        return Expect.That(Screen.GetByTestId("far")).Not.ToHaveScreenshotAsync("palette.png");
    }
}
