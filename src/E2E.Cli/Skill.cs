// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Cli;

/// <summary>One file of the bundled skill.</summary>
/// <param name="Relative">Path relative to the skill root with <c>/</c> separators.</param>
/// <param name="Content">The file's text.</param>
internal sealed record SkillFile(string Relative, string Content);

/// <summary>
/// The bundled agent skill: <c>SKILL.md</c> plus one reference file per guide topic. The tool embeds the
/// repository's <c>skills/e2e-dotnet</c> directory, so an installed tool always carries it.
/// </summary>
internal static partial class Skill
{
    public const string Name = "e2e-dotnet";

    private const string ResourcePrefix = "skill/";

    private const string TopicPrefix = "references/";

    private static readonly Lazy<IReadOnlyList<SkillFile>> Files = new(Load);

    /// <summary>Every file of the skill, <c>SKILL.md</c> first.</summary>
    public static IReadOnlyList<SkillFile> ReadFiles() => Files.Value;

    /// <summary>The topics the guide serves: one per <c>references/&lt;topic&gt;.md</c>, never a nested file.</summary>
    public static IReadOnlyList<string> Topics()
    {
        return ReadFiles()
            .Where(file => file.Relative.StartsWith(TopicPrefix, StringComparison.Ordinal) && file.Relative.EndsWith(".md", StringComparison.Ordinal))
            .Select(file => file.Relative[TopicPrefix.Length..^".md".Length])
            .Where(topic => !topic.Contains('/', StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>The overview (<c>SKILL.md</c> without its frontmatter) or one topic's text; null for an unknown topic.</summary>
    public static string? ReadGuide(string? topic)
    {
        var files = ReadFiles();
        if (topic is null)
        {
            var skill = files.FirstOrDefault(file => file.Relative == "SKILL.md");
            return skill is null ? null : Frontmatter().Replace(skill.Content, "", 1);
        }

        return Topics().Contains(topic) ? files.First(file => file.Relative == TopicPrefix + topic + ".md").Content : null;
    }

    private static IReadOnlyList<SkillFile> Load()
    {
        var assembly = typeof(Skill).Assembly;
        var files = new List<SkillFile>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            files.Add(new SkillFile(resource[ResourcePrefix.Length..].Replace('\\', '/'), reader.ReadToEnd()));
        }

        return files
            .OrderBy(file => file.Relative == "SKILL.md" ? 0 : 1)
            // By name without the extension, so a topic comes before the topics that extend its name.
            .ThenBy(file => Path.ChangeExtension(file.Relative, null), StringComparer.Ordinal)
            .ToList();
    }

    [GeneratedRegex(@"\A---\r?\n[\s\S]*?\r?\n---\r?\n(\r?\n)*")]
    private static partial Regex Frontmatter();
}
