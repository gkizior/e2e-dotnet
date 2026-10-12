// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Mobile;

/// <summary>Screenshot masking policy; PNG parsing and encoding belong to <see cref="ImageOps"/>.</summary>
public static class ScreenshotMasking
{
    /// <summary>The empty <c>IEND</c> chunk every PNG ends with: its length, its type, and its CRC.</summary>
    private static ReadOnlySpan<byte> IendChunk => [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4e, 0x44, 0xae, 0x42, 0x60, 0x82];

    /// <summary>
    /// Paints each requested rectangle opaque black, rounding outward and clipping to the image. Returns
    /// <paramref name="bytes"/> itself when there is nothing to mask. Throws <see cref="InvalidDataException"/>
    /// when the bytes are not a whole PNG.
    /// </summary>
    public static byte[] MaskPng(byte[] bytes, IReadOnlyList<Rect> rects)
    {
        if (rects.Count == 0)
        {
            return bytes;
        }

        // The decoder paints what a cut-short file holds; a screenshot with no end chunk is not a whole one.
        if (!bytes.AsSpan().EndsWith(IendChunk))
        {
            throw new InvalidDataException("the PNG is cut short: it has no end chunk");
        }

        var image = ImageOps.DecodePng(bytes);
        foreach (var rect in rects)
        {
            // Clamp in double before the cast, so a huge or non-finite rect cannot wrap around to an empty box.
            var x0 = (int)Math.Clamp(Math.Floor(rect.X), 0, image.Width);
            var y0 = (int)Math.Clamp(Math.Floor(rect.Y), 0, image.Height);
            var x1 = (int)Math.Clamp(Math.Ceiling(rect.X + rect.Width), 0, image.Width);
            var y1 = (int)Math.Clamp(Math.Ceiling(rect.Y + rect.Height), 0, image.Height);
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++)
                {
                    var at = ((y * image.Width) + x) * 4;
                    image.Data[at] = 0;
                    image.Data[at + 1] = 0;
                    image.Data[at + 2] = 0;
                    image.Data[at + 3] = 255;
                }
            }
        }

        return ImageOps.EncodePng(image);
    }
}
