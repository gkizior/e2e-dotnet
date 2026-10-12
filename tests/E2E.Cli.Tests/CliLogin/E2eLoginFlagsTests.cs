// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.OAuth;

namespace E2E.Cli.Tests.CliLogin;

/// <summary>
/// <c>e2e login</c> flags, from argv to the provider flow. The flow is a double registered in place of the built-in
/// provider, so no browser opens and no vendor is called. The port's CLI hands every flag to every provider's options,
/// so upstream's "drops a flag meant for another provider" is not ported.
/// </summary>
[Collection(Name)]
public sealed class E2eLoginFlagsTests : IDisposable
{
    public const string Name = "e2e login flags";

    private readonly string _configHome = Directory.CreateTempSubdirectory("e2e-login-").FullName;
    private readonly string? _xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly string? _credentials = Environment.GetEnvironmentVariable(CredentialStores.EnvironmentVariable);
    private readonly List<(string Id, OAuthLoginOptions Options)> _received = [];

    public E2eLoginFlagsTests()
    {
        // The file store under a throwaway config home: never the developer's own login file, nor the read-only environment store.
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _configHome);
        Environment.SetEnvironmentVariable(CredentialStores.EnvironmentVariable, "");
        OAuthProviders.Replacements["openai"] = new FlowDouble("openai", _received);
        OAuthProviders.Replacements["github-copilot"] = new FlowDouble("github-copilot", _received);
    }

    public void Dispose()
    {
        OAuthProviders.Replacements.Clear();
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _xdg);
        Environment.SetEnvironmentVariable(CredentialStores.EnvironmentVariable, _credentials);
        Directory.Delete(_configHome, recursive: true);
    }

    [Fact]
    public async Task Device_asks_the_ChatGPT_flow_for_its_device_code_method_and_stores_what_it_returned()
    {
        var (code, stdout, _) = await LoginAsync("openai", "--device");

        var (id, options) = Assert.Single(_received);
        Assert.Equal("openai", id);
        Assert.True(options.Device);
        Assert.Equal(0, code);
        Assert.Contains("openai double login stored.", stdout, StringComparison.Ordinal);
        Assert.Equal("token", Stored()?["openai"]?["access"]?.GetValue<string>());
    }

    [Fact]
    public async Task Client_id_names_the_OAuth_App_the_GitHub_Copilot_device_flow_runs_with()
    {
        var (code, _, _) = await LoginAsync("github-copilot", "--client-id", "Iv23_my_app");

        var (id, options) = Assert.Single(_received);
        Assert.Equal("github-copilot", id);
        Assert.Equal("Iv23_my_app", options.ClientId);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task From_gh_asks_the_GitHub_Copilot_flow_to_reuse_the_GitHub_CLI_token()
    {
        var (code, _, _) = await LoginAsync("github-copilot", "--from-gh");

        var (id, options) = Assert.Single(_received);
        Assert.Equal("github-copilot", id);
        Assert.True(options.FromGitHubCli);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Enterprise_url_names_the_GitHub_Enterprise_host()
    {
        var (code, _, _) = await LoginAsync("github-copilot", "--enterprise-url", "github.acme.com");

        var (id, options) = Assert.Single(_received);
        Assert.Equal("github-copilot", id);
        Assert.Equal("github.acme.com", options.EnterpriseUrl);
        Assert.Equal(0, code);
    }

    /// <summary>Partial: the CLI raises the flow's error, and <c>Main</c> turns it into the one error line and exit 1.</summary>
    [Fact]
    public async Task Fails_a_from_gh_login_with_no_signed_in_GitHub_CLI_with_the_flows_message_and_stores_nothing()
    {
        OAuthProviders.Replacements["github-copilot"] = new CopilotProvider(githubCliToken: (_, _) => Task.FromResult<string?>(null));

        var error = await Assert.ThrowsAsync<OAuthException>(() => LoginAsync("github-copilot", "--client-id", "Iv23_my_app", "--from-gh"));

        // With a client id and no --from-gh the flow would run GitHub's device flow, so the error proves the flag arrived.
        Assert.Equal(
            "GitHub Copilot login needs either the GitHub CLI signed in (gh auth login) or the client id of a GitHub OAuth App with the device flow enabled",
            error.Message);
        Assert.Null(Stored());
    }

    private static async Task<(int Code, string Stdout, string Stderr)> LoginAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await Program.RunAsync(["login", .. args], stdout, stderr, TextReader.Null, interactive: false, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    /// <summary>The logins the file store holds after a command, or null when it wrote none.</summary>
    private JsonNode? Stored()
    {
        var file = Path.Combine(_configHome, "e2e", "oauth.json");
        return File.Exists(file) ? JsonNode.Parse(File.ReadAllText(file)) : null;
    }

    /// <summary>A provider whose login records its options and hands back a non-expiring token.</summary>
    private sealed class FlowDouble(string id, List<(string Id, OAuthLoginOptions Options)> received) : IOAuthProvider
    {
        public string Id => id;

        public string Name => id + " double";

        public Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken)
        {
            received.Add((id, options));
            return Task.FromResult(new OAuthCredentials { Access = "token", Refresh = "", Expires = 0 });
        }

        public Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("a login is never refreshed here");

        public Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("models are never listed here");
    }
}

[CollectionDefinition(E2eLoginFlagsTests.Name, DisableParallelization = true)]
public sealed class E2eLoginFlagsCollection;
