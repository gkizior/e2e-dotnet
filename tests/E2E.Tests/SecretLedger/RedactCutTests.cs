// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.SecretLedger;

public sealed class RedactCutTests
{
    private const string Value = "cut-secret-Kq7ZrT2mWx9pLd4sNv8bHc3jFg6yQa1eUo5iRk0tYw2zXn7uM";

    private const string Lead = "leadqzrtmwxplkdsnvbhcjfg";

    private const string Tail = "tailyqaeuoirktywzxnumbvcxzlkjhgfdsapoiuytrewqmnbvcxzlkj";

    // A line break, a tab, a CRLF, a no-break space, and repeated spaces.
    public static TheoryData<string> Separators => new() { "\n", "\t", "\r\n", " ", "   " };

    [Fact]
    public void Rewrites_the_leading_part_of_a_value_the_cut_stopped_inside_down_to_8_characters()
    {
        var ledger = Ledger.Of(("apiKey", Value));
        foreach (var kept in new[] { 59, 40, 8 })
        {
            var cut = "receipt " + Value[..kept];
            Assert.Equal(cut, ledger.Redact(cut));
            Assert.Equal("receipt <secret:apiKey>", ledger.RedactCut(cut));
        }
    }

    [Fact]
    public void Leaves_a_leading_part_shorter_than_the_minimum_as_it_is_so_plain_text_is_not_taken_for_one()
    {
        var ledger = Ledger.Of(("long", "ovl-secret-AbCdEfGhIjKlMnOpQrStUvWxYz"));
        foreach (var text in new[] { "name-co", "name-control-o", "text ovl-sec" })
        {
            Assert.Equal(text, ledger.RedactCut(text));
        }
    }

    [Fact]
    public void Still_rewrites_whole_values_and_the_fragment_after_the_last_one_down_to_half_of_a_short_value()
    {
        var ledger = Ledger.Of(("member", "hunter2"));
        Assert.Equal("<secret:member> then <secret:member> then <secret:member>", ledger.RedactCut("hunter2 then hunter2 then hunt"));
        Assert.Equal("<secret:member> then hun", ledger.RedactCut("hunter2 then hun"));
        Assert.Equal("<secret:member><secret:member>", ledger.RedactCut("hunter2hunt"));
    }

    [Fact]
    public void Leaves_a_cut_plain_value_a_fragment_not_at_the_end_and_a_text_ending_in_a_marker_as_they_are()
    {
        var ledger = Ledger.Of(("member", "hunter2"));
        Assert.Equal("plain-control-plain-control-pla", ledger.RedactCut("plain-control-plain-control-pla"));
        Assert.Equal("hunt the snark", ledger.RedactCut("hunt the snark"));
        Assert.Equal("pw <secret:member>", ledger.RedactCut("pw hunter2"));
        Assert.Equal("pw <secret:member>", ledger.RedactCut("pw <secret:member>"));
    }

    [Fact]
    public void Rewrites_the_longest_fragment_any_value_leaves_at_the_end()
    {
        var ledger = Ledger.Of(("short", "ab-xyz"), ("long", "cab-long-value"));
        Assert.Equal("text <secret:long>", ledger.RedactCut("text cab-long"));
        Assert.Equal("text <secret:short>", ledger.RedactCut("text ab-"));
    }

    [Fact]
    public void Rewrites_the_cut_part_of_a_value_that_starts_with_another_value_or_that_a_whole_value_runs_into_as_one_marker()
    {
        var prefixed = Ledger.Of(("short", "abcdef12"), ("long", "abcdef12-XYZ-long"));
        Assert.Equal("pw <secret:long>", prefixed.RedactCut("pw abcdef12-XY"));
        Assert.Equal("pw <secret:short> <secret:long>", prefixed.RedactCut("pw abcdef12 abcdef12-XY"));
        var chained = Ledger.Of(("first", "abc123xyz"), ("second", "xyz-tail-value"));
        Assert.Equal("pw <secret:second>", chained.RedactCut("pw abc123xyz-tai"));
    }

    [Fact]
    public void Rewrites_a_cut_part_shown_in_another_case_as_a_CSS_text_transform_shows_it()
    {
        var ledger = Ledger.Of(("apiKey", Value));
        Assert.Equal("receipt <secret:apiKey>", ledger.RedactCut("receipt " + Value[..59].ToUpperInvariant()));
        Assert.Equal("receipt <secret:apiKey>", ledger.RedactCut("receipt " + Value[..8].ToLowerInvariant()));
        Assert.Equal("receipt CUT-SEC", ledger.RedactCut("receipt CUT-SEC"));
    }

    // Upstream's describe.each over the separators, a value with each one cut after the engine collapsed it.
    [Theory]
    [MemberData(nameof(Separators))]
    public void Rewrites_the_part_left_when_the_cut_falls_in_the_second_part(string separator)
    {
        Assert.Equal(Padded("<secret:multi>"), Multi(separator).RedactCut(Padded(Lead + " " + Tail[..14])));
    }

    [Theory]
    [MemberData(nameof(Separators))]
    public void Rewrites_the_part_left_when_the_cut_falls_on_the_collapsed_space(string separator)
    {
        Assert.Equal(Padded("<secret:multi>"), Multi(separator).RedactCut(Padded(Lead + " ")));
    }

    [Theory]
    [MemberData(nameof(Separators))]
    public void Rewrites_the_part_left_when_the_cut_falls_in_the_first_part(string separator)
    {
        Assert.Equal(Padded("<secret:multi>"), Multi(separator).RedactCut(Padded(Lead[..10])));
    }

    [Theory]
    [MemberData(nameof(Separators))]
    public void Rewrites_the_part_with_the_whitespace_as_the_value_writes_it_or_widened(string separator)
    {
        var ledger = Multi(separator);
        Assert.Equal("pad <secret:multi>", ledger.RedactCut("pad " + Lead + separator + Tail[..3]));
        Assert.Equal("pad <secret:multi>", ledger.RedactCut("pad " + Lead + "\n\n\n" + Tail[..3]));
    }

    [Fact]
    public void Rewrites_a_cut_hex_value_with_a_line_break_and_one_whose_leading_whitespace_the_reader_trimmed()
    {
        var hex = Ledger.Of(("hex", "0f3a9c71e4b2d85601aa7c3e\n9b84f0c2d17e6a35b9f04c8d2e71a6b3c95d08f4e2a7b61c3d9e0f5a8b47c26"));
        Assert.Equal("id <secret:hex>", hex.RedactCut("id 0f3a9c71e4b2d85601aa7c3e 9b84f0c2d1"));
        var padded = Ledger.Of(("padded", "\n\t  padded-secret-value-2718"));
        Assert.Equal("value:<secret:padded>", padded.RedactCut("value: padded-secr"));
        Assert.Equal("<secret:padded>", padded.RedactCut("padded-secr"));
    }

    [Fact]
    public void Changes_nothing_with_no_value_registered()
    {
        Assert.Equal("anything at all", Ledger.Of().RedactCut("anything at all"));
    }

    private static E2E.Internal.Redactor Multi(string separator) => Ledger.Of(("multi", Lead + separator + Tail));

    private static string Padded(string kept) => new string('x', 40) + " " + kept;
}
