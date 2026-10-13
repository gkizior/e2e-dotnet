// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.TraceRecorder;

/// <summary>
/// Records a step through an act and reads the stored entry back. Upstream drives <c>TraceRecorder</c> and
/// <c>describeTarget</c> directly. The port records inside the agent.
/// </summary>
internal static class RecordedEntries
{
    internal static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static CacheEntry ReadEntry(string directory) =>
        Assert.IsType<CacheEntry>(new FileStepCache(directory).Read(Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.json")))).Entry);

    internal static string RawEntry(string directory) =>
        JsonNode.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json"))))!.ToJsonString(Relaxed);

    internal static DocumentWorld SettingsWorld(string? greeting = null) => new DocumentWorld().Map("/settings", page => page.Heading("Settings")).Map("/billing", page =>
    {
        var status = page.Status("Idle");
        page.Roots.Add(new DocumentElement { Role = "button", Name = "Upgrade", Text = "Upgrade", TestId = "upgrade-cta", OnTap = () => status.Name = status.Text = "Upgraded" });
        page.Roots.Add(new DocumentElement { Role = "textbox", Name = "Plan", Value = "s3cr3t", OnTap = () => status.Name = status.Text = "Plan tapped" });
        page.Textbox("Password", secure: true);
        if (greeting is not null)
        {
            page.Button(greeting, () => status.Name = status.Text = "Greeted");
        }
    });

    internal static async Task<string> RecordAsync(DocumentWorld world, IReadOnlyDictionary<string, object?>? parameters, params ModelResponse[] steps) =>
        await RecordAsync(world, parameters, null, steps);

    internal static async Task<string> RecordAsync(DocumentWorld world, IReadOnlyDictionary<string, object?>? parameters, IReadOnlyDictionary<string, AgentTool>? tools, params ModelResponse[] steps)
    {
        var directory = TempCache();
        var next = 0;
        var model = new ScriptedModel(_ => next < steps.Length ? steps[next++] : ModelResponses.Done("passed", "Done."));
        var attempt = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings");
                await ctx.Agent.ActAsync("work the billing page", new ActOptions { Params = parameters });
                await Expect.That(ctx.Screen.GetByRole("status")).ToBeVisibleAsync();
            },
            world,
            model,
            directory,
            agentTools: tools);
        Assert.Null(attempt.Error);
        return directory;
    }
}
