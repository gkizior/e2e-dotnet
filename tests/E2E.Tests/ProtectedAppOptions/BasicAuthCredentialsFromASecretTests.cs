// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

/// <summary>
/// The credentials a <c>Secret</c> password becomes. Not ported: "declares a secrets.get() password as the engine secret
/// it resolves per attempt". The port has no engine secrets to declare; the engine resolves the password through the
/// attempt when it starts.
/// </summary>
public sealed class BasicAuthCredentialsFromASecretTests
{
    [Fact]
    public async Task Registers_the_Authorization_credential_the_password_becomes_padded_and_not_for_redaction()
    {
        var asked = new List<(string Name, IReadOnlyList<string> Derived)>();
        var credentials = await WebEngine.HttpCredentialsAsync(
            new WebBasicAuth("ada", Secret.Create("stagingPassword", "pa55word!")),
            (secret, options) =>
            {
                asked.Add((secret.Name, options?.Derived?.Invoke("pa55word!") ?? []));
                return Task.FromResult("pa55word!");
            });

        Assert.Equal("ada", credentials.Username);
        Assert.Equal("pa55word!", credentials.Password);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("ada:pa55word!"));
        Assert.EndsWith("=", encoded, StringComparison.Ordinal);
        var (name, derived) = Assert.Single(asked);
        Assert.Equal("stagingPassword", name);
        Assert.Equal([encoded, encoded.TrimEnd('=')], derived);
    }

    [Fact]
    public async Task Resolves_nothing_for_a_plain_string_password()
    {
        var credentials = await WebEngine.HttpCredentialsAsync(
            new WebBasicAuth("ada", "plain-value"),
            (_, _) => throw new InvalidOperationException("not a secret"));

        Assert.Equal("ada", credentials.Username);
        Assert.Equal("plain-value", credentials.Password);
    }
}
