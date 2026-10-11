// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;

namespace E2E.Tests.ObservationMetadata;

/// <summary>
/// An application can replace what the page script might read to serialize an observation: its own
/// <c>JSON.stringify</c>, or a <c>toJSON</c> on <c>Object</c> or <c>Array</c>. The observation and the refs
/// it hands out must not depend on any of them.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ObservationMetadataTests
{
    private const string Page = """
        <!doctype html><button onclick="this.textContent = 'Clicked'">Preserved "quoted" name</button>
        <input type="password" aria-label="Password" value="withheld-secret">
        """;

    [Theory]
    [InlineData("""JSON.stringify = () => '{"nodes":[],"ids":[],"truncated":false}';""")]
    [InlineData("JSON.stringify = (() => 'not JSON').bind(null);")]
    [InlineData("Object.defineProperty(Object.prototype, 'toJSON', { value: () => 'not metadata', configurable: true });")]
    [InlineData("Object.defineProperty(Array.prototype, 'toJSON', { value: () => 'not metadata', configurable: true });")]
    [InlineData("Function.prototype.toString = () => { throw new Error('disabled'); };")]
    public async Task Captures_live_refs_when_the_page_overrides(string script)
    {
        using var site = await TinySite.StartAsync(Page);
        await using var session = await new WebEngine(headless: true).StartAsync(new EngineStartOptions(), CancellationToken.None);
        await session.OpenAsync(site.Url, CancellationToken.None);
        await WebEngine.SurfaceOf(session)!.Page().EvaluateAsync($"() => {{ {script} }}");

        var observation = await session.ObserveAsync(CancellationToken.None);

        Assert.Equal(2, observation.Roots.Count);
        Assert.False(observation.Truncated);
        var button = observation.Roots[0];
        Assert.Equal("button", button.Role);
        Assert.Equal("Preserved \"quoted\" name", button.Name);
        Assert.True(observation.Roots[1].States.Secure);
        Assert.Equal("Password", observation.Roots[1].Name);
        Assert.DoesNotContain("withheld-secret", JsonSerializer.Serialize(observation.Roots), StringComparison.Ordinal);
        await session.PerformAsync(button, new LocatorAction.Tap(), CancellationToken.None);
        Assert.Equal("Clicked", Assert.Single((await session.ObserveAsync(CancellationToken.None)).Roots, node => node.Role == "button").Name);
    }
}
