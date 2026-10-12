// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Tests.AgentAct;

namespace E2E.Tests.AgentTraceCache;

/// <summary>
/// The "increment the counter twice" step the upstream trace cache tests run through a two-tap executor. The scripted
/// model resumes after the actions a replay already ran, so it taps until the counter would read 2.
/// </summary>
internal static class TwoTapCounter
{
    public const string Instruction = "increment the counter twice";

    public static string NewDirectory() => Path.Combine(Path.GetTempPath(), "e2e-two-tap", Guid.NewGuid().ToString("n"));

    public static ScriptedModel Model()
    {
        return new ScriptedModel(request =>
        {
            var replayed = Replayed(request.Messages[0].Content!);
            var tapped = request.Messages.Count(message => message.Role == "tool" && message.Content!.Contains("tapped", StringComparison.Ordinal));
            return replayed + tapped >= 2 ? ModelResponses.Done("passed", "The counter shows 2.") : ModelResponses.Tap("button", "Increment");
        });
    }

    /// <summary>The actions the hand-off notice at the top of the first message lists as already run.</summary>
    public static int Replayed(string firstMessage)
    {
        const string marker = "after these actions:\n";
        var start = firstMessage.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return 0;
        }

        var end = firstMessage.IndexOf("Continue from", start, StringComparison.Ordinal);
        Assert.True(end > start, "the hand-off notice ends with 'Continue from'");
        var notice = firstMessage[(start + marker.Length)..end];
        return notice.Split('\n').Count(line => line.StartsWith("- ", StringComparison.Ordinal));
    }

    /// <summary>A model whose provider never answers: every request fails, as with a missing key or an outage.</summary>
    public static ScriptedModel UnreachableModel()
    {
        return new ScriptedModel(_ => throw new AgentException("MODEL_PROVIDER_FAILED", "401 Unauthorized: no API key was provided"));
    }

    public static Task<E2ESession> StartAsync(string directory, IAgentModel? model, IAgentModel? judge = null)
    {
        return E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(CounterPage.World()),
            Model = model,
            Judge = judge,
            BaseUrl = "https://counter.test",
            Cache = new FileStepCache(directory),
            CacheEnabled = true,
            CacheMode = CacheMode.ReadWrite,
            TestTitle = "cached step increments twice",
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            ReplayTimeout = TimeSpan.FromMilliseconds(300),
        });
    }

    /// <summary>Records the step, then makes the recorded second tap look for a button the page does not have.</summary>
    public static async Task RecordAndSabotageAsync(string directory)
    {
        await using (var session = await StartAsync(directory, Model()))
        {
            await RunAsync(session);
            session.Complete();
        }

        var cache = new FileStepCache(directory);
        var key = Path.GetFileNameWithoutExtension(Directory.GetFiles(directory).Single());
        var entry = cache.Read(key).Entry!;
        Assert.Equal(["tap", "tap"], entry.Actions.Select(action => action.Kind));
        entry.Actions[1].Name = "No Such Button";
        cache.Write(key, entry);
    }

    public static async Task<ActResult> RunAsync(E2ESession session)
    {
        await session.App.OpenAsync("/");
        var result = await session.Agent.ActAsync(Instruction);
        await Expect.That(session.Screen.GetByRole("status")).ToHaveTextAsync("2");
        return result;
    }

    public static CacheEntry OnlyEntry(string directory)
    {
        var key = Path.GetFileNameWithoutExtension(Directory.GetFiles(directory).Single());
        return new FileStepCache(directory).Read(key).Entry!;
    }

    /// <summary>The one entry file's bytes and write time, to prove a run left it alone.</summary>
    public static (string Text, DateTime Written) FileState(string directory)
    {
        var file = Directory.GetFiles(directory).Single();
        return (File.ReadAllText(file), File.GetLastWriteTimeUtc(file));
    }
}
