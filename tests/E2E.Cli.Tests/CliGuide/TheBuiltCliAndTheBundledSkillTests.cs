// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;

namespace E2E.Cli.Tests.CliGuide;

public sealed class TheBuiltCliAndTheBundledSkillTests
{
    [Fact]
    // The topic list holds the port's two framework topics beside upstream's eight.
    public async Task Prints_the_overview_and_a_topic_from_the_package_copy_of_the_skill()
    {
        var dir = Directory.CreateTempSubdirectory("e2e-guide-").FullName;
        try
        {
            var overview = await RunAsync(dir, "guide");
            Assert.StartsWith("# e2e", overview.Stdout, StringComparison.Ordinal);
            var topic = await RunAsync(dir, "guide", "writing-tests");
            Assert.Equal(await File.ReadAllTextAsync(Path.Combine(RepoSkill(), "references", "writing-tests.md")), topic.Stdout);
            var unknown = await RunAsync(dir, "guide", "nope");
            Assert.Equal(2, unknown.Code);
            Assert.Contains("unknown topic \"nope\"; topics: agent, bug-bash, debugging, explore, mcp, running, setup, writing-tests, writing-tests-nunit, writing-tests-xunit", unknown.Stderr, StringComparison.Ordinal);
            var help = await RunAsync(dir, "--help");
            Assert.Contains("guide [topic]", help.Stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Runs the built <c>e2e</c> tool in <paramref name="dir"/>.</summary>
    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string dir, params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "E2E.Cli.dll"));
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
    }

    /// <summary>The repository's <c>skills/e2e-dotnet</c> directory.</summary>
    private static string RepoSkill()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "E2E.slnx")))
        {
            root = root.Parent ?? throw new InvalidOperationException("no E2E.slnx above " + AppContext.BaseDirectory);
        }

        return Path.Combine(root.FullName, "skills", "e2e-dotnet");
    }
}
