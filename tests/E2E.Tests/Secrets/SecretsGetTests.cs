// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace E2E.Tests.Secrets;

/// <summary>
/// Both tests are partial. The port's message for an undeclared name does not name <c>E2E_SECRET_MISSING</c>, and a
/// .NET handle is not frozen, so the handle test checks that the value is never in its text or JSON.
/// </summary>
public sealed class SecretsGetTests
{
    private static readonly E2E.Secrets Declared = new(new Dictionary<string, Secret>(StringComparer.Ordinal)
    {
        ["api-key"] = Secret.Create("api-key", "sk_live_1_value", "generic-secret"),
    });

    [Fact]
    public void Throws_SECRET_UNAVAILABLE_naming_a_name_the_running_config_does_not_declare()
    {
        var error = Assert.Throws<TestException>(() => Declared.Get("missing"));
        Assert.Equal("SECRET_UNAVAILABLE", error.Code);
        Assert.Contains("'missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Returns_a_generic_handle_that_never_carries_the_value()
    {
        var handle = Declared.Get("api-key");

        Assert.Equal("api-key", handle.Name);
        Assert.Equal("generic-secret", handle.Purpose);
        Assert.Equal("<secret:api-key>", handle.ToString());
        Assert.DoesNotContain("sk_live_1_value", JsonSerializer.Serialize(handle), StringComparison.Ordinal);
    }
}
