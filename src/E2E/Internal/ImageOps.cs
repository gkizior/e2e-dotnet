// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace E2E.Internal;

/// <summary>An RGBA image, 8 bits per channel, rows top to bottom.</summary>
internal sealed record RgbaImage(int Width, int Height, byte[] Data);

/// <summary>A box on whole image pixels.</summary>
internal readonly record struct PixelBox(int X, int Y, int Width, int Height);

/// <summary>How far two images may differ and still match.</summary>
/// <param name="Threshold">Per-pixel color distance, 0 to 1, below which two pixels are the same.</param>
/// <param name="MaxDiffPixels">How many pixels may differ, or null.</param>
/// <param name="MaxDiffPixelRatio">What share of the pixels may differ, or null.</param>
internal sealed record ImageTolerance(double Threshold, int? MaxDiffPixels, double? MaxDiffPixelRatio);

/// <summary>What comparing two images found.</summary>
internal abstract record ImageComparison
{
    public sealed record Match(int DiffPixels) : ImageComparison;

    public sealed record SizeMismatch(PixelBox Expected, PixelBox Actual) : ImageComparison;

    public sealed record Pixels(int DiffPixels, double Ratio, RgbaImage Diff) : ImageComparison;
}

/// <summary>RGBA images in memory: PNG decoding and encoding, resampling, cropping, painting boxes, and comparing two images.</summary>
internal static class ImageOps
{
    /// <summary>Decodes PNG bytes to 8-bit RGBA: any color type, at 8 or 16 bits or with a palette. Throws on anything that is not a PNG.</summary>
    public static RgbaImage DecodePng(byte[] bytes)
    {
        using var image = PngDecoder.Instance.Decode<Rgba32>(new PngDecoderOptions(), new MemoryStream(bytes));
        var data = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(data);
        return new RgbaImage(image.Width, image.Height, data);
    }

    /// <summary>Encodes an image as PNG bytes.</summary>
    public static byte[] EncodePng(RgbaImage image)
    {
        using var pixels = Image.LoadPixelData<Rgba32>(image.Data, image.Width, image.Height);
        using var stream = new MemoryStream();
        pixels.Save(stream, new PngEncoder { ColorType = PngColorType.RgbWithAlpha, BitDepth = PngBitDepth.Bit8 });
        return stream.ToArray();
    }

    /// <summary>Resamples an image to <paramref name="width"/> by <paramref name="height"/> with a box filter; the input itself when it already has that size.</summary>
    public static RgbaImage Resample(RgbaImage image, int width, int height)
    {
        if (image.Width == width && image.Height == height)
        {
            return image;
        }

        var data = new byte[width * height * 4];
        var xRatio = (double)image.Width / width;
        var yRatio = (double)image.Height / height;
        for (var ty = 0; ty < height; ty++)
        {
            var y0 = (int)Math.Floor(ty * yRatio);
            var y1 = Math.Min(image.Height, Math.Max(y0 + 1, (int)Math.Floor((ty + 1) * yRatio)));
            for (var tx = 0; tx < width; tx++)
            {
                var x0 = (int)Math.Floor(tx * xRatio);
                var x1 = Math.Min(image.Width, Math.Max(x0 + 1, (int)Math.Floor((tx + 1) * xRatio)));
                long r = 0, g = 0, b = 0, a = 0;
                var count = 0;
                for (var y = y0; y < y1; y++)
                {
                    var offset = (y * image.Width + x0) * 4;
                    for (var x = x0; x < x1; x++)
                    {
                        r += image.Data[offset];
                        g += image.Data[offset + 1];
                        b += image.Data[offset + 2];
                        a += image.Data[offset + 3];
                        offset += 4;
                        count++;
                    }
                }

                var at = (ty * width + tx) * 4;
                data[at] = Average(r, count);
                data[at + 1] = Average(g, count);
                data[at + 2] = Average(b, count);
                data[at + 3] = Average(a, count);
            }
        }

        return new RgbaImage(width, height, data);
    }

    /// <summary>The part of <paramref name="box"/> inside the image, on whole pixels; null when none of it is.</summary>
    public static PixelBox? ClipBox(int imageWidth, int imageHeight, BoundingBox box)
    {
        var x0 = Math.Max(0, RoundHalfUp(box.X));
        var y0 = Math.Max(0, RoundHalfUp(box.Y));
        var x1 = Math.Min(imageWidth, RoundHalfUp(box.X + box.Width));
        var y1 = Math.Min(imageHeight, RoundHalfUp(box.Y + box.Height));
        return x1 > x0 && y1 > y0 ? new PixelBox(x0, y0, x1 - x0, y1 - y0) : null;
    }

    /// <summary>The pixels of a box already inside the image (see <see cref="ClipBox"/>).</summary>
    public static RgbaImage Crop(RgbaImage image, PixelBox box)
    {
        var data = new byte[box.Width * box.Height * 4];
        for (var row = 0; row < box.Height; row++)
        {
            var from = ((box.Y + row) * image.Width + box.X) * 4;
            Buffer.BlockCopy(image.Data, from, data, row * box.Width * 4, box.Width * 4);
        }

        return new RgbaImage(box.Width, box.Height, data);
    }

    /// <summary>A copy of the image with each box, clipped to it, painted opaque in <paramref name="color"/>.</summary>
    public static RgbaImage FillBoxes(RgbaImage image, IEnumerable<BoundingBox> boxes, (byte R, byte G, byte B) color)
    {
        var data = (byte[])image.Data.Clone();
        foreach (var box in boxes)
        {
            if (ClipBox(image.Width, image.Height, box) is not { } clipped)
            {
                continue;
            }

            for (var y = clipped.Y; y < clipped.Y + clipped.Height; y++)
            {
                for (var x = clipped.X; x < clipped.X + clipped.Width; x++)
                {
                    var at = (y * image.Width + x) * 4;
                    data[at] = color.R;
                    data[at + 1] = color.G;
                    data[at + 2] = color.B;
                    data[at + 3] = 255;
                }
            }
        }

        return new RgbaImage(image.Width, image.Height, data);
    }

    /// <summary>
    /// Compares <paramref name="actual"/> against <paramref name="expected"/>. They match when they have one size
    /// and at most the tolerated number of pixels differ: <c>MaxDiffPixels</c>, a <c>MaxDiffPixelRatio</c> share
    /// of the image, the smaller of the two when both are set, and none when neither is.
    /// </summary>
    public static ImageComparison Compare(RgbaImage expected, RgbaImage actual, ImageTolerance tolerance)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            return new ImageComparison.SizeMismatch(new PixelBox(0, 0, expected.Width, expected.Height), new PixelBox(0, 0, actual.Width, actual.Height));
        }

        var diff = new byte[expected.Width * expected.Height * 4];
        var diffPixels = PixelMatch.Count(expected.Data, actual.Data, diff, expected.Width, expected.Height, tolerance.Threshold);
        var total = expected.Width * expected.Height;
        double? byRatio = tolerance.MaxDiffPixelRatio is { } ratio ? total * ratio : null;
        var allowed = tolerance.MaxDiffPixels is { } pixels && byRatio is { } share
            ? Math.Min(pixels, share)
            : tolerance.MaxDiffPixels ?? byRatio ?? 0;
        return diffPixels <= allowed
            ? new ImageComparison.Match(diffPixels)
            : new ImageComparison.Pixels(diffPixels, total == 0 ? 0 : (double)diffPixels / total, new RgbaImage(expected.Width, expected.Height, diff));
    }

    private static byte Average(long sum, int count) => (byte)RoundHalfUp((double)sum / count);

    // JavaScript's Math.round: halves go up.
    private static int RoundHalfUp(double value) => (int)Math.Floor(value + 0.5);
}
