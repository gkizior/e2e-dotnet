// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.OAuth;

/// <summary>
/// The contract between a subscription login and the model clients: a provider signs the user in, refreshes
/// what the login returned, and adjusts one API request the vendor's way. The credentials file, the handler that
/// swaps the token in, and the models are shared.
/// </summary>
public interface IOAuthProvider
{
    /// <summary>The id in the credentials file and on the command line: <c>openai</c>, <c>github-copilot</c>, <c>opencode-console</c>, <c>spacexai</c>.</summary>
    string Id { get; }

    /// <summary>The vendor's name for messages.</summary>
    string Name { get; }

    Task<OAuthCredentials> LoginAsync(OAuthLoginCallbacks callbacks, OAuthLoginOptions options, CancellationToken cancellationToken);

    /// <summary>Renews the credentials, or throws <see cref="OAuthException.LoginRequired"/> when the user has to sign in again.</summary>
    Task<OAuthCredentials> RefreshAsync(OAuthCredentials credentials, CancellationToken cancellationToken);

    /// <summary>
    /// Adjusts one API request before it goes out. It arrives with the bearer token and the user agent set; the
    /// provider rewrites the URL, adds vendor headers, or changes the body.
    /// </summary>
    Task PrepareAsync(HttpRequestMessage request, OAuthCredentials credentials, CancellationToken cancellationToken);

    /// <summary>The models the subscription serves, asked of the vendor through <paramref name="http"/>, which carries the login.</summary>
    Task<IReadOnlyList<SubscriptionModel>> ListModelsAsync(HttpClient http, CancellationToken cancellationToken);
}

/// <summary>How a login flow talks to the terminal, or whatever hosts it.</summary>
public sealed class OAuthLoginCallbacks
{
    /// <summary>The URL to open and how to finish there. Called once per flow.</summary>
    public required Action<OAuthAuthInfo> OnAuth { get; init; }

    /// <summary>Asks the user for text: the pasted code when the local callback never arrives.</summary>
    public required Func<string, CancellationToken, Task<string>> OnPrompt { get; init; }

    public Action<string>? OnProgress { get; init; }
}

/// <summary>Where the user is sent and, for device flows, the code to enter there.</summary>
public sealed record OAuthAuthInfo(string Url, string Instructions, string? UserCode = null);

/// <summary>The <c>e2e login</c> flags; each provider reads the ones meant for it.</summary>
public sealed class OAuthLoginOptions
{
    /// <summary>ChatGPT: print a code for another device instead of opening the browser.</summary>
    public bool Device { get; init; }

    /// <summary>GitHub Copilot: the client id of your GitHub OAuth App with the device flow enabled.</summary>
    public string? ClientId { get; init; }

    /// <summary>GitHub Copilot: reuse <c>gh auth token</c> even when a client id is given.</summary>
    public bool FromGitHubCli { get; init; }

    /// <summary>GitHub Copilot: <c>github.example.com</c> for GitHub Enterprise.</summary>
    public string? EnterpriseUrl { get; init; }
}

/// <summary>One model a subscription serves: the id a config passes, and how the vendor describes it.</summary>
public sealed record SubscriptionModel(string Id, string? Name = null, string? Detail = null);
