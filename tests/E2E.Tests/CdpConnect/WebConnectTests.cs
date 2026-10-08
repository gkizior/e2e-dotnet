// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.CdpConnect;

/// <summary>
/// What <c>connect.reconnectEndpoint</c> declares and refuses. The recovery
/// itself, against a live Chrome, is in <see cref="CdpRecovery.CdpSessionRecoveryTests"/>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class WebConnectTests
{
    [Fact]
    public async Task Declares_persistent_recovery_without_context_replacement_capabilities_and_names_why_a_reset_is_refused()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var engine = new WebEngine(new WebEngineOptions
        {
            Connect = new WebConnectOptions { CdpEndpoint = _ => Task.FromResult(remote.Endpoint), ReconnectEndpoint = _ => Task.FromResult(remote.Endpoint) },
        });
        await using var session = await engine.StartAsync(new EngineStartOptions { BaseUrl = app.Url }, CancellationToken.None);
        var error = await Assert.ThrowsAsync<EngineException>(() => session.ClearStateAsync(CancellationToken.None));
        Assert.Equal("UNSUPPORTED_CAPABILITY", error.Code);
        Assert.Equal("app.clearState() is unavailable with connect.reconnectEndpoint: it replaces the browser context, and the attempt rides one persistent context", error.Message);
        await session.RestartAsync(CancellationToken.None);
    }

    [Fact]
    public void Rejects_creation_time_credentials_headers_user_agent_locale_and_time_zone_with_persistent_recovery()
    {
        var connect = new WebConnectOptions { CdpEndpoint = _ => Task.FromResult("ws://localhost:0"), ReconnectEndpoint = _ => Task.FromResult("ws://localhost:0") };
        Assert.Contains("persistent context", Refused(new WebEngineOptions { Connect = connect, Headers = new Dictionary<string, string> { ["x-preview"] = "synthetic" } }), StringComparison.Ordinal);
        Assert.Contains("persistent context", Refused(new WebEngineOptions { Connect = connect, BasicAuth = new WebBasicAuth("user", "synthetic") }), StringComparison.Ordinal);
        Assert.Contains("persistent context", Refused(new WebEngineOptions { Connect = connect, UserAgent = "synthetic playwright" }), StringComparison.Ordinal);
        Assert.Contains("persistent context; locale requires", Refused(new WebEngineOptions { Connect = connect, Locale = "de-DE" }), StringComparison.Ordinal);
        Assert.Contains("persistent context; timezoneId requires", Refused(new WebEngineOptions { Connect = connect, TimezoneId = "Europe/Berlin" }), StringComparison.Ordinal);
    }

    private static string Refused(WebEngineOptions options)
    {
        var error = Assert.Throws<EngineException>(() => new WebEngine(options));
        Assert.Equal("INVALID_CONFIG", error.Code);
        return error.Message;
    }
}
