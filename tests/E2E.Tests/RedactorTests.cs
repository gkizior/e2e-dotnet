// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Internal;

namespace E2E.Tests;

public sealed class RedactorTests
{
    private static readonly Secret[] Secrets = [Secret.Create("token", "Hunter2/Pass  word")];

    [Theory]
    [InlineData("collapsed Hunter2/Pass word end")]
    [InlineData("upper HUNTER2/PASS WORD end")]
    [InlineData("lower hunter2/pass word end")]
    [InlineData("json Hunter2\\/Pass word end")]
    [InlineData("unicode Hunter2\\u002fPass word end")]
    [InlineData("url Hunter2%2FPass+word end")]
    [InlineData("url Hunter2%2fPass%20word end")]
    [InlineData("html Hunter2&#47;Pass&#x20;word end")]
    [InlineData("written Hunter2/Pass  word end")]
    public void Redacts_each_spelling(string text)
    {
        var redacted = SnapshotText.Redact(text, Secrets);
        Assert.Contains("<secret:token>", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(" end", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redacting_twice_is_redacting_once()
    {
        var secrets = new[] { Secret.Create("secret", "secret") };
        var once = SnapshotText.Redact("a secret here", secrets);
        Assert.Equal("a <secret:secret> here", once);
        Assert.Equal(once, SnapshotText.Redact(once, secrets));
    }

    [Fact]
    public void Longer_value_wins()
    {
        var secrets = new[] { Secret.Create("short", "abcdef"), Secret.Create("long", "abcdef-123456") };
        Assert.Equal("x <secret:long> y <secret:short>", SnapshotText.Redact("x ABCDEF-123456 y abcdef", secrets));
    }

    [Fact]
    public void Fragments_are_redacted_in_engine_text()
    {
        var redactor = Redactor.ForValue("correct-horse-battery");
        var message = redactor.RedactFragments("fill(\"CORRECT-HORSE-BAT…\") timed out");
        Assert.Equal("fill(\"[redacted]…\") timed out", message);
    }

    [Fact]
    public void Text_without_values_is_unchanged()
    {
        Assert.Equal("nothing to see", SnapshotText.Redact("nothing to see", Secrets));
        Assert.Equal("nothing to see", SnapshotText.Redact("nothing to see", []));
    }
}
