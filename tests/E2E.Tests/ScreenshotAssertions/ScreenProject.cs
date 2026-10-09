// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Tests.ScreenshotAssertions;

/// <summary>
/// A project directory in the temp folder, and the test files "run" in it: each run is one session over a
/// <see cref="ScreenEngine"/>, with the stored screenshots in <c>tests/&lt;file&gt;.e2e.ts-snapshots</c>.
/// </summary>
internal sealed class ScreenProject : IDisposable
{
    public ScreenProject(ScreenEngine engine)
    {
        Engine = engine;
        Directory = Path.Combine(Path.GetTempPath(), "e2e-screenshots-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
    }

    public static string Suffix => "-fake-" + ScreenshotStoreName();

    public ScreenEngine Engine { get; }

    public string Directory { get; }

    /// <summary>The results of the last run, for a title.</summary>
    public string ResultsOf(string title, int attempt = 1) =>
        Path.Combine(Directory, ".e2e", "results", ScreenshotAssertion.Slug(title, 100, "test"), "attempt-" + attempt);

    public string Snapshots(string file) => Path.Combine(Directory, "tests", file + ".e2e.ts-snapshots");

    /// <summary>Runs one test body and returns what it threw, or null when it passed. A new run, unless told otherwise, counts no screenshot as written.</summary>
    public async Task<TestException?> RunAsync(string file, string title, Func<Screen, Task> body, bool update = false, bool ci = false, int attempt = 1, bool newRun = true)
    {
        if (newRun)
        {
            ScreenshotStore.ForgetWrittenUnder(Directory);
        }

        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = Engine,
            TestTitle = title,
            ProjectRoot = Directory,
            TargetName = "fake",
            UpdateSnapshots = update,
            Ci = ci,
            Attempt = attempt,
            AssertionTimeout = TimeSpan.FromSeconds(5),
            SnapshotDirectoryOf = _ => Snapshots(file),
        });
        try
        {
            await body(session.Screen);
            return null;
        }
        catch (TestException ex)
        {
            return ex;
        }
    }

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string ScreenshotStoreName() => ScreenshotStore.OperatingSystemName();
}
