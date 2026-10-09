// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Internal;

/// <summary>
/// Counts the pixels two RGBA images differ in, as the pixelmatch library does with only a threshold set:
/// a perceptual (YIQ) color distance, and anti-aliased pixels left out of the count.
/// </summary>
internal static class PixelMatch
{
    /// <summary>
    /// The number of pixels that differ by more than <paramref name="threshold"/> (0 to 1) and are not anti-aliasing.
    /// <paramref name="output"/> receives a diff image: differing pixels red, anti-aliased yellow, the rest faded gray.
    /// </summary>
    public static int Count(byte[] img1, byte[] img2, byte[] output, int width, int height, double threshold)
    {
        var length = width * height;
        var a32 = Pack(img1, length);
        var b32 = Pack(img2, length);
        if (a32.AsSpan().SequenceEqual(b32))
        {
            for (var i = 0; i < length; i++)
            {
                DrawGray(img1, 4 * i, 0.1, output);
            }

            return 0;
        }

        // 35215 is the maximum possible value of the YIQ difference metric.
        var maxDelta = 35215 * threshold * threshold;
        var diff = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pos = (y * width + x) * 4;
                var delta = a32[y * width + x] == b32[y * width + x] ? 0 : ColorDelta(img1, img2, pos, pos, false);
                if (Math.Abs(delta) > maxDelta)
                {
                    if (Antialiased(img1, x, y, width, height, a32, b32) || Antialiased(img2, x, y, width, height, b32, a32))
                    {
                        Draw(output, pos, 255, 255, 0);
                    }
                    else
                    {
                        Draw(output, pos, 255, 0, 0);
                        diff++;
                    }
                }
                else
                {
                    DrawGray(img1, pos, 0.1, output);
                }
            }
        }

        return diff;
    }

    private static uint[] Pack(byte[] image, int length)
    {
        var packed = new uint[length];
        for (var i = 0; i < length; i++)
        {
            packed[i] = BitConverter.ToUInt32(image, i * 4);
        }

        return packed;
    }

    // Whether the pixel looks like anti-aliasing: it sits between darker and brighter neighbors, and one of the extremes is a flat area in both images.
    private static bool Antialiased(byte[] img, int x1, int y1, int width, int height, uint[] a32, uint[] b32)
    {
        var x0 = Math.Max(x1 - 1, 0);
        var y0 = Math.Max(y1 - 1, 0);
        var x2 = Math.Min(x1 + 1, width - 1);
        var y2 = Math.Min(y1 + 1, height - 1);
        var pos = y1 * width + x1;
        var zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        double min = 0, max = 0;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        for (var x = x0; x <= x2; x++)
        {
            for (var y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }

                var delta = ColorDelta(img, img, pos * 4, (y * width + x) * 4, true);
                if (delta == 0)
                {
                    zeroes++;
                    if (zeroes > 2)
                    {
                        return false;
                    }
                }
                else if (delta < min)
                {
                    min = delta;
                    minX = x;
                    minY = y;
                }
                else if (delta > max)
                {
                    max = delta;
                    maxX = x;
                    maxY = y;
                }
            }
        }

        if (min == 0 || max == 0)
        {
            return false;
        }

        return (HasManySiblings(a32, minX, minY, width, height) && HasManySiblings(b32, minX, minY, width, height))
            || (HasManySiblings(a32, maxX, maxY, width, height) && HasManySiblings(b32, maxX, maxY, width, height));
    }

    private static bool HasManySiblings(uint[] img, int x1, int y1, int width, int height)
    {
        var x0 = Math.Max(x1 - 1, 0);
        var y0 = Math.Max(y1 - 1, 0);
        var x2 = Math.Min(x1 + 1, width - 1);
        var y2 = Math.Min(y1 + 1, height - 1);
        var value = img[y1 * width + x1];
        var zeroes = x1 == x0 || x1 == x2 || y1 == y0 || y1 == y2 ? 1 : 0;
        for (var x = x0; x <= x2; x++)
        {
            for (var y = y0; y <= y2; y++)
            {
                if (x == x1 && y == y1)
                {
                    continue;
                }

                if (value == img[y * width + x])
                {
                    zeroes++;
                }

                if (zeroes > 2)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Squared YIQ distance between two pixels, negative when the second is brighter; the brightness difference alone when yOnly.
    private static double ColorDelta(byte[] img1, byte[] img2, int k, int m, bool yOnly)
    {
        double r1 = img1[k], g1 = img1[k + 1], b1 = img1[k + 2], a1 = img1[k + 3];
        double r2 = img2[m], g2 = img2[m + 1], b2 = img2[m + 2], a2 = img2[m + 3];
        if (a1 == a2 && r1 == r2 && g1 == g2 && b1 == b2)
        {
            return 0;
        }

        if (a1 < 255)
        {
            a1 /= 255;
            r1 = Blend(r1, a1);
            g1 = Blend(g1, a1);
            b1 = Blend(b1, a1);
        }

        if (a2 < 255)
        {
            a2 /= 255;
            r2 = Blend(r2, a2);
            g2 = Blend(g2, a2);
            b2 = Blend(b2, a2);
        }

        var y1 = Y(r1, g1, b1);
        var y2 = Y(r2, g2, b2);
        var y = y1 - y2;
        if (yOnly)
        {
            return y;
        }

        var i = I(r1, g1, b1) - I(r2, g2, b2);
        var q = Q(r1, g1, b1) - Q(r2, g2, b2);
        var delta = (0.5053 * y * y) + (0.299 * i * i) + (0.1957 * q * q);
        return y1 > y2 ? -delta : delta;
    }

    private static double Y(double r, double g, double b) => (r * 0.29889531) + (g * 0.58662247) + (b * 0.11448223);

    private static double I(double r, double g, double b) => (r * 0.59597799) - (g * 0.27417610) - (b * 0.32180189);

    private static double Q(double r, double g, double b) => (r * 0.21147017) - (g * 0.52261711) + (b * 0.31114694);

    private static double Blend(double c, double a) => 255 + ((c - 255) * a);

    private static void Draw(byte[] output, int pos, double r, double g, double b)
    {
        output[pos] = (byte)r;
        output[pos + 1] = (byte)g;
        output[pos + 2] = (byte)b;
        output[pos + 3] = 255;
    }

    private static void DrawGray(byte[] img, int i, double alpha, byte[] output)
    {
        var value = Blend(Y(img[i], img[i + 1], img[i + 2]), alpha * img[i + 3] / 255);
        Draw(output, i, value, value, value);
    }
}
