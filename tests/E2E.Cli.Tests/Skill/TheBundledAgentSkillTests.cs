// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using BundledSkill = E2E.Cli.Skill;

namespace E2E.Cli.Tests.Skill;

public sealed class TheBundledAgentSkillTests
{
    [Fact]
    public void Starts_with_a_SKILL_md_whose_frontmatter_names_the_skill_after_its_directory()
    {
        var files = BundledSkill.ReadFiles();
        var skill = files[0];
        Assert.Equal("SKILL.md", skill.Relative);
        Assert.Matches(new Regex("^---\r?\nname: e2e-dotnet\r?\ndescription: .+\r?\n---\r?\n"), skill.Content);
        Assert.Equal(BundledSkill.Topics().Select(topic => "references/" + topic + ".md"), files.Skip(1).Select(file => file.Relative));
        var description = Regex.Match(skill.Content, "^description: (.+?)\r?$", RegexOptions.Multiline).Groups[1].Value;
        Assert.InRange(description.Length, 1, 1024);
        Assert.DoesNotContain(": ", description, StringComparison.Ordinal);
    }

    [Fact]
    // The port adds one writing-tests topic per test framework to upstream's eight.
    public void Offers_one_topic_per_reference_file_and_lists_every_topic_in_SKILL_md()
    {
        var topics = BundledSkill.Topics();
        Assert.Equal(["agent", "bug-bash", "debugging", "explore", "mcp", "running", "setup", "writing-tests", "writing-tests-nunit", "writing-tests-xunit"], topics);
        var overview = BundledSkill.ReadGuide(null) ?? "";
        var linked = Regex.Matches(overview, @"\[references/([a-z-]+)\.md\]\(references/\1\.md\)").Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal);
        Assert.Equal(topics, linked);
        foreach (var topic in topics)
        {
            Assert.Contains("| `" + topic + "` |", overview, StringComparison.Ordinal);
        }
    }

    [Fact]
    // Upstream's overview names `npx e2e guide <topic>`; the .NET tool is `e2e` itself.
    public void Prints_the_overview_without_its_frontmatter_and_each_topic_verbatim()
    {
        var overview = BundledSkill.ReadGuide(null) ?? "";
        Assert.StartsWith("# e2e", overview, StringComparison.Ordinal);
        Assert.DoesNotContain("\n---\n", overview, StringComparison.Ordinal);
        Assert.Contains("e2e guide <topic>", overview, StringComparison.Ordinal);
        foreach (var topic in BundledSkill.Topics())
        {
            var text = BundledSkill.ReadGuide(topic) ?? "";
            Assert.StartsWith("# ", text, StringComparison.Ordinal);
            Assert.Equal(BundledSkill.ReadFiles().Single(file => file.Relative == "references/" + topic + ".md").Content, text);
        }

        Assert.Null(BundledSkill.ReadGuide("nope"));
        Assert.Null(BundledSkill.ReadGuide("../SKILL"));
    }

    [Fact]
    public void Runs_the_installed_CLI_as_npx_e2e_without_no_install_or_the_scoped_package_name()
    {
        foreach (var file in BundledSkill.ReadFiles())
        {
            Assert.False(file.Content.Contains("--no-install", StringComparison.Ordinal), file.Relative);
            Assert.False(Regex.IsMatch(file.Content, @"npx @e2edev/e2e (run|guide)\b"), file.Relative);
        }
    }
}
