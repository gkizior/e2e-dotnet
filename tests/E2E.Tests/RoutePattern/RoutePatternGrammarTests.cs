// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.RoutePattern;

public sealed class RoutePatternGrammarTests
{
    [Fact]
    public void Star_matches_within_one_path_segment()
    {
        Assert.True(Matches("http://x.test/api/*", "http://x.test/api/flags"));
        Assert.False(Matches("http://x.test/api/*", "http://x.test/api/a/b"));
    }

    [Fact]
    public void Double_star_crosses_slash()
    {
        Assert.True(Matches("**/api/flags", "http://x.test/api/flags"));
        Assert.True(Matches("http://x.test/**", "http://x.test/a/b/c"));
    }

    [Fact]
    public void Question_mark_matches_one_character()
    {
        Assert.True(Matches("http://x.test/v?", "http://x.test/v1"));
        Assert.False(Matches("http://x.test/v?", "http://x.test/v12"));
    }

    [Fact]
    public void Backslash_escapes_the_next_character()
    {
        Assert.True(Matches("http://x.test/a\\*b", "http://x.test/a*b"));
        Assert.False(Matches("http://x.test/a\\*b", "http://x.test/axb"));
    }

    [Fact]
    public void Other_characters_are_literal_including_dots()
    {
        Assert.True(Matches("http://x.test/a.b", "http://x.test/a.b"));
        Assert.False(Matches("http://x.test/a.b", "http://x.test/aXb"));
    }

    [Fact]
    public void Matches_the_complete_URL_not_a_substring()
    {
        Assert.False(Matches("/api/flags", "http://x.test/api/flags"));
    }

    private static bool Matches(string pattern, string url) => E2E.Internal.Routes.CompilePattern(pattern).IsMatch(url);
}
