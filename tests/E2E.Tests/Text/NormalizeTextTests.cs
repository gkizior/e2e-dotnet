// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Tests.Text;

/// <summary>
/// Not ported from text.test.ts: "resets global regexp state between matches", the <c>toTextPattern</c> tests and the
/// <c>normalizeRegexpFlags</c> test (the port takes a <c>Regex</c> or a string by type and has no serialized pattern),
/// and the <c>bound</c> test (<c>TextRules.Truncate</c> cuts without an ellipsis and is not surrogate safe, so the
/// behavior differs).
/// </summary>
public sealed class NormalizeTextTests
{
    [Fact]
    public void Trims_and_collapses_unicode_whitespace_runs_to_one_ASCII_space()
    {
        Assert.Equal("a b c", TextRules.Normalize("  a \t\n b c "));
        Assert.Equal("x", TextRules.Normalize(" x "));
    }
}
