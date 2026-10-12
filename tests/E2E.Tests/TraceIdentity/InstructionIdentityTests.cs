// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceIdentity;

/// <summary>Instruction identity. The port keys on the trimmed instruction, with no digest and no Unicode normalization.</summary>
public sealed class InstructionIdentityTests
{
    private static string KeyOf(string instruction) => CacheKeys.Create("document", "billing upgrade", instruction, null, "default", null);

    /// <summary>
    /// Partial port of "normalizes line endings, NFC, and outer whitespace only". The port has no <c>instructionDigest</c> and
    /// does not normalize line endings inside the text or Unicode form, so only the outer whitespace and the inner spacing are checked.
    /// </summary>
    [Fact]
    public void Trims_outer_whitespace_and_keeps_inner_spacing_in_the_key()
    {
        Assert.Equal(KeyOf("open billing"), KeyOf("  open billing\r\n"));
        Assert.NotEqual(KeyOf("open billing"), KeyOf("open  billing"));
    }
}
