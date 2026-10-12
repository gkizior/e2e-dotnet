// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using E2E.Engine;

namespace E2E.Tests.AgentAct;

/// <summary>
/// The home page of upstream's <c>tests/helpers/fixture-app.ts</c> that the <c>agent.act</c> tests use: an
/// Increment button, the counter it drives, and an Email and a Focus target field.
/// </summary>
internal static class CounterPage
{
    public static DocumentWorld World()
    {
        return new DocumentWorld().Map("/", page =>
        {
            var count = 0;
            var counter = page.Status("Counter");
            counter.Text = "0";
            page.Button("Increment", () => counter.Text = (++count).ToString(CultureInfo.InvariantCulture));
            page.Textbox("Email");
            page.Textbox("Focus target");
        });
    }

    /// <summary>The text of every tool result the model has been sent so far.</summary>
    public static IReadOnlyList<string> ToolResults(ScriptedModel model)
    {
        return model.Requests[^1].Messages.Where(message => message.Role == "tool").Select(message => message.Content ?? "").ToList();
    }

    /// <summary>The system prompt and every message the model has been sent, as one text.</summary>
    public static string Everything(ScriptedModel model)
    {
        var last = model.Requests[^1];
        return last.System + "\n" + string.Join('\n', last.Messages.Select(message => message.Content));
    }

    /// <summary>A model turn that issues several tool calls at once.</summary>
    public static ModelResponse Batch(params ModelResponse[] calls)
    {
        return new ModelResponse
        {
            ToolCalls = calls.Select((call, index) => new ModelToolCall
            {
                Id = "call_" + index.ToString(CultureInfo.InvariantCulture),
                Name = call.ToolCalls[0].Name,
                Arguments = call.ToolCalls[0].Arguments,
            }).ToList(),
        };
    }
}
