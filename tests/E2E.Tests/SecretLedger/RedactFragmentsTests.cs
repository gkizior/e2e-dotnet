// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.SecretLedger;

public sealed class RedactFragmentsTests
{
    private const string Value = "cut-secret-Kq7ZrT2mWx9pLd4sNv8bHc3jFg6yQa1eUo5iRk0tYw2zXn7uM";

    [Fact]
    public void Rewrites_a_run_of_8_or_more_characters_of_a_value_anywhere_in_the_text_and_leaves_a_shorter_one()
    {
        var ledger = Ledger.Of(("apiKey", Value));
        Assert.Equal("cut <secret:apiKey>", ledger.RedactFragments("cut " + Value[..59]));
        Assert.Equal("sel <secret:apiKey> end", ledger.RedactFragments("sel " + Value[5..45] + " end"));
        Assert.Equal("a <secret:apiKey> b", ledger.RedactFragments("a " + Value[20..28] + " b"));
        Assert.Equal("a " + Value[20..27] + " b", ledger.RedactFragments("a " + Value[20..27] + " b"));
        Assert.Equal("plain text, 0123456789 and more", ledger.RedactFragments("plain text, 0123456789 and more"));
    }

    [Fact]
    public void Rewrites_a_fragment_in_another_case_and_leaves_a_shorter_one()
    {
        var ledger = Ledger.Of(("apiKey", Value));
        Assert.Equal("sel <secret:apiKey> end", ledger.RedactFragments("sel " + Value[5..45].ToUpperInvariant() + " end"));
        Assert.Equal("a <secret:apiKey> b", ledger.RedactFragments("a " + Value[20..28].ToLowerInvariant() + " b"));
        Assert.Equal("a " + Value[20..27].ToUpperInvariant() + " b", ledger.RedactFragments("a " + Value[20..27].ToUpperInvariant() + " b"));
    }

    [Fact]
    public void Rewrites_whole_values_in_every_spelling_first_then_the_fragments_between_them_and_never_a_marker()
    {
        var ledger = Ledger.Of(("member", "pa\"ss-word-2718-xyz"));
        Assert.Equal("{\"v\":\"<secret:member>\"} <secret:member>", ledger.RedactFragments("{\"v\":\"pa\\\"ss-word-2718-xyz\"} ss-word-2718"));
        Assert.Equal("<secret:member> and <secret:member>", ledger.RedactFragments("<secret:member> and <secret:member>"));
    }

    [Fact]
    public void Names_a_run_two_values_share_after_the_longer_and_a_whole_shorter_value_after_itself()
    {
        const string Short = "ovl-secret-AbCdEfGhIjKlMnOpQrSt";
        const string Long = Short + "UvWxYz0123456789ABCDEFGHIJKLMN";
        var ledger = Ledger.Of(("short", Short), ("long", Long));
        Assert.Equal("x <secret:long>", ledger.RedactFragments("x " + Short[..20]));
        Assert.Equal("x <secret:short><secret:long>", ledger.RedactFragments("x " + Long[..59]));
    }

    [Fact]
    public void Rewrites_a_base64_run_that_decodes_to_a_value_or_a_fragment_whole_and_leaves_other_runs()
    {
        var ledger = Ledger.Of(("basic", "S3cretPassw0rd"));
        Assert.Equal("Basic <secret:basic>", ledger.RedactFragments("Basic " + Base64("bbuser:S3cretPassw0rd")));
        Assert.Equal("<secret:basic>", ledger.RedactFragments(Base64Url("x:S3cretPa")));
        var clean = "Basic " + Base64("bbuser:other-password") + " authorization";
        Assert.Equal(clean, ledger.RedactFragments(clean));
    }

    [Fact]
    public void Rewrites_a_base64_run_that_starts_with_text_the_encoding_does_not()
    {
        var ledger = Ledger.Of(("basic", "S3cretPassw0rd"));
        var encoded = Base64Url("ada:S3cretPassw0rd");
        foreach (var prefix in new[] { "https://app.test/reset/", "cookie=v2_", "tokenValue", "x-" })
        {
            Assert.DoesNotContain(encoded[4..16], ledger.RedactFragments(prefix + encoded), StringComparison.Ordinal);
        }

        Assert.Equal("https://app.<secret:basic>", ledger.RedactFragments("https://app.test/reset/" + encoded));
    }

    [Fact]
    public void Reads_a_marker_a_base64_run_already_encodes_as_page_text_not_as_a_value()
    {
        var ledger = Ledger.Of(("basic", "S3cretPassw0rd"), ("other", "Oth3r-Secret-Value"));
        var page = Base64("shown <secret:basic> here");
        Assert.Equal(page, ledger.RedactFragments(page));
        Assert.Equal("<secret:basic>", ledger.RedactFragments(Base64("<secret:other> ada:S3cretPassw0rd")));
    }

    [Fact]
    public void Rewrites_a_whole_short_value_in_a_base64_run()
    {
        var ledger = Ledger.Of(("pin", "pw1234"));
        foreach (var prefix in new[] { "", "a", "ab" })
        {
            Assert.Equal("t=<secret:pin>", ledger.RedactFragments("t=" + Base64(prefix + "u:pw1234")));
        }
    }

    [Fact]
    public void Folds_a_fragment_as_whole_value_matching_does_keeping_every_index()
    {
        var ledger = Ledger.Of(("turkish", "fragment-igloo-sigma-2718"), ("longS", "long-\u017fecret-\u017ftring-3141"));
        var turkish = "fragment-igloo-sigma-2718"[..20].ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr"));
        Assert.Contains("\u0130", turkish, StringComparison.Ordinal);
        Assert.Equal("x <secret:turkish> y", ledger.RedactFragments("x " + turkish + " y"));
        Assert.Equal("x <secret:longS> y", ledger.RedactFragments("x " + "long-\u017fecret-\u017ftring-3141"[3..18].ToUpperInvariant() + " y"));
    }

    [Fact]
    public void Rewrites_a_fragment_spanning_a_whitespace_run_the_text_collapsed_widened_or_spelled_otherwise_run_included()
    {
        var ledger = Ledger.Of(("multi", "abcde\r\n\tfghijklmnopqrstu"));
        foreach (var separator in new[] { " ", "\n", "\u00a0", "  \t " })
        {
            Assert.Equal("cut [<secret:multi>] end", ledger.RedactFragments("cut [abcde" + separator + "fgh] end"));
        }

        Assert.Equal("plain abcde xyz fghij", ledger.RedactFragments("plain abcde xyz fghij"));
    }

    [Theory]
    [InlineData("single spaces", "var a = 1; ")]
    [InlineData("line breaks and indentation", "var a = 1;\n    ")]
    public void Reads_a_large_text_in_about_its_own_size_and_still_finds_a_fragment_at_its_end(string kind, string line)
    {
        _ = kind;
        var ledger = Ledger.Of(("apiKey", Value));
        var text = string.Concat(Enumerable.Repeat(line, (int)Math.Ceiling(8_000_000.0 / line.Length))) + Value[10..30];
        var before = GC.GetTotalMemory(forceFullCollection: false);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var redacted = ledger.RedactFragments(text);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
        Assert.True(GC.GetTotalMemory(forceFullCollection: false) - before < text.Length * 16L, "used too much memory");
        Assert.EndsWith("<secret:apiKey>", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Scans_a_long_text_in_one_linear_pass()
    {
        var ledger = Ledger.Of(("apiKey", Value));
        double Scan(int repeats)
        {
            var text = string.Concat(Enumerable.Repeat(Value[..7].ToUpperInvariant() + " ", repeats));
            var fastest = double.MaxValue;
            for (var run = 0; run < 3; run++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Assert.Equal(text, ledger.RedactFragments(text));
                fastest = Math.Min(fastest, watch.Elapsed.TotalMilliseconds);
            }

            return fastest;
        }

        var once = Scan(20_000);

        // Eight times the text costs about eight times the time; a quadratic scan costs 64.
        Assert.True(Scan(160_000) / Math.Max(once, 1) < 24);
    }

    [Fact]
    public void Changes_nothing_with_no_value_registered()
    {
        Assert.Equal("anything at all", Ledger.Of().RedactFragments("anything at all"));
    }

    private static string Base64(string text) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text));

    private static string Base64Url(string text) => Base64(text).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
