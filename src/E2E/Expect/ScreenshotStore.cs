// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using E2E.Engine;

namespace E2E;

/// <summary>Where one attempt's <c>ToHaveScreenshotAsync</c> calls keep their screenshots, and what the process has written so far.</summary>
internal sealed class ScreenshotStore
{
    // Stored screenshots this process wrote. A retry, a repeat, or another test naming the same screenshot
    // compares against an unreviewed file, so the comparison fails rather than passes.
    private static readonly ConcurrentDictionary<string, bool> WrittenFiles = new(StringComparer.Ordinal);

    private int _unnamed;

    /// <summary>The project root; stored screenshots are reported relative to it.</summary>
    public required string ProjectRoot { get; init; }

    /// <summary>Appended to every name so each target and operating system keeps its own: <c>-target-os</c>.</summary>
    public required string Suffix { get; init; }

    /// <summary>The test's title, which names a screenshot the test does not name.</summary>
    public required string Title { get; init; }

    /// <summary>The attempt's results directory: the diff, actual, and expected images go under it.</summary>
    public required string ResultsDirectory { get; init; }

    /// <summary>A missing or different stored screenshot is written, and the matcher passes.</summary>
    public bool Update { get; init; }

    /// <summary>Without <see cref="Update"/>, a CI run never writes into the project: a missing screenshot is only attached to the results.</summary>
    public bool Ci { get; init; }

    /// <summary>Whether the engine can scroll a node into view before it is captured.</summary>
    public bool ScrollsIntoView { get; init; }

    /// <summary>Directory of the stored screenshots of the test file a call was made from.</summary>
    public Func<string?, string?> DirectoryOf { get; init; } = static file => file is null ? null : file + "-snapshots";

    public required Func<CancellationToken, Task<EngineScreenshot>> Capture { get; init; }

    /// <summary>Whether a secret fill withholds pixels for the rest of the attempt.</summary>
    public required Func<bool> WithholdsPixels { get; init; }

    public bool HasWritten(string file) => WrittenFiles.ContainsKey(file);

    /// <summary>Starts a new run for the stored screenshots under <paramref name="directory"/>: none counts as written.</summary>
    internal static void ForgetWrittenUnder(string directory)
    {
        var prefix = Path.EndsInDirectorySeparator(directory) ? directory : directory + Path.DirectorySeparatorChar;
        foreach (var file in WrittenFiles.Keys.Where(file => file.StartsWith(prefix, StringComparison.Ordinal)))
        {
            WrittenFiles.TryRemove(file, out _);
        }
    }

    public void MarkWritten(string file) => WrittenFiles[file] = true;

    /// <summary>Counts an unnamed call, from 1 in each attempt.</summary>
    public int NextUnnamed() => Interlocked.Increment(ref _unnamed);

    /// <summary>The operating system as upstream names it: <c>darwin</c>, <c>linux</c>, or <c>win32</c>.</summary>
    public static string OperatingSystemName() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win32"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
        : "unknown";
}
