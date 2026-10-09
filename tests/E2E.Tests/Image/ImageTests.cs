// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace E2E.Tests.Image;

/// <summary>The in-memory image operations <c>ToHaveScreenshotAsync</c> is built on: clipping, cropping, painting, the PNG round trip, and the comparison's tolerance.</summary>
public sealed class ImageTests
{
    private static readonly ImageTolerance Exact = new(0, null, null);

    /// <summary>A width by height image of one gray level.</summary>
    private static RgbaImage Gray(int width, int height, byte level)
    {
        var data = new byte[width * height * 4];
        for (var at = 0; at < data.Length; at += 4)
        {
            data[at] = level;
            data[at + 1] = level;
            data[at + 2] = level;
            data[at + 3] = 255;
        }

        return new RgbaImage(width, height, data);
    }

    private static byte[] Save<TPixel>(Image<TPixel> image, PngEncoder encoder)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using (image)
        {
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            return stream.ToArray();
        }
    }

    [Fact]
    public void Round_trips_through_PNG()
    {
        var image = Gray(3, 2, 120);
        var decoded = ImageOps.DecodePng(ImageOps.EncodePng(image));
        Assert.Equal(image.Width, decoded.Width);
        Assert.Equal(image.Height, decoded.Height);
        Assert.Equal(image.Data, decoded.Data);
    }

    [Fact]
    public void Decodes_gray_to_RGBA()
    {
        using var image = new Image<L8>(3, 2);
        byte[] samples = [0, 90, 180, 255, 30, 60];
        for (var i = 0; i < samples.Length; i++)
        {
            image[i % 3, i / 3] = new L8(samples[i]);
        }

        var decoded = ImageOps.DecodePng(Save(image, new PngEncoder { ColorType = PngColorType.Grayscale, BitDepth = PngBitDepth.Bit8 }));
        Assert.Equal(samples.SelectMany(gray => new byte[] { gray, gray, gray, 255 }), decoded.Data);
    }

    [Fact]
    public void Decodes_gray_with_alpha_to_RGBA()
    {
        using var image = new Image<La16>(3, 2);
        (byte Gray, byte Alpha)[] samples = [(0, 255), (90, 128), (180, 0), (255, 255), (30, 10), (60, 200)];
        for (var i = 0; i < samples.Length; i++)
        {
            image[i % 3, i / 3] = new La16(samples[i].Gray, samples[i].Alpha);
        }

        var decoded = ImageOps.DecodePng(Save(image, new PngEncoder { ColorType = PngColorType.GrayscaleWithAlpha, BitDepth = PngBitDepth.Bit8 }));
        Assert.Equal(samples.SelectMany(sample => new byte[] { sample.Gray, sample.Gray, sample.Gray, sample.Alpha }), decoded.Data);
    }

    [Fact]
    public void Decodes_RGB_to_RGBA()
    {
        using var image = new Image<Rgb24>(3, 2);
        byte[] samples = [255, 0, 0, 0, 255, 0, 0, 0, 255, 10, 20, 30, 40, 50, 60, 70, 80, 90];
        for (var i = 0; i < 6; i++)
        {
            image[i % 3, i / 3] = new Rgb24(samples[i * 3], samples[(i * 3) + 1], samples[(i * 3) + 2]);
        }

        var decoded = ImageOps.DecodePng(Save(image, new PngEncoder { ColorType = PngColorType.Rgb, BitDepth = PngBitDepth.Bit8 }));
        Assert.Equal(Enumerable.Range(0, 6).SelectMany(i => new[] { samples[i * 3], samples[(i * 3) + 1], samples[(i * 3) + 2], (byte)255 }), decoded.Data);
    }

    [Fact]
    public void Decodes_16_bit_RGBA_to_RGBA_scaled_with_rounding()
    {
        using var image = new Image<Rgba64>(3, 2);
        ushort[] samples = [65535, 0, 0, 65535, 0, 32768, 0, 65535, 0, 0, 65535, 0, 4096, 8192, 12288, 65535, 1, 2, 3, 4, 60000, 50000, 40000, 30000];
        for (var i = 0; i < 6; i++)
        {
            image[i % 3, i / 3] = new Rgba64(samples[i * 4], samples[(i * 4) + 1], samples[(i * 4) + 2], samples[(i * 4) + 3]);
        }

        var decoded = ImageOps.DecodePng(Save(image, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit16 }));
        Assert.Equal(samples.Select(sample => (byte)Math.Floor((sample * 255 / 65535.0) + 0.5)), decoded.Data);
    }

    [Fact]
    public void Decodes_a_palette_to_RGBA()
    {
        using var image = new Image<Rgb24>(3, 2);
        Rgb24[] palette = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255)];
        int[] indexes = [0, 1, 2, 1, 0, 2];
        for (var i = 0; i < 6; i++)
        {
            image[i % 3, i / 3] = palette[indexes[i]];
        }

        var decoded = ImageOps.DecodePng(Save(image, new PngEncoder { ColorType = PngColorType.Palette, BitDepth = PngBitDepth.Bit8 }));
        Assert.Equal(indexes.SelectMany(index => new byte[] { palette[index].R, palette[index].G, palette[index].B, 255 }), decoded.Data);
    }

    [Fact]
    public void Clips_a_box_to_the_image_on_whole_pixels_and_has_none_for_a_box_outside_it()
    {
        Assert.Equal(new PixelBox(0, 4, 3, 6), ImageOps.ClipBox(10, 10, new BoundingBox(-2.4, 3.6, 5, 20)));
        Assert.Null(ImageOps.ClipBox(10, 10, new BoundingBox(12, 0, 5, 5)));
    }

    [Fact]
    public void Crops_the_pixels_of_a_box_and_paints_boxes_clipped_to_the_image()
    {
        var blank = Gray(4, 4, 0);
        var image = ImageOps.FillBoxes(blank, [new BoundingBox(2, 2, 10, 10)], (255, 0, 0));
        Assert.Equal(Gray(4, 4, 0).Data, blank.Data);
        Assert.Equal(
            new byte[] { 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 0, 0, 255 },
            ImageOps.Crop(image, new PixelBox(1, 1, 2, 2)).Data);
    }

    [Fact]
    public void Averages_the_pixels_each_target_pixel_covers_when_it_resamples()
    {
        var image = new RgbaImage(2, 1, [0, 0, 0, 255, 200, 200, 200, 255]);
        Assert.Equal(new byte[] { 100, 100, 100, 255 }, ImageOps.Resample(image, 1, 1).Data);
        Assert.Same(image, ImageOps.Resample(image, 2, 1));
    }

    [Fact]
    public void Reports_a_size_mismatch_and_counts_differing_pixels_against_maxDiffPixels_and_maxDiffPixelRatio_the_smaller_one_when_both_are_set()
    {
        Assert.IsType<ImageComparison.SizeMismatch>(ImageOps.Compare(Gray(2, 2, 0), Gray(2, 3, 0), Exact));
        var expected = Gray(10, 10, 255);
        var actual = ImageOps.FillBoxes(Gray(10, 10, 255), [new BoundingBox(0, 0, 5, 1)], (0, 0, 0));
        var pixels = Assert.IsType<ImageComparison.Pixels>(ImageOps.Compare(expected, actual, Exact));
        Assert.Equal(5, pixels.DiffPixels);
        Assert.Equal(0.05, pixels.Ratio);
        Assert.IsType<ImageComparison.Match>(ImageOps.Compare(expected, actual, Exact with { MaxDiffPixels = 5 }));
        Assert.IsType<ImageComparison.Match>(ImageOps.Compare(expected, actual, Exact with { MaxDiffPixelRatio = 0.05 }));
        Assert.IsType<ImageComparison.Pixels>(ImageOps.Compare(expected, actual, Exact with { MaxDiffPixels = 10, MaxDiffPixelRatio = 0.04 }));
    }

    [Fact]
    public void Counts_a_pixel_as_the_same_when_its_color_is_within_the_threshold()
    {
        Assert.IsType<ImageComparison.Match>(ImageOps.Compare(Gray(4, 4, 200), Gray(4, 4, 204), Exact with { Threshold = 0.2 }));
        var pixels = Assert.IsType<ImageComparison.Pixels>(ImageOps.Compare(Gray(4, 4, 200), Gray(4, 4, 204), Exact));
        Assert.Equal(16, pixels.DiffPixels);
    }
}
