// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Cli.Tests.Skill;

public sealed class TheSkillAndTheDocsTests
{
    [Fact]
    public void Pass_artifacts_only_in_a_paragraph_that_says_the_flag_is_gone()
    {
        foreach (var (file, text) in UserFacingPages())
        {
            foreach (var paragraph in Regex.Split(text, @"\n\s*\n").Where(block => block.Contains("--artifacts", StringComparison.Ordinal)))
            {
                Assert.True(Regex.IsMatch(paragraph, "removed|gone"), file);
            }
        }
    }

    /// <summary>The skill sources and the docs pages, relative to the repository root, each with its text.</summary>
    private static IEnumerable<(string File, string Text)> UserFacingPages()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "E2E.slnx")))
        {
            root = root.Parent ?? throw new InvalidOperationException("no E2E.slnx above " + AppContext.BaseDirectory);
        }

        return new[] { Path.Combine("skills", "e2e-dotnet"), "docs" }
            .SelectMany(dir => MarkdownFiles(Path.Combine(root.FullName, dir)))
            .Select(path => (Path.GetRelativePath(root.FullName, path), File.ReadAllText(path)));
    }

    /// <summary>Every <c>.md</c> and <c>.mdx</c> file under <paramref name="dir"/>, never descending into <c>node_modules</c>.</summary>
    private static IEnumerable<string> MarkdownFiles(string dir)
    {
        foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
        {
            if (entry is DirectoryInfo directory)
            {
                if (directory.Name == "node_modules")
                {
                    continue;
                }

                foreach (var file in MarkdownFiles(directory.FullName))
                {
                    yield return file;
                }
            }
            else if (entry.Extension is ".md" or ".mdx")
            {
                yield return entry.FullName;
            }
        }
    }
}
