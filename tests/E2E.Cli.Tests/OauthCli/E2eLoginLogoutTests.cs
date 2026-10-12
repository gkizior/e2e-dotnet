// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Tests.CliLogin;
using E2E.OAuth;

namespace E2E.Cli.Tests.OauthCli;

/// <summary>
/// <c>e2e login</c> and <c>e2e logout</c> with no terminal. The tests name their logins through the environment store, never
/// the developer's own credential file. The command raises its error and <c>Main</c> prints it as one line and exits 1, so
/// these tests are partial: they read the raised error, not the printed line or the exit code.
/// </summary>
[Collection(E2eLoginFlagsTests.Name)]
public sealed class E2eLoginLogoutTests : IDisposable
{
    private readonly string? _credentials = Environment.GetEnvironmentVariable(CredentialStores.EnvironmentVariable);

    public E2eLoginLogoutTests()
    {
        Environment.SetEnvironmentVariable(CredentialStores.EnvironmentVariable, "{}");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(CredentialStores.EnvironmentVariable, _credentials);
    }

    [Fact]
    public async Task Needs_a_named_provider_naming_the_providers_when_there_is_no_terminal_to_pick_in()
    {
        var error = await Assert.ThrowsAsync<OAuthException>(() => Run("login"));

        Assert.Equal(OAuthException.Misconfigured, error.Code);
        Assert.Equal("name a provider: openai, github-copilot, opencode-console, spacexai", error.Message);
    }

    [Fact]
    public async Task Turns_an_unknown_provider_into_one_error_naming_the_providers_not_a_stack_trace()
    {
        var error = await Assert.ThrowsAsync<OAuthException>(() => Run("logout", "nope"));

        Assert.Equal(OAuthException.Misconfigured, error.Code);
        Assert.Contains("openai, github-copilot, opencode-console, spacexai", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain('\n', error.Message);
    }

    private static Task<int> Run(params string[] args) =>
        Program.RunAsync(args, new StringWriter(), new StringWriter(), TextReader.Null, interactive: false, CancellationToken.None);
}
