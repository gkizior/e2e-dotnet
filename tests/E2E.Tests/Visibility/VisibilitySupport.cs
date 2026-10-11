// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Visibility;

/// <summary>One observation of a page through the engine, frames included, as the tree walk tests read it.</summary>
internal static class VisibilitySupport
{
    /// <summary>Opens <paramref name="html"/> in an engine session and hands the session to <paramref name="body"/>, with every frame loaded.</summary>
    public static async Task<T> WithSessionAsync<T>(string html, Func<IEngineSession, Task<T>> body)
    {
        using var site = await TinySite.StartAsync(html);
        await using var session = await new WebEngine(headless: true).StartAsync(new EngineStartOptions(), CancellationToken.None);
        await session.OpenAsync(site.Url, CancellationToken.None);
        var page = WebEngine.SurfaceOf(session)!.Page();
        await Task.WhenAll(page.Frames.Skip(1).Select(frame => frame.WaitForLoadStateAsync()));
        return await body(session);
    }

    /// <summary>Every node of one observation of <paramref name="html"/>, in document order.</summary>
    public static Task<IReadOnlyList<SemanticNode>> ObserveAsync(string html) =>
        WithSessionAsync(html, async session => Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).ToList() as IReadOnlyList<SemanticNode>);

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
