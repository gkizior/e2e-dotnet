// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.SecretLedger;

public sealed class CaseTransformedFormsTests
{
    private const string Hex = "Tok-9f3aC0DeB1e7";

    [Fact]
    public void Redacts_the_value_upper_cased_lower_cased_and_capitalized_as_CSS_text_transform_renders_it()
    {
        var ledger = Ledger.Of(("token", Hex));
        Assert.Equal("plain <secret:token>", ledger.Redact("plain " + Hex));
        Assert.Equal("upper <secret:token>", ledger.Redact("upper " + Hex.ToUpperInvariant()));
        Assert.Equal("lower <secret:token>", ledger.Redact("lower " + Hex.ToLowerInvariant()));
        Assert.Equal("capitalized <secret:token>", ledger.Redact("capitalized Tok-9f3ac0deb1e7"));
    }

    [Fact]
    public void Redacts_full_Unicode_case_mappings_locale_specific_ones_and_a_sigma_lower_cased_at_the_end_of_a_word()
    {
        var ledger = Ledger.Of(("german", "straße-ǆungla"), ("turkish", "kilit-sifre"), ("greek", "ΚΛΕΙΔΙΣ-77"));
        Assert.Equal("<secret:german> <secret:german>", ledger.Redact("STRASSE-ǄUNGLA Strasse-ǅungla"));
        Assert.Equal("<secret:turkish>", ledger.Redact("KİLİT-SİFRE"));
        Assert.Equal("<secret:greek> <secret:greek> <secret:greek> κλειδι-77", ledger.Redact("κλειδις-77 κλειδισ-77 Κλειδις-77 κλειδι-77"));
    }

    [Fact]
    public void Redacts_a_case_variant_in_its_encoded_spellings()
    {
        var ledger = Ledger.Of(("accent", "café-crème-42"));
        Assert.Equal("<secret:accent>", ledger.Redact("CAF&#201;-CR&#xC8;ME-42"));
        Assert.Equal("""{"v":"<secret:accent>"}""", ledger.Redact("""{"v":"CAFÉ-CRÈME-42"}"""));
    }

    [Fact]
    public void Leaves_text_that_differs_in_more_than_case_alone()
    {
        var ledger = Ledger.Of(("token", Hex));
        Assert.Equal("TOK-9F3AC0DEB1E8 tok 9f3ac0deb1e7", ledger.Redact("TOK-9F3AC0DEB1E8 tok 9f3ac0deb1e7"));
    }
}
