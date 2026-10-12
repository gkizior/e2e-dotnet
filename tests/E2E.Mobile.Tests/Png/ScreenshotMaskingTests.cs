// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Mobile.Tests.PngCodec;

namespace E2E.Mobile.Tests.Png;

public sealed class ScreenshotMaskingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Masks_and_clips_RGBA_screenshots_encoded_with_filter(int filter)
    {
        var masked = Decode(ScreenshotMasking.MaskPng(Encode(Solid(10, 10, 4, 255), filter), [new Rect(2, 2, 3, 2), new Rect(8, 8, 50, 50)]));
        byte[] At(int x, int y) => masked.Pixels[(((y * 10) + x) * 4)..(((y * 10) + x) * 4 + 4)];

        Assert.Equal([0, 0, 0, 255], At(2, 2));
        Assert.Equal([0, 0, 0, 255], At(4, 3));
        Assert.Equal([255, 255, 255, 200], At(5, 3));
        Assert.Equal([255, 255, 255, 200], At(1, 2));
        Assert.Equal([0, 0, 0, 255], At(9, 9));
        Assert.Equal([255, 255, 255, 200], At(7, 7));
    }

    [Fact]
    public void Returns_the_bytes_untouched_when_there_is_nothing_to_mask_and_refuses_other_formats()
    {
        var bytes = Encode(Solid(2, 2, 3, 9));
        Assert.Same(bytes, ScreenshotMasking.MaskPng(bytes, []));
        Rect[] rects = [new Rect(0, 0, 1, 1)];
        Assert.ThrowsAny<Exception>(() => ScreenshotMasking.MaskPng([1, 2, 3], rects));
        Assert.ThrowsAny<Exception>(() => ScreenshotMasking.MaskPng(bytes[..^10], rects));
        var corrupt = (byte[])bytes.Clone();
        corrupt[20] = 255;
        Assert.ThrowsAny<Exception>(() => ScreenshotMasking.MaskPng(corrupt, rects));
        var masked = Decode(ScreenshotMasking.MaskPng(bytes, rects));
        Assert.Equal([0, 0, 0, 255], masked.Pixels[..4]);
        Assert.Equal([9, 9, 9, 255], masked.Pixels[4..8]);
    }
}
