// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.AgentPolicy;

public sealed class ASecretTheObservedScreenShowsTests
{
    [Fact]
    public async Task Reaches_no_observation_text_executor_tree_cache_entry_or_report_whole_or_as_the_part_a_cut_field_keeps()
    {
        var directory = TempCache();
        const string probe = "probeKqZrTmWxpLdsNvbHcjFgyQaeUoiRktYwzXnu";
        const string lead = "leadqzrtmwxplkdsnvbhcjfg";
        const string tail = "tailyqaeuoirktywzxnumbvcxzlkjhgfdsapoiuytrewqmnbvcxzlkj";
        const string multiline = lead + "\r\n\t" + tail;
        const string collapsed = lead + " " + tail;
        static DocumentWorld World() => new DocumentWorld().Map("/reveal", page =>
        {
            page.Roots.Add(new DocumentElement
            {
                Role = "button",
                Name = "Go " + collapsed,
                TestId = probe,
                OnTap = () => page.Roots.Add(new DocumentElement { Role = "status", Name = "Output " + collapsed, Text = "Output " + collapsed, TestId = probe }),
            });
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "revealed")
                : ModelResponses.Tap("button", "Go <secret:multiline>");
        });

        var results = new List<ActResult>();
        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/reveal");
            results.Add(await ctx.Agent.ActAsync("reveal the output", new ActOptions
            {
                Params = new Dictionary<string, object?>
                {
                    ["probe"] = Secret.Create("probe", probe),
                    ["multiline"] = Secret.Create("multiline", multiline),
                },
            }));
            await Expect.That(ctx.Screen.GetByRole("status")).ToBeVisibleAsync();
        }

        var recorded = await RunAsync(Body, World(), model, directory);
        Assert.Null(recorded.Error);

        // The redacted anchors still match the redacted screen, so the replay finishes on its own.
        var replayed = await RunAsync(Body, World(), new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")), directory);
        Assert.Equal("self-finalized", results[1].Cache?.Mode);
        Assert.Equal(1, results[1].Actions);

        // Read JSON unescaped, since the cache writes a marker's angle brackets as \u003C and \u003E.
        var relaxed = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var entries = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(file => JsonNode.Parse(File.ReadAllText(file))!.ToJsonString(relaxed))
            .ToList();
        Assert.Single(entries);
        Assert.Contains("<secret:multiline>", entries[0], StringComparison.Ordinal);

        // The observation text is every screen the model read, in its prompts and tool results.
        var seen = model.Requests.SelectMany(request => request.Messages.Select(message => message.Content).Prepend(request.System)).ToList();
        Assert.Contains("<secret:probe>", string.Join('\n', seen), StringComparison.Ordinal);

        // The port writes no report file; the act results and run outcomes are what it reports.
        var written = seen.Concat(entries).Append(JsonSerializer.Serialize(results, relaxed)).Append(JsonSerializer.Serialize(new[] { recorded, replayed }, relaxed));
        foreach (var fragment in new[] { probe, multiline, collapsed, lead[..12], tail[..8] })
        {
            foreach (var text in written)
            {
                Assert.DoesNotContain(fragment, text, StringComparison.Ordinal);
            }
        }
    }
}
