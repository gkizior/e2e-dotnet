// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E;

/// <summary>What one live session knows about secrets: the values to redact.</summary>
internal static class Secrecy
{
    /// <summary>
    /// The plaintext of one secret an engine option carries. The value joins <paramref name="ledger"/> before it is
    /// returned, so it is redacted from the moment it exists, whoever it is handed to.
    /// </summary>
    public static string ResolveSecretValue(Secret secret, ICollection<(string Name, string Value)> ledger)
    {
        Register(ledger, secret.Name, secret.Value);
        return secret.Value;
    }

    /// <summary>
    /// Registers the forms an engine derived from one resolved secret (the base64 basic-auth credential) under the
    /// secret's name in <paramref name="ledger"/>, so what the app sees in place of the value is redacted wherever the
    /// value is. Registered before the engine hands a form to the app, since the resolve that computes them has not returned yet.
    /// </summary>
    public static void RegisterDerivedSecrets(string name, IEnumerable<string> derived, ICollection<(string Name, string Value)> ledger)
    {
        foreach (var value in derived.Where(value => value.Length > 0))
        {
            Register(ledger, name, value);
        }
    }

    private static void Register(ICollection<(string Name, string Value)> ledger, string name, string value)
    {
        if (!ledger.Contains((name, value)))
        {
            ledger.Add((name, value));
        }
    }
}
