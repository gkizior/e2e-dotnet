// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Tests.Store;

namespace E2E.Tests.Secrets;

/// <summary>
/// The port reads the process environment, so these tests set variables one at a time. "throws AUTH_CREDENTIAL_UNAVAILABLE
/// when the runner is not active" and "replacing the registry swaps the visible credential set" are not ported: the port
/// has no registry or runner. The handle test is partial: a .NET handle is not frozen, so it checks that the password is
/// never in the handle's text or JSON.
/// </summary>
[Collection(ProcessEnvironment.Name)]
public sealed class CredentialsUserTests
{
    [Fact]
    public void Throws_AUTH_CREDENTIAL_UNAVAILABLE_for_an_unconfigured_name()
    {
        var error = Assert.Throws<TestException>(() => Credentials.User("secrets-test-missing"));
        Assert.Equal("AUTH_CREDENTIAL_UNAVAILABLE", error.Code);
        Assert.Contains("'secrets-test-missing' is not configured", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Returns_a_handle_exposing_username_but_never_the_password_value()
    {
        using var username = ProcessEnvironment.Set("E2E_USER_SECRETS_TEST_ADMIN_USERNAME", "admin@example.com");
        using var password = ProcessEnvironment.Set("E2E_USER_SECRETS_TEST_ADMIN_PASSWORD", "super-secret-password");

        var handle = Credentials.User("secrets-test-admin");

        Assert.Equal("admin@example.com", handle.Username);
        Assert.DoesNotContain("super-secret-password", handle.Password.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-password", JsonSerializer.Serialize(handle), StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret-password", JsonSerializer.Serialize(handle.Password), StringComparison.Ordinal);
    }
}
