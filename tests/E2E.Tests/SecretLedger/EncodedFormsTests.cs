// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.SecretLedger;

/// <summary>"measures the longest spelling a registered value can take" is not ported: the port has no stream redactor to size.</summary>
public sealed class EncodedFormsTests
{
    private const string Member = "p@ss \"w\\rd\" & <x>";

    private readonly E2E.Internal.Redactor _ledger = Ledger.Of(("member", Member));

    [Fact]
    public void Redacts_the_value_as_a_JSON_string_body_once_and_twice_quoted()
    {
        var once = """{"value":"p@ss \"w\\rd\" & <x>"}""";
        Assert.Equal("""{"value":"<secret:member>"}""", _ledger.Redact(once));
        var twice = """{"text":"{\"value\":\"p@ss \\\"w\\\\rd\\\" & <x>\"}"}""";
        Assert.Equal("""{"text":"{\"value\":\"<secret:member>\"}"}""", _ledger.Redact(twice));
    }

    [Fact]
    public void Redacts_the_value_URL_encoded_as_a_query_component_and_as_a_form_body()
    {
        Assert.Equal("/login?pw=<secret:member>", _ledger.Redact("/login?pw=" + Uri.EscapeDataString(Member)));
        Assert.Equal("pw=<secret:member>", _ledger.Redact("pw=p%40ss+%22w%5Crd%22+%26+%3Cx%3E"));
    }

    [Fact]
    public void Redacts_the_value_HTML_escaped()
    {
        Assert.Equal("<input value=\"<secret:member>\">", _ledger.Redact("<input value=\"p@ss &quot;w\\rd&quot; &amp; &lt;x&gt;\">"));
    }

    [Fact]
    public void Adds_no_forms_for_a_value_every_encoding_leaves_alone()
    {
        Assert.Equal("<secret:plain> <secret:plain>", Ledger.Of(("plain", "hunter2")).Redact("hunter2 hunter2"));
    }

    [Fact]
    public void Redacts_percent_encoding_in_lower_case_hex_as_some_servers_spell_it()
    {
        var path = Ledger.Of(("path", "a/b c?d"));
        Assert.Equal("q=<secret:path>", path.Redact("q=a%2fb%20c%3fd"));
        Assert.Equal("q=<secret:path>", path.Redact("q=a%2Fb+c%3Fd"));
    }

    [Fact]
    public void Redacts_numeric_character_references_decimal_zero_padded_hex_in_either_case_and_apos()
    {
        var quoted = Ledger.Of(("quoted", "it's <ok>"));
        Assert.Equal("<secret:quoted>", quoted.Redact("it&#39;s &#60;ok&#62;"));
        Assert.Equal("<secret:quoted>", quoted.Redact("it&#039;s &lt;ok&gt;"));
        Assert.Equal("<secret:quoted>", quoted.Redact("it&#x27;s &#x3c;ok&#x3E;"));
        Assert.Equal("<secret:quoted>", quoted.Redact("it&apos;s &lt;ok&gt;"));
    }

    [Fact]
    public void Redacts_uXXXX_JSON_escapes_in_either_hex_case_once_and_twice_quoted()
    {
        var quoted = Ledger.Of(("quoted", "it's <ok>"));
        Assert.Equal("""{"v":"<secret:quoted>"}""", quoted.Redact("""{"v":"it's <ok>"}"""));
        Assert.Equal(
            """{"t":"{\"v\":\"<secret:quoted>\"}"}""",
            quoted.Redact("""{"t":"{\"v\":\"it\\u0027s \\u003cok\\u003e\"}"}"""));
    }

    [Fact]
    public void Redacts_a_double_quote_doubled_as_CSV_writes_it_inside_a_quoted_field()
    {
        var quoted = Ledger.Of(("quoted", "pa\"ss,word"));
        Assert.Equal("id,key\n1,\"<secret:quoted>\"\n", quoted.Redact("id,key\n1,\"pa\"\"ss,word\"\n"));
        Assert.Equal("\"<secret:lead>\"", Ledger.Of(("lead", "\"quoted")).Redact("\"\"\"quoted\""));
    }

    [Fact]
    public void Redacts_a_slash_escaped_the_way_PHP_writes_JSON()
    {
        Assert.Equal("""{"p":"<secret:path>"}""", Ledger.Of(("path", "a/b/c")).Redact("""{"p":"a\/b\/c"}"""));
    }
}
