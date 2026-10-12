// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.AgentObservation;

/// <summary>
/// The port has no executor tree, so two upstream its are not ported: "gives an executor tree an app link absolute, cut
/// no shorter than the line cuts its path", and the half of "never splits a surrogate pair at the cut, and bounds the
/// target an executor tree carries the same way" that reads the tree. The line half is ported.
/// </summary>
public sealed class DisambiguatingAttributesTests
{
    private static SemanticNode Link(string name, string href) =>
        new() { Ref = "n", Role = "link", Name = name, Attributes = new Dictionary<string, string> { ["href"] = href } };

    private static string[] Lines(IEnumerable<SemanticNode> nodes, string? appOrigin = null, Redactor? redactor = null) =>
        SnapshotText.Render(new Observation { Route = "/", Roots = [new SemanticNode { Ref = "n1", Children = nodes.ToList() }] }, redactor ?? Redactor.None, appOrigin).Split(Environment.NewLine);

    [Fact]
    public void Renders_test_IDs_hrefs_and_unnamed_placeholders_for_the_model()
    {
        var lines = Lines(
        [
            new SemanticNode { Ref = "n2", Role = "link", Name = "About", Attributes = new Dictionary<string, string> { ["href"] = "https://app.test/about" } },
            new SemanticNode { Ref = "n3", Role = "listitem", Text = "Alpha", TestId = "item" },
            new SemanticNode { Ref = "n4", Role = "textbox", Placeholder = "you@example.test" },
            new SemanticNode { Ref = "n5", Role = "textbox", Name = "Email", Placeholder = "ignored" },
        ]);

        Assert.Equal("  - link \"About\" href=\"https://app.test/about\" [ref=n2]", lines[2]);
        Assert.Equal("  - listitem \"Alpha\" testid=\"item\" [ref=n3]", lines[3]);
        Assert.Equal("  - textbox placeholder=\"you@example.test\" [ref=n4]", lines[4]);
        Assert.Equal("  - textbox \"Email\" [ref=n5]", lines[5]);
    }

    [Fact]
    public void Renders_a_link_target_on_the_screen_origin_as_its_whole_path_and_cuts_only_past_the_bound_with_a_trailing_ellipsis()
    {
        const string project = "http://127.0.0.1:3100/dashboard/e2e-dc3cf837/projects/404b2532-9fab-44dc-b3d0-f0a1b2c3d4e5";
        var longHref = "https://cdn.example.test/" + new string('a', 300);
        var lines = Lines(
            [Link("Project", project), Link("Docs", "https://docs.example.test/guides/setup"), Link("Search", "http://127.0.0.1:3100/search?…#…"), Link("Asset", longHref), Link("Lookalike", "http://127.0.0.1:31000/x")],
            "http://127.0.0.1:3100");

        Assert.Equal("  - link \"Project\" href=\"/dashboard/e2e-dc3cf837/projects/404b2532-9fab-44dc-b3d0-f0a1b2c3d4e5\" [ref=n]", lines[2]);
        Assert.Equal("  - link \"Docs\" href=\"https://docs.example.test/guides/setup\" [ref=n]", lines[3]);
        Assert.Equal("  - link \"Search\" href=\"/search?…#…\" [ref=n]", lines[4]);
        Assert.Equal($"  - link \"Asset\" href=\"{longHref[..256]}…\" [ref=n]", lines[5]);
        Assert.Equal("  - link \"Lookalike\" href=\"http://127.0.0.1:31000/x\" [ref=n]", lines[6]);
    }

    [Fact]
    public void Keeps_whole_URLs_on_a_page_off_the_app_origin_since_navigation_resolves_a_path_against_the_app()
    {
        var nodes = new[] { Link("Authorize", "https://github.com/login/oauth/authorize?…"), Link("Back to app", "https://app.test/settings") };

        var lines = Lines(nodes, "https://app.test");
        Assert.Equal("  - link \"Authorize\" href=\"https://github.com/login/oauth/authorize?…\" [ref=n]", lines[2]);
        Assert.Equal("  - link \"Back to app\" href=\"/settings\" [ref=n]", lines[3]);
        Assert.Contains("href=\"https://app.test/settings\"", Lines(nodes)[3], StringComparison.Ordinal);
    }

    [Fact]
    public void Never_splits_a_surrogate_pair_at_the_cut()
    {
        var href = "https://cdn.example.test/" + new string('a', 230) + "😀tail";
        Assert.True(char.IsHighSurrogate(href[255]));

        Assert.Equal($"  - link \"Emoji\" href=\"{href[..255]}…\" [ref=n]", Lines([Link("Emoji", href)])[2]);
    }

    [Fact]
    public void Redacts_a_secret_in_a_link_target_before_cutting_it_so_no_part_of_it_survives_the_cut()
    {
        var path = "/" + new string('p', 250) + "/tok-0123456789";
        var redactor = Redactor.For([Secret.Create("token", "tok-0123456789")]);

        var line = Lines([Link("Magic", "https://app.test" + path)], "https://app.test", redactor)[2];

        Assert.Equal($"  - link \"Magic\" href=\"{redactor.Redact(path)[..256]}…\" [ref=n]", line);
        Assert.DoesNotContain("tok-", line, StringComparison.Ordinal);
    }
}
