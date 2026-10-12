// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.IO.Compression;
using E2E.Internal;

namespace E2E.Mobile.Tests;

/// <summary>A synthetic screenshot: 8-bit RGB (3 channels) or RGBA (4 channels), rows top to bottom.</summary>
internal sealed record DecodedPng(int Width, int Height, int Channels, byte[] Pixels);

/// <summary>Builds synthetic PNGs with a chosen row filter, and reads masked ones back, for the masking tests.</summary>
internal static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(value =>
    {
        var crc = (uint)value;
        for (var bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }).ToArray();

    /// <summary>A solid image of one value; RGBA images get an alpha of 200.</summary>
    public static DecodedPng Solid(int width, int height, int channels, byte value)
    {
        var pixels = Enumerable.Repeat(value, width * height * channels).ToArray();
        if (channels == 4)
        {
            for (var at = 3; at < pixels.Length; at += 4)
            {
                pixels[at] = 200;
            }
        }

        return new DecodedPng(width, height, channels, pixels);
    }

    /// <summary>Reads assertion pixels as RGBA, independently of the masking policy.</summary>
    public static DecodedPng Decode(byte[] bytes)
    {
        var image = ImageOps.DecodePng(bytes);
        return new DecodedPng(image.Width, image.Height, 4, image.Data);
    }

    /// <summary>Encodes a synthetic screenshot in RGB or RGBA, with PNG row filter 0 (none) or 4 (Paeth).</summary>
    public static byte[] Encode(DecodedPng image, int filterType = 0)
    {
        var stride = image.Width * image.Channels;
        var raw = new byte[(stride + 1) * image.Height];
        for (var row = 0; row < image.Height; row++)
        {
            raw[row * (stride + 1)] = (byte)filterType;
            for (var column = 0; column < stride; column++)
            {
                var current = image.Pixels[(row * stride) + column];
                var left = column >= image.Channels ? image.Pixels[(row * stride) + column - image.Channels] : 0;
                var up = row > 0 ? image.Pixels[((row - 1) * stride) + column] : 0;
                var upLeft = row > 0 && column >= image.Channels ? image.Pixels[((row - 1) * stride) + column - image.Channels] : 0;
                raw[(row * (stride + 1)) + 1 + column] = (byte)(filterType == 4 ? current - Paeth(left, up, upLeft) : current);
            }
        }

        var header = new byte[13];
        WriteBigEndian(header, 0, image.Width);
        WriteBigEndian(header, 4, image.Height);
        header[8] = 8;
        header[9] = (byte)(image.Channels == 3 ? 2 : 6);

        using var output = new MemoryStream();
        output.Write(Signature);
        WriteChunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
        {
            return left;
        }

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBigEndian(length, 0, data.Length);
        output.Write(length);
        var body = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        output.Write(body);
        var crc = new byte[4];
        WriteBigEndian(crc, 0, unchecked((int)Crc32(body)));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xffffffff;
        foreach (var value in data)
        {
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        }

        return crc ^ 0xffffffff;
    }

    private static void WriteBigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
