// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.SecretLedger;

/// <summary>"keeps the MCP call boundary from rewriting what observe already redacted" is not ported: the port has no MCP server.</summary>
public sealed class MarkersTests
{
    [Fact]
    public void Redacts_twice_as_it_redacts_once_a_marker_it_wrote_is_never_read_again()
    {
        var ledger = Ledger.Of(("apiKey", "api"));
        var once = ledger.Redact("key api set");
        Assert.Equal("key <secret:apiKey> set", once);
        Assert.Equal(once, ledger.Redact(once));
    }

    [Fact]
    public void Leaves_a_marker_whole_when_another_value_is_a_substring_of_it_or_of_the_word_secret()
    {
        var ledger = Ledger.Of(("long", "longvalue"), ("word", "secret"), ("apiKey", "api"), ("k", "Key"));
        Assert.Equal("<secret:long> <secret:apiKey> <secret:k> <secret:word>", ledger.Redact("longvalue api Key secret"));
    }

    [Fact]
    public void Cuts_out_only_the_markers_of_names_it_knows_so_a_marker_shaped_span_holding_a_raw_value_is_still_redacted()
    {
        var ledger = Ledger.Of(("apiKey", "sk-1234"));
        Assert.Equal("<secret:<secret:apiKey>> <secret:other>", ledger.Redact("<secret:sk-1234> <secret:other>"));
    }
}
