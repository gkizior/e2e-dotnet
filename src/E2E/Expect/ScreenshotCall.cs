// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using E2E.Internal;

namespace E2E;

/// <summary>The arguments of a <c>toHaveScreenshot</c> call, checked, and the name its screenshot is kept under.</summary>
internal static partial class ScreenshotAssertion
{
    private const double DefaultThreshold = 0.2;

    /// <summary>
    /// UTF-8 bytes a name may take. A file name holds 255 bytes on every common file system; the target and
    /// system suffix, an attachment's <c>-expected</c>, and a temporary suffix take the rest.
    /// </summary>
    private const int MaxNameBytes = 150;

    /// <summary>UTF-8 bytes of a test title an unnamed screenshot keeps; a longer title is cut and a hash of the whole added.</summary>
    private const int MaxTitleNameBytes = 100;

    /// <summary>One checked call.</summary>
    private sealed record ScreenshotCall(string Stem, ImageTolerance Tolerance, IReadOnlyList<Locator> Masks, (byte R, byte G, byte B) MaskColor, TimeSpan? Timeout);

    // --- arguments ---

    /// <summary>Checks a call's arguments and names its screenshot; <c>INVALID_ARGUMENT</c> before the screen is read.</summary>
    private static ScreenshotCall ParseScreenshotCall(string api, ScreenshotContext store, string? name, ScreenshotOptions? options)
    {
        if (options?.MaxDiffPixels is < 0)
        {
            throw new TestException("INVALID_ARGUMENT", api + " option maxDiffPixels must be a whole number of pixels, 0 or more");
        }

        if (options?.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new TestException("INVALID_ARGUMENT", api + " option timeout must be a positive number of milliseconds");
        }

        var stem = name is null ? UnnamedStem(store) : NamedStem(api, name);
        var tolerance = new ImageTolerance(
            Fraction(api, "threshold", options?.Threshold) ?? DefaultThreshold,
            options?.MaxDiffPixels,
            Fraction(api, "maxDiffPixelRatio", options?.MaxDiffPixelRatio));
        var masks = options?.Mask ?? [];
        if (masks.Any(mask => mask is null))
        {
            throw new TestException("INVALID_ARGUMENT", api + " option mask must be an array of locators");
        }

        var color = options?.MaskColor is { } written ? ParseColor(api, written) : (R: (byte)255, G: (byte)0, B: (byte)255);
        return new ScreenshotCall(stem, tolerance, masks, color, options?.Timeout);
    }

    private static double? Fraction(string api, string option, double? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is >= 0 and <= 1)
        {
            return value;
        }

        throw new TestException("INVALID_ARGUMENT", api + " option " + option + " must be a number from 0 to 1");
    }

    /// <summary>The stem of a name the test gave: <c>.png</c> dropped, anything that could leave the directory refused.</summary>
    private static string NamedStem(string api, string name)
    {
        var stem = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name[..^".png".Length] : name;
        if (string.IsNullOrWhiteSpace(stem) || stem is "." or ".." || UnsafeName().IsMatch(stem))
        {
            throw new TestException("INVALID_ARGUMENT", api + " name must be a file name with no path separators, got " + System.Text.Json.JsonSerializer.Serialize(name));
        }

        var bytes = Encoding.UTF8.GetByteCount(stem);
        if (bytes > MaxNameBytes)
        {
            throw new TestException("INVALID_ARGUMENT", api + " name must take at most " + Number(MaxNameBytes) + " bytes, got " + Number(bytes));
        }

        return stem;
    }

    /// <summary>
    /// A name for an unnamed call: the test's title, lowercase so titles that differ only in case never share a
    /// file on a case-insensitive disk, and a count, <c>checkout-pays-by-card-1</c>. A title cut to fit keeps a hash of the whole.
    /// </summary>
    private static string UnnamedStem(ScreenshotContext store)
    {
        var count = store.NextUnnamed();
        return Slug(store.Title, MaxTitleNameBytes, "screenshot") + "-" + Number(count);
    }

    /// <summary>A lowercase, dash-separated form of <paramref name="text"/> within <paramref name="maxBytes"/>; a cut one ends in a hash of the whole.</summary>
    internal static string Slug(string text, int maxBytes, string empty)
    {
        var title = NonWord().Replace(text.ToLowerInvariant(), "-").Trim('-');
        var cut = CutToBytes(title, maxBytes);
        var fitted = cut == title ? title : cut + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();
        return fitted.Length == 0 ? empty : fitted;
    }

    /// <summary>The longest start of <paramref name="text"/> that takes at most <paramref name="maxBytes"/> UTF-8 bytes, cut between code points.</summary>
    private static string CutToBytes(string text, int maxBytes)
    {
        var bytes = 0;
        var end = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > maxBytes)
            {
                break;
            }

            end += rune.Utf16SequenceLength;
        }

        return text[..end];
    }

    /// <summary><c>#rrggbb</c> as its three channels.</summary>
    private static (byte R, byte G, byte B) ParseColor(string api, string color)
    {
        if (!HexColor().IsMatch(color))
        {
            throw new TestException("INVALID_ARGUMENT", api + " option maskColor must be a color written #rrggbb");
        }

        return (byte.Parse(color.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(color.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture), byte.Parse(color.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>Characters a stored screenshot's name may not hold: path separators, controls, and what Windows refuses in a file name.</summary>
    [GeneratedRegex("[\\\\/<>:\"|?*\\p{Cc}]")]
    private static partial Regex UnsafeName();

    [GeneratedRegex("[^\\p{L}\\p{N}]+")]
    private static partial Regex NonWord();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
