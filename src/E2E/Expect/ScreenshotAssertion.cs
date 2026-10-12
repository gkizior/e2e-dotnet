// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using E2E.Internal;

namespace E2E;

/// <summary>
/// <c>toHaveScreenshot</c>: the screen, or one locator's box on it, compared against a PNG stored beside the test
/// file. The engine captures the viewport; everything else happens here.
/// </summary>
internal static partial class ScreenshotAssertion
{
    /// <summary>One call in one attempt.</summary>
    private sealed class Run(Screen screen, ScreenshotContext store, string api, ScreenshotCall call, string file, string shown, Func<CancellationToken, Task<Capture>> capture)
    {
        public Screen Screen { get; } = screen;

        public ScreenshotContext Store { get; } = store;

        public string Api { get; } = api;

        public ScreenshotCall Call { get; } = call;

        public string File { get; } = file;

        public string Shown { get; } = shown;

        public Func<CancellationToken, Task<Capture>> Capture { get; } = capture;
    }

    /// <summary>Runs one call as an assertion: of the whole screen when <paramref name="subject"/> is null, else of the one node it matches.</summary>
    public static async Task ExpectScreenshotAsync(Screen screen, Locator? subject, bool negated, string? name, ScreenshotOptions? options, string? callerFile, CancellationToken cancellationToken)
    {
        var api = (negated ? "not." : "") + "toHaveScreenshot";
        var store = screen.Screenshots
            ?? throw new TestException("UNSUPPORTED_CAPABILITY", api + " keeps screenshots beside a test file, so it runs only inside a test");
        var call = ParseScreenshotCall(api, store, name, options);
        var directory = store.DirectoryOf(callerFile)
            ?? throw new TestException("INVALID_ARGUMENT", api + " could not tell which test file it was called from");
        var file = Path.Combine(directory, call.Stem + store.Suffix + ".png");
        var shown = Shown(store, file);
        var token = screen.Token(cancellationToken);
        var deadline = new Deadline(call.Timeout ?? screen.AssertionTimeout, screen.Clock);

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

    private static string Shown(ScreenshotContext store, string file)
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
    private static async Task<RgbaImage?> MatchOrSettleAsync(Run run, Locator? subject, RgbaImage? expected, Deadline deadline, CancellationToken token)
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
                var comparison = expected is null ? null : ImageOps.CompareImages(expected, current, run.Call.Tolerance);
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

                if (ImageOps.CompareImages(before, current, run.Call.Tolerance) is ImageComparison.Match)
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
                    if (ImageOps.CompareImages(first, second, new ImageTolerance(0, null, null)) is ImageComparison.Pixels pixels)
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
    private static async Task ExpectDifferentAsync(Run run, Locator? subject, RgbaImage? expected, Deadline deadline, CancellationToken token)
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
                return ImageOps.CompareImages(expected, capture.Image, run.Call.Tolerance) is ImageComparison.Match;
            },
            cause => compared
                ? Failure(run, subject, "the screen still matches " + run.Shown, cause)
                : Failure(run, subject, missing ?? "no screenshot was taken", cause),
            negated: true).ConfigureAwait(false);
    }

    /// <summary>
    /// Polls the capture for a call (see <see cref="Time.PollConditionAsync"/>).
    /// <paramref name="evaluate"/> returns null when the condition cannot be evaluated yet.
    /// </summary>
    private static Task PollAsync(Run run, Deadline deadline, CancellationToken token, Func<Task<bool?>> evaluate, Func<Exception?, Exception> onTimeout, bool negated) =>
        Time.PollConditionAsync(new PollConditionOptions
        {
            Deadline = deadline,
            Clock = run.Screen.Clock,
            Token = token,
            Negated = negated,
            Evaluate = evaluate,
            OnTimeout = onTimeout,
        });

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

}
