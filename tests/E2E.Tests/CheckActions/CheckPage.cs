// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.CheckActions;

/// <summary>A page served on a loopback port and a web engine session on it, shared by the check action tests.</summary>
internal static class CheckPage
{
    public const string CountClicks = """<script>window.clicks = 0; document.addEventListener("click", () => window.clicks++, true)</script>""";

    public static async Task RunAsync(string body, Func<IEngineSession, Task> test)
    {
        using var site = await TinySite.StartAsync("<!DOCTYPE html><html><body>" + body + "</body></html>");
        await using var session = await StartAsync();
        await session.OpenAsync(site.Url, CancellationToken.None);
        await test(session);
    }

    /// <summary>Opens <paramref name="body"/> in a test session, whose screen locators act on it.</summary>
    public static async Task RunWithScreenAsync(string body, Func<E2ESession, Task> test)
    {
        using var site = await TinySite.StartAsync("<!DOCTYPE html><html><body>" + body + "</body></html>");
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")),
            BaseUrl = site.Url,
            TestTitle = "check actions > through the locator target",
            ActionTimeout = TimeSpan.FromSeconds(2),
            AssertionTimeout = TimeSpan.FromSeconds(5),
        });
        await session.App.OpenAsync("/");
        await test(session);
        session.Complete(null);
    }

    public static Task<IEngineSession> StartAsync()
    {
        return new WebEngine(headless: true).StartAsync(new EngineStartOptions { ActionTimeout = TimeSpan.FromSeconds(2) }, CancellationToken.None);
    }

    public static async Task<SemanticNode> FindAsync(IEngineSession session, string role, string name)
    {
        var observation = await session.ObserveAsync(CancellationToken.None);
        return Flatten(observation.Roots).First(node => node.Role == role && node.Name == name);
    }

    public static async Task<T> EvaluateAsync<T>(IEngineSession session, string expression)
    {
        var result = await ((IBrowserSession)session).EvaluateAsync(expression, null, hasArg: false, CancellationToken.None);
        return System.Text.Json.JsonSerializer.Deserialize<T>(result!.Value)!;
    }

    public static IEnumerable<SemanticNode> Flatten(IEnumerable<SemanticNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
