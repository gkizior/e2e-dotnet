// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Tests.Text;

public sealed class BoundTests
{
    [Fact]
    public void Keeps_text_within_its_cap_whole_and_cuts_longer_text_with_an_ellipsis_never_through_a_surrogate_pair()
    {
        Assert.Equal("short", TextRules.Bound("short", 10));
        Assert.Equal("abcd…", TextRules.Bound("abcdefghij", 5));

        // The cut lands between the halves of 😀; it steps back rather than leave half a character.
        Assert.Equal("abc…", TextRules.Bound("abc😀def", 5));
        Assert.Equal("ab😀…", TextRules.Bound("ab😀def", 5));
    }
}
