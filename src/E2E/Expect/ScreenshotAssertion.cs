// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// <c>toHaveScreenshot</c>: the screen, or one locator's box on it, compared against a PNG stored beside the test
/// file. The engine captures the viewport; everything else happens here.
/// </summary>
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

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly TimeSpan NegationGrace = TimeSpan.FromMilliseconds(1000);

    /// <summary>One checked call.</summary>
    private sealed record Call(string Stem, ImageTolerance Tolerance, IReadOnlyList<Locator> Masks, (byte R, byte G, byte B) MaskColor, TimeSpan? Timeout);

    /// <summary>One call in one attempt.</summary>
    private sealed class Run(Screen screen, ScreenshotStore store, string api, Call call, string file, string shown, Func<CancellationToken, Task<Capture>> capture)
    {
        public Screen Screen { get; } = screen;

        public ScreenshotStore Store { get; } = store;

        public string Api { get; } = api;

        public Call Call { get; } = call;

        public string File { get; } = file;

        public string Shown { get; } = shown;

        public Func<CancellationToken, Task<Capture>> Capture { get; } = capture;
    }

    /// <summary>One capture, or why there is none yet.</summary>
    private readonly record struct Capture(RgbaImage? Image, string? Missing);

    /// <summary>Runs one call as an assertion: of the whole screen when <paramref name="subject"/> is null, else of the one node it matches.</summary>
    public static async Task RunAsync(Screen screen, Locator? subject, bool negated, string? name, ScreenshotOptions? options, string? callerFile, CancellationToken cancellationToken)
    {
        var api = (negated ? "not." : "") + "toHaveScreenshot";
        var store = screen.Screenshots
            ?? throw new TestException("UNSUPPORTED_CAPABILITY", api + " keeps screenshots beside a test file, so it runs only inside a test");
        var call = Parse(api, store, name, options);
        var directory = store.DirectoryOf(callerFile)
            ?? throw new TestException("INVALID_ARGUMENT", api + " could not tell which test file it was called from");
        var file = Path.Combine(directory, call.Stem + store.Suffix + ".png");
        var shown = Shown(store, file);
        var token = screen.Token(cancellationToken);
        var start = screen.Clock.GetUtcNow();
        var deadline = start + (call.Timeout ?? screen.AssertionTimeout);

        if (store.WithholdsPixels())
        {
            throw new TestException("POLICY_DENIED", "expect(" + Subject(subject) + ")." + api + " is denied after a secret fill because the app may display the secret outside a secure field");
        }

        var capturer = new Capturer(screen, store, subject, call, deadline);
        var run = new Run(screen, store, api, call, file, shown, capturer.CaptureAsync);
        var expected = System.IO.File.Exists(file) ? ReadStored(run) : null;
        if (expected is not null && (negated || !store.Update) && store.HasWritten(file))
        {
            throw Failure(run, subject, shown + " was written earlier in this run and is not reviewed yet: look at it, commit it, and run again");
        }

        if (negated)
        {
            await ExpectDifferentAsync(run, subject, expected, deadline, token).ConfigureAwait(false);
        }
        else if (await MatchOrSettleAsync(run, subject, expected, deadline, token).ConfigureAwait(false) is { } settled)
        {
            await KeepAsync(run, subject, expected, settled).ConfigureAwait(false);
        }

        screen.NotifyVerified();
    }

    private static string Subject(Locator? subject) => subject is null ? "screen" : subject.Query.Describe();

    private static string Shown(ScreenshotStore store, string file)
    {
        var relative = Path.GetRelativePath(store.ProjectRoot, file);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? file : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static RgbaImage ReadStored(Run run)
    {
        try
        {
            return ImageOps.DecodePng(System.IO.File.ReadAllBytes(run.File));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TestException("INVALID_ARGUMENT", run.Api + " could not read the stored screenshot " + run.Shown + " as a PNG; delete it or run with E2E_UPDATE_SNAPSHOTS=1", ex);
        }
    }

    // --- polling ---

    /// <summary>
    /// Captures until the screen matches the stored screenshot, or, with none stored or update on, until it holds
    /// still: two captures in a row the same within the call's tolerance. Returns the screen that held still, to
    /// keep; null when it matched.
    /// </summary>
    private static async Task<RgbaImage?> MatchOrSettleAsync(Run run, Locator? subject, RgbaImage? expected, DateTimeOffset deadline, CancellationToken token)
    {
        var settles = expected is null || run.Store.Update;
        // The newest capture with pixels, and why the newest without had none: a read the deadline cut short can
        // come back empty, and must not hide what the screen showed before it.
        RgbaImage? latest = null;
        ImageComparison? latestComparison = null;
        string? missing = null;
        RgbaImage? previous = null;
        (RgbaImage, RgbaImage)? unsettled = null;
        RgbaImage? settled = null;
        var captures = 0;
        await PollAsync(
            run,
            deadline,
            token,
            async () =>
            {
                var capture = await run.Capture(token).ConfigureAwait(false);
                if (capture.Image is null)
                {
                    missing = capture.Missing;
                    return null;
                }

                captures++;
                var current = capture.Image;
                var comparison = expected is null ? null : ImageOps.Compare(expected, current, run.Call.Tolerance);
                latest = current;
                latestComparison = comparison;
                if (comparison is ImageComparison.Match)
                {
                    return true;
                }

                if (!settles)
                {
                    return false;
                }

                var before = previous;
                previous = current;
                if (before is null)
                {
                    return false;
                }

                if (ImageOps.Compare(before, current, run.Call.Tolerance) is ImageComparison.Match)
                {
                    settled = current;
                    return true;
                }

                unsettled = (before, current);
                return false;
            },
            cause =>
            {
                if (latest is null)
                {
                    return Failure(run, subject, missing ?? "no screenshot was taken", cause);
                }

                if (!settles && expected is not null && latestComparison is { } comparison and not ImageComparison.Match)
                {
                    return Mismatch(run, subject, expected, latest, comparison, cause);
                }

                var images = new List<string>();
                if (unsettled is var (first, second))
                {
                    if (ImageOps.Compare(first, second, new ImageTolerance(0, null, null)) is ImageComparison.Pixels pixels)
                    {
                        images.Add("diff: " + Attach(run, run.Call.Stem + "-diff", pixels.Diff));
                    }

                    images.Add("previous: " + Attach(run, run.Call.Stem + "-previous", first));
                    images.Add("actual: " + Attach(run, run.Call.Stem + "-actual", second));
                }

                var message = captures < 2
                    ? "took " + Number(captures) + " screenshot" + (captures == 1 ? "" : "s") + " before the timeout and needs two the same; give it a longer timeout"
                    : "the screen never held still: no two of " + Number(captures) + " screenshots in a row were the same";
                return Failure(run, subject, string.Join('\n', new[] { message }.Concat(images)), cause);
            },
            negated: false).ConfigureAwait(false);
        return settled;
    }

    /// <summary><c>.Not</c>: waits for the screen to differ from the stored screenshot, which must exist.</summary>
    private static async Task ExpectDifferentAsync(Run run, Locator? subject, RgbaImage? expected, DateTimeOffset deadline, CancellationToken token)
    {
        if (expected is null)
        {
            throw Failure(run, subject, "no stored screenshot at " + run.Shown + " to differ from");
        }

        var compared = false;
        string? missing = null;
        await PollAsync(
            run,
            deadline,
            token,
            async () =>
            {
                var capture = await run.Capture(token).ConfigureAwait(false);
                if (capture.Image is null)
                {
                    missing = capture.Missing;
                    return null;
                }

                compared = true;
                return ImageOps.Compare(expected, capture.Image, run.Call.Tolerance) is ImageComparison.Match;
            },
            cause => compared
                ? Failure(run, subject, "the screen still matches " + run.Shown, cause)
                : Failure(run, subject, missing ?? "no screenshot was taken", cause),
            negated: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Polls a condition until it holds (or, negated, until its negation has held continuously for the grace window),
    /// throwing the caller's error at the deadline. No sleep runs past the deadline, so the last read starts at it.
    /// A read the deadline cut off after an earlier one completed saw nothing: the poll ends there.
    /// <paramref name="evaluate"/> returns null when the condition cannot be evaluated yet.
    /// </summary>
    private static async Task PollAsync(Run run, DateTimeOffset deadline, CancellationToken token, Func<Task<bool?>> evaluate, Func<Exception?, Exception> onTimeout, bool negated)
    {
        var clock = run.Screen.Clock;
        var startedAt = clock.GetUtcNow();
        var grace = deadline - startedAt < NegationGrace ? deadline - startedAt : NegationGrace;
        var readAt = startedAt;
        DateTimeOffset? holdingSince = null;
        var sampled = false;
        bool Holds(DateTimeOffset now) => holdingSince is { } since && now - since >= grace;
        while (true)
        {
            var startedWith = deadline - readAt;
            bool? value;
            try
            {
                value = await evaluate().ConfigureAwait(false);
                sampled = true;
            }
            catch (EngineException ex) when (sampled && startedWith < PollInterval && ex.Code == EngineErrorCodes.OperationTimeout)
            {
                if (negated && Holds(Min(clock.GetUtcNow(), deadline)))
                {
                    return;
                }

                throw onTimeout(ex);
            }

            if (!negated)
            {
                if (value == true)
                {
                    return;
                }
            }
            else
            {
                holdingSince = value == false ? holdingSince ?? readAt : null;
            }

            var now = clock.GetUtcNow();
            if (Holds(now))
            {
                return;
            }

            if (now >= deadline)
            {
                throw onTimeout(null);
            }

            var remaining = deadline - now;
            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, clock, token).ConfigureAwait(false);
            readAt = clock.GetUtcNow();
            if (negated && readAt >= deadline)
            {
                if (Holds(readAt))
                {
                    return;
                }

                throw onTimeout(null);
            }
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    // --- keeping ---

    /// <summary>
    /// Keeps a screen that held still. With nothing stored and no update, the call fails either way: locally the
    /// screenshot is written for review; in CI it is only attached, under the path it belongs at, since a CI
    /// checkout is thrown away and a retry must not pass against what its first attempt wrote.
    /// </summary>
    private static async Task KeepAsync(Run run, Locator? subject, RgbaImage? expected, RgbaImage settled)
    {
        var store = run.Store;
        if (expected is null && !store.Update && store.Ci)
        {
            var kept = Attach(run, Path.Combine("snapshots", Path.IsPathRooted(run.Shown) ? Path.GetFileName(run.File) : run.Shown), settled);
            throw Failure(run, subject, "no stored screenshot at " + run.Shown + ", and CI writes none without E2E_UPDATE_SNAPSHOTS=1; commit this run's there\nactual: " + kept);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(run.File)!);
        var temporary = run.File + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        await System.IO.File.WriteAllBytesAsync(temporary, ImageOps.EncodePng(settled)).ConfigureAwait(false);
        System.IO.File.Move(temporary, run.File, overwrite: true);
        store.MarkWritten(run.File);
        if (expected is null && !store.Update)
        {
            throw Failure(run, subject, "no stored screenshot at " + run.Shown + "; wrote this run's there. Look at it, commit it, and run again");
        }
    }

    /// <summary>The failure of a comparison that never matched, the diff, actual, and stored images attached and named in the message.</summary>
    private static TestException Mismatch(Run run, Locator? subject, RgbaImage expected, RgbaImage actual, ImageComparison comparison, Exception? cause)
    {
        var stem = run.Call.Stem;
        var images = new List<string>();
        if (comparison is ImageComparison.Pixels pixels)
        {
            images.Add("diff: " + Attach(run, stem + "-diff", pixels.Diff));
        }

        images.Add("actual: " + Attach(run, stem + "-actual", actual));
        images.Add("expected: " + Attach(run, stem + "-expected", expected));
        var observed = comparison is ImageComparison.SizeMismatch size
            ? "a " + Number(size.Actual.Width) + "x" + Number(size.Actual.Height) + " screenshot where " + run.Shown + " is " + Number(size.Expected.Width) + "x" + Number(size.Expected.Height)
            : Number(((ImageComparison.Pixels)comparison).DiffPixels) + " pixels (" + FormatRatio(((ImageComparison.Pixels)comparison).Ratio) + " of the image) differ from " + run.Shown;
        return Failure(run, subject, string.Join('\n', new[] { observed + "; run with E2E_UPDATE_SNAPSHOTS=1 to keep this run's" }.Concat(images)), cause);
    }

    private static TestException Failure(Run run, Locator? subject, string message, Exception? cause = null)
    {
        var text = "expect(" + Subject(subject) + ")." + run.Api + " failed\n" + message;
        return cause is null ? new TestException("ASSERTION_FAILED", text) : new TestException("ASSERTION_FAILED", text, cause);
    }

    /// <summary>A share as a percentage with two significant digits at most: <c>0.04%</c>, <c>12%</c>.</summary>
    private static string FormatRatio(double ratio) =>
        double.Parse((ratio * 100).ToString("G2", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture).ToString("0.################", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Writes an image into the attempt's results, at <c>screenshots/&lt;name&gt;.png</c> (or at <paramref name="name"/>
    /// itself when it ends in <c>.png</c>). Returns its path as a failure names it: relative to the project root.
    /// </summary>
    private static string Attach(Run run, string name, RgbaImage image)
    {
        var relative = name.EndsWith(".png", StringComparison.Ordinal) ? name : Path.Combine("screenshots", name + ".png");
        var absolute = Path.Combine(run.Store.ResultsDirectory, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        System.IO.File.WriteAllBytes(absolute, ImageOps.EncodePng(image));
        return Shown(run.Store, absolute);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    // --- capturing ---

    /// <summary>
    /// Captures for one call: the engine's viewport at one image pixel per viewport pixel, so a screenshot is the
    /// same size on every device density, cut to the subject's box when there is one, the masks painted over. The
    /// subject is scrolled into view once, the first time it is found; a subject that is not there yet is a capture
    /// still missing, so the call's poll keeps waiting and fails as an assertion. So is a capture the deadline cut short.
    /// </summary>
    private sealed class Capturer(Screen screen, ScreenshotStore store, Locator? subject, Call call, DateTimeOffset deadline)
    {
        private bool _scrolled = !store.ScrollsIntoView;

        public async Task<Capture> CaptureAsync(CancellationToken cancellationToken)
        {
            var remaining = deadline - screen.Clock.GetUtcNow();
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            try
            {
                return await CaptureOnceAsync(bounded.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (bounded.IsCancellationRequested || screen.Clock.GetUtcNow() >= deadline)
                && (ex is OperationCanceledException || ex is EngineException { Code: EngineErrorCodes.OperationTimeout or EngineErrorCodes.Cancelled } || ex.InnerException is OperationCanceledException))
            {
                return new Capture(null, "the last screenshot ran past the timeout");
            }
        }

        private async Task<Capture> CaptureOnceAsync(CancellationToken cancellationToken)
        {
            var shown = Subject(subject);
            var observation = await screen.ObserveAsync(cancellationToken).ConfigureAwait(false);
            BoundingBox? box = null;
            if (subject is not null)
            {
                var node = OneNode(observation);
                if (node is not null && !_scrolled)
                {
                    try
                    {
                        await screen.PerformAsync(node, new LocatorAction.ScrollIntoView(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is EngineException { Code: EngineErrorCodes.NodeStale } or TestException { Code: "NOT_FOUND" })
                    {
                    }

                    _scrolled = true;
                    observation = await screen.ObserveAsync(cancellationToken).ConfigureAwait(false);
                    node = OneNode(observation);
                }

                if (node is null)
                {
                    return new Capture(null, "no node matches " + shown);
                }

                if (node.States.Hidden || node.Rect is null)
                {
                    return new Capture(null, shown + " is not visible");
                }

                box = node.Rect;
            }

            var shot = await store.Capture(cancellationToken).ConfigureAwait(false);
            var raw = ImageOps.DecodePng(shot.Png);
            var scale = shot.Scale > 0 ? shot.Scale : 1;
            var image = ImageOps.Resample(raw, Math.Max(1, (int)Math.Round(raw.Width / scale)), Math.Max(1, (int)Math.Round(raw.Height / scale)));

            // The engine covers secure fields in the pixels; painting the observed ones as well covers a field the engine's selector missed.
            var secure = LocatorResolver.Walk(observation.Roots).Where(node => node.States.Secure && !node.States.Hidden && node.Rect is not null).Select(node => node.Rect!);
            image = ImageOps.FillBoxes(image, secure, (0, 0, 0));
            if (call.Masks.Count > 0)
            {
                var masks = call.Masks.SelectMany(mask => LocatorResolver.Resolve(observation, mask.Query)).Where(node => !node.States.Hidden && node.Rect is not null).Select(node => node.Rect!);
                image = ImageOps.FillBoxes(image, masks, call.MaskColor);
            }

            if (box is null)
            {
                return new Capture(image, null);
            }

            return ImageOps.ClipBox(image.Width, image.Height, box) is { } clipped
                ? new Capture(ImageOps.Crop(image, clipped), null)
                : new Capture(null, shown + " is outside the viewport");
        }

        private SemanticNode? OneNode(Observation observation)
        {
            var matches = LocatorResolver.Resolve(observation, subject!.Query);
            return matches.Count switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new TestException("STRICT_MODE", subject.Query.Describe() + " matched " + Number(matches.Count) + " nodes."),
            };
        }
    }

    // --- arguments ---

    /// <summary>Checks a call's arguments and names its screenshot; <c>INVALID_ARGUMENT</c> before the screen is read.</summary>
    private static Call Parse(string api, ScreenshotStore store, string? name, ScreenshotOptions? options)
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
        return new Call(stem, tolerance, masks, color, options?.Timeout);
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
    private static string UnnamedStem(ScreenshotStore store)
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
