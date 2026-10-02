// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E;

/// <summary>
/// A value the model must not see. Prompts, transcripts, and reports receive
/// <c>&lt;secret:name&gt;</c>. The runner fills the real value itself.
/// </summary>
public sealed class Secret
{
    private Secret(string name, string value, string? purpose)
    {
        Name = name;
        Value = value;
        Purpose = purpose;
    }

    public string Name { get; }

    public string? Purpose { get; }

    internal string Value { get; }

    public static Secret Create(string name, string value, string? purpose = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        return new Secret(name, value, purpose);
    }

    public override string ToString() => "<secret:" + Name + ">";
}

/// <summary>
/// A value that changes every run, such as a timestamp or a fresh email.
/// The replay cache treats every <see cref="UniqueValue"/> as the same param
/// and substitutes the current value when it types.
/// </summary>
public sealed class UniqueValue
{
    public UniqueValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

/// <summary>Wraps a fresh value so the replay cache can still match the step.</summary>
public static class Values
{
    public static UniqueValue Unique(string value) => new(value);
}

/// <summary>Username plus a <see cref="Secret"/> password loaded from the environment.</summary>
public sealed class UserCredential
{
    public UserCredential(string username, Secret password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(password);
        Username = username;
        Password = password;
    }

    public string Username { get; }

    public Secret Password { get; }
}

/// <summary>
/// Reads <c>E2E_USER_{NAME}_USERNAME</c> and <c>E2E_USER_{NAME}_PASSWORD</c>.
/// The name is uppercased and dashes become underscores.
/// </summary>
public static class Credentials
{
    public static UserCredential User(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var key = name.ToUpperInvariant().Replace('-', '_');
        var username = Environment.GetEnvironmentVariable("E2E_USER_" + key + "_USERNAME");
        var password = Environment.GetEnvironmentVariable("E2E_USER_" + key + "_PASSWORD");
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            throw new TestException(
                "AUTH_CREDENTIAL_UNAVAILABLE",
                "Credential '" + name + "' is not configured. Set E2E_USER_" + key + "_USERNAME and E2E_USER_" + key + "_PASSWORD.");
        }

        return new UserCredential(username, Secret.Create("password", password, name + " password"));
    }
}
