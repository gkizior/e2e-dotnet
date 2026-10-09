// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E;

/// <summary>Options of <c>ToHaveScreenshotAsync</c>.</summary>
public sealed class ScreenshotOptions
{
    /// <summary>How far one pixel's color may drift and still count as the same, from 0 (exact) to 1. Default 0.2.</summary>
    public double? Threshold { get; init; }

    /// <summary>How many pixels may differ. Default 0.</summary>
    public int? MaxDiffPixels { get; init; }

    /// <summary>What share of the pixels may differ, from 0 to 1. Default 0. With <see cref="MaxDiffPixels"/> also set, the smaller count wins.</summary>
    public double? MaxDiffPixelRatio { get; init; }

    /// <summary>Locators painted over before comparing, for content that changes from run to run.</summary>
    public IReadOnlyList<Locator>? Mask { get; init; }

    /// <summary>Color of the <see cref="Mask"/> boxes, written <c>#rrggbb</c>. Default <c>#ff00ff</c>.</summary>
    public string? MaskColor { get; init; }

    /// <summary>Assertion budget. Defaults to the assertion timeout.</summary>
    public TimeSpan? Timeout { get; init; }
}
