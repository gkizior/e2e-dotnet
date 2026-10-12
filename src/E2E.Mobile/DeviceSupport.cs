// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>A point on the screen, in the device's logical points.</summary>
public readonly record struct Point(double X, double Y);

/// <summary>A box on the screen, in the device's logical points.</summary>
public readonly record struct Rect(double X, double Y, double Width, double Height);

/// <summary>The logical size of the screen, in points.</summary>
public readonly record struct ViewportSize(double Width, double Height);

/// <summary>How far a scroll gesture travels inside its rect. Unset is the default.</summary>
public enum Momentum
{
    /// <summary>A quarter of the box: shorter than any row, so every row lands fully on screen at some stop.</summary>
    Slow,

    /// <summary>Four fifths of the box.</summary>
    Fast,
}

/// <summary>The screenshot fields a viewport probe reads off the device client's response.</summary>
public sealed record RawScreenshotResult
{
    public string? Path { get; init; }

    public double? Width { get; init; }

    public double? Height { get; init; }

    public double? LogicalWidth { get; init; }

    public double? LogicalHeight { get; init; }

    public double? PixelDensity { get; init; }
}

/// <summary>Shared helpers for the device engine: error constructors, filenames, PNG headers, gestures, the screen location and size.</summary>
public static partial class DeviceSupport
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

    public static EngineException Cancelled(string text) => new(EngineErrorCodes.Cancelled, text, retryable: false);

    public static EngineException InvalidState(string text) => new(EngineErrorCodes.InvalidState, text, retryable: false);

    public static EngineException NotActionable(string text) => new(EngineErrorCodes.NotActionable, text, retryable: false);

    public static EngineException Unsupported(string text) => new(EngineErrorCodes.UnsupportedCapability, text, retryable: false);

    /// <summary>Constrains a caller-supplied artifact label to a safe filename.</summary>
    public static string SanitizeFilename(string name)
    {
        var safe = UnsafeFilenameCharacters().Replace(name, "_");
        safe = safe.Length > 64 ? safe[..64] : safe;
        return safe.Length == 0 ? "artifact" : safe;
    }

    /// <summary>Reads the dimensions out of a PNG's IHDR chunk; null for anything else.</summary>
    public static (int Width, int Height)? ReadPngSize(ReadOnlySpan<byte> data)
    {
        if (data.Length < 24 || !data[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return null;
        }

        return (ReadBigEndian(data[16..]), ReadBigEndian(data[20..]));
    }

    /// <summary>
    /// The finger gesture that scrolls one rect's content in <paramref name="direction"/>. Scrolling
    /// down reveals what is below, so the finger travels up; the travel is a share of the rect's extent
    /// scaled by momentum, and never leaves the rect. The root scroll sends no share (the device
    /// client's default); these are for a gesture inside one control, and deterministic tests step
    /// through a list with <see cref="Momentum.Slow"/>, a quarter of the box, shorter than any row, so
    /// every row lands fully on screen at some stop. At three quarters the mobile benchmark's scenario
    /// list dragged rows past.
    /// </summary>
    public static (Point From, Point To) SwipeWithin(Rect rect, ScrollDirection direction, Momentum? momentum)
    {
        var ratio = momentum switch
        {
            Momentum.Fast => 0.8,
            Momentum.Slow => 0.25,
            _ => 0.5,
        };
        var centre = new Point(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        var dx = rect.Width * ratio / 2;
        var dy = rect.Height * ratio / 2;
        return direction switch
        {
            ScrollDirection.Down => (new Point(centre.X, centre.Y + dy), new Point(centre.X, centre.Y - dy)),
            ScrollDirection.Up => (new Point(centre.X, centre.Y - dy), new Point(centre.X, centre.Y + dy)),
            ScrollDirection.Right => (new Point(centre.X + dx, centre.Y), new Point(centre.X - dx, centre.Y)),
            ScrollDirection.Left => (new Point(centre.X - dx, centre.Y), new Point(centre.X + dx, centre.Y)),
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
        };
    }

    /// <summary>
    /// The location a device surface reports in a snapshot: the foreground app and the screen shown,
    /// <c>&lt;app&gt; / &lt;screen title&gt;</c>. A simulator has no address bar, so this is an opaque
    /// address the harness shows to the model and the report and anchors trace replay on; it changes
    /// exactly when the app or its screen changes. It is deliberately not a URL: no scheme, no colon,
    /// so the harness never mistakes it for one and applies an origin policy to it. Null when neither
    /// the app nor a title is known.
    /// </summary>
    public static string? ScreenLocation(string? app, string? title)
    {
        var parts = new[] { app, title }
            .Select(part => part is null ? "" : Whitespace().Replace(part, " ").Trim())
            .Where(part => part.Length > 0)
            .ToList();
        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }

    /// <summary>
    /// The device's logical screen size as its screenshot reports it, in points: the logical dimensions
    /// when given, else the pixel dimensions over the density. Null when the response carries neither.
    /// </summary>
    public static ViewportSize? LogicalScreenSize(RawScreenshotResult result)
    {
        ViewportSize? logical =
            result.LogicalWidth is { } logicalWidth && result.LogicalHeight is { } logicalHeight
                ? new ViewportSize(logicalWidth, logicalHeight)
                : result is { Width: { } width, Height: { } height, PixelDensity: > 0 and var density }
                    ? new ViewportSize(width / density, height / density)
                    : null;
        return logical is { Width: > 0, Height: > 0 } ? logical : null;
    }

    private static int ReadBigEndian(ReadOnlySpan<byte> data) =>
        (data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3];

    [GeneratedRegex("[^A-Za-z0-9._-]")]
    private static partial Regex UnsafeFilenameCharacters();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
