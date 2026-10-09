// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests;

/// <summary>
/// One plain Playwright page per test class, as upstream's <c>beforeAll</c> launches Chromium and opens a page
/// for a test file. The port's reader, <see cref="PageScript.Collect"/>, runs on this same page, so each check
/// compares it with what Playwright itself answers there.
/// </summary>
public sealed class ChromiumPage : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private int _nextRef = 1;

    public IPage Page { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await WebEngine.EnsureChromiumAsync(headed: false, CancellationToken.None);
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        Page = await _browser.NewPageAsync(new BrowserNewPageOptions { ViewportSize = new ViewportSize { Width = 1280, Height = 720 } });
        // The init script that records closed roots runs on a navigation; setContent alone is none.
        await Page.AddInitScriptAsync(PageScript.RecordClosedShadowRoots);
        await Page.GotoAsync("about:blank");
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    /// <summary>The port's reader over the page: every node it lists, in document order, keyed by its ref.</summary>
    public async Task<IReadOnlyList<ReadNode>> CaptureAsync()
    {
        // The next free ref carries over, as the engine's does, so a ref never names two elements.
        var json = await Page.EvaluateAsync<string>(PageScript.Collect, new { seed = _nextRef, max = ObservationLimits.Nodes, testIdAttribute = "data-testid" });
        using var document = JsonDocument.Parse(json);
        _nextRef = document.RootElement.GetProperty("next").GetInt32();
        var nodes = new List<ReadNode>();
        Flatten(document.RootElement.GetProperty("roots"), nodes);
        return nodes;
    }

    /// <summary>
    /// The reader's node for the element <paramref name="locator"/> names. The reader walks the document, so this
    /// reads it whole and picks the node by the ref the walk stamped on that element.
    /// </summary>
    public async Task<ReadNode?> ReadAsync(ILocator locator)
    {
        var nodes = await CaptureAsync();
        var reference = await locator.EvaluateAsync<string?>(PageScript.RefOf);
        return nodes.FirstOrDefault(node => node.Ref == reference);
    }

    private static void Flatten(JsonElement list, List<ReadNode> into)
    {
        foreach (var node in list.EnumerateArray())
        {
            var rect = node.GetProperty("rect");
            into.Add(new ReadNode(
                node.GetProperty("ref").GetString()!,
                StringOf(node, "role"),
                StringOf(node, "name"),
                StringOf(node, "text"),
                StringOf(node, "value"),
                StringOf(node, "testId"),
                node.GetProperty("hidden").GetBoolean(),
                node.GetProperty("selected").GetBoolean(),
                node.GetProperty("attributes").EnumerateObject().ToDictionary(attribute => attribute.Name, attribute => attribute.Value.GetString() ?? "", StringComparer.Ordinal),
                rect.ValueKind == JsonValueKind.Null
                    ? null
                    : new BoundingBox(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(), rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble())));
            Flatten(node.GetProperty("children"), into);
        }
    }

    private static string? StringOf(JsonElement node, string name)
    {
        return node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}

/// <summary>One node as the port's reader reports it.</summary>
public sealed record ReadNode(
    string Ref,
    string? Role,
    string? Name,
    string? Text,
    string? Value,
    string? TestId,
    bool Hidden,
    bool Selected,
    IReadOnlyDictionary<string, string> Attributes,
    BoundingBox? Rect)
{
    /// <summary>The node as an observation carries it, for what reads an observation (the snapshot).</summary>
    public SemanticNode ToSemanticNode() => new()
    {
        Ref = Ref,
        Role = Role,
        Name = Name,
        Text = Text,
        Value = Value,
        TestId = TestId,
        States = new NodeStates { Hidden = Hidden },
        Attributes = Attributes,
        Rect = Rect,
    };
}
