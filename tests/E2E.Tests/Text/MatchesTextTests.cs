// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Text;

public sealed class MatchesTextTests
{
    [Fact]
    public void String_matching_is_exact_by_default()
    {
        var signIn = TextMatch.FromString("Sign in");
        Assert.True(signIn.Matches("Sign in", exact: true));
        Assert.False(signIn.Matches("Sign in now", exact: true));
        Assert.True(signIn.Matches("  Sign\n in ", exact: true));
    }

    [Fact]
    public void Exact_false_is_case_insensitive_substring_matching()
    {
        Assert.True(TextMatch.FromString("sign in").Matches("Sign In Now", exact: false));
        Assert.False(TextMatch.FromString("log in").Matches("Sign In Now", exact: false));
    }
}
