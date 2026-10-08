// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.SecretLedger;

/// <summary>"holds a value whose whitespace a line break can stand for across lines of a stream" is not ported: the port has no stream redactor.</summary>
public sealed class WhitespaceNormalizedFormsTests
{
    private const string Note = "first line 4417\nsecond  line\tQx";

    [Fact]
    public void Redacts_a_multi_line_value_with_each_whitespace_run_collapsed_to_one_space_as_a_text_reader_shows_it()
    {
        var ledger = Ledger.Of(("note", Note));
        Assert.Equal("<pre><secret:note></pre>", ledger.Redact("<pre>" + Note + "</pre>"));
        Assert.Equal("text \"<secret:note>\"", ledger.Redact("text \"first line 4417 second line Qx\""));
        Assert.Equal("""{"actual":"<secret:note>"}""", ledger.Redact("""{"actual":"first line 4417 second line Qx"}"""));
        Assert.Equal("""{"actual":"<secret:note>"}""", ledger.Redact("""{"actual":"first line 4417\nsecond  line\tQx"}"""));
    }

    [Fact]
    public void Matches_one_Unicode_whitespace_character_and_the_space_in_its_encoded_spellings_for_a_run()
    {
        var ledger = Ledger.Of(("phrase", "correct horse battery"));
        Assert.Equal("<secret:phrase>", ledger.Redact("correct horse battery"));
        Assert.Equal("?q=<secret:phrase>", ledger.Redact("?q=correct+horse%20battery"));
        Assert.Equal("<secret:phrase>", ledger.Redact("correct horse\nbattery"));
    }

    [Fact]
    public void Redacts_a_value_with_leading_or_trailing_whitespace_trimmed_and_keeps_the_edge_whitespace_the_text_has()
    {
        var ledger = Ledger.Of(("padded", "  padded-secret-99\n"));
        Assert.Equal("value: <secret:padded>.", ledger.Redact("value: padded-secret-99."));
        Assert.Equal("x<secret:padded>y", ledger.Redact("x  padded-secret-99\ny"));
    }

    [Fact]
    public void Adds_no_trimmed_form_shorter_than_the_shortest_secret_so_it_cannot_take_ordinary_text()
    {
        var ledger = Ledger.Of(("short", "     ab"));
        Assert.Equal("ab cab", ledger.Redact("ab cab"));
        Assert.Equal("x<secret:short>", ledger.Redact("x     ab"));
    }

    [Fact]
    public void Matches_a_run_shortened_to_any_length_down_to_one_character_spelled_as_written_or_encoded()
    {
        var ledger = Ledger.Of(("block", "row-one-11\r\n\r\nrow-two-22"));
        Assert.Equal("<secret:block>", ledger.Redact("row-one-11\n\nrow-two-22"));
        Assert.Equal("<secret:block>", ledger.Redact("row-one-11\nrow-two-22"));
        Assert.Equal("""{"v":"<secret:block>"}""", ledger.Redact("""{"v":"row-one-11\r\n\r\nrow-two-22"}"""));
    }

    [Fact]
    public void Leaves_whitespace_runs_the_value_does_not_have_and_a_value_whose_words_are_glued_together()
    {
        var ledger = Ledger.Of(("phrase", "correct horse battery"));
        Assert.Equal("correct  horse battery correcthorse battery", ledger.Redact("correct  horse battery correcthorse battery"));
    }
}
