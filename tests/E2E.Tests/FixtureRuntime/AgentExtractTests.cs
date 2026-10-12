// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.FixtureRuntime;

/// <summary>
/// The port takes the C# type as the schema, with no Standard Schema and no <c>found</c>, <c>value</c>, and
/// <c>missing</c> envelope, and it records no steps. So the upstream it "asks for the same envelope in text mode, when
/// the caller schema has no JSON Schema projection" is not ported: every C# type has one. "Sends the caller schema as a
/// shape: types and required fields, no value constraints" is not ported: the schema the port sends for a number allows
/// a string and carries a <c>pattern</c>, so the test would fail.
/// </summary>
public sealed class AgentExtractTests
{
    private static DocumentWorld Todos() => new DocumentWorld().Map("/", page =>
    {
        page.Paragraph("Buy milk");
        page.Paragraph("Walk the dog");
        page.Paragraph("Ship it");
    });

    private static async Task<(Exception? Error, T? Value)> ExtractAsync<T>(ScriptedModel model, string instruction)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(Todos()),
            Model = model,
            BaseUrl = "https://todos.test",
            TestTitle = "extract",
            CacheEnabled = false,
        });
        await session.App.OpenAsync("/");
        try
        {
            return (null, await session.Agent.ExtractAsync<T>(instruction));
        }
        catch (E2EException ex)
        {
            return (ex, default);
        }
    }

    [Fact]
    public async Task Fails_inconclusive_naming_what_was_missing_when_the_screen_does_not_show_the_data()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("failed", "no phone number is on this screen", "ASSERTION_INCONCLUSIVE"));

        var (error, _) = await ExtractAsync<Phone>(model, "the customer phone number");

        var failure = Assert.IsType<AgentException>(error);
        Assert.Equal("ASSERTION_INCONCLUSIVE", failure.Code);
        Assert.Contains("no phone number is on this screen", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, model.CallCount);
    }

    /// <summary>Partial: the data the first answer gets wrong is a count that is text, where upstream's schema also bounds it to 100 or more.</summary>
    [Fact]
    public async Task Lets_the_model_decline_on_the_repair_round_instead_of_bending_the_value_to_the_type()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("<previous-attempt-rejected>", StringComparison.Ordinal)
                ? ModelResponses.Done("failed", "the list shows 3 todos, not a number of them in words", "ASSERTION_INCONCLUSIVE")
                : ModelResponses.Call("extract", new { data = new { count = "three" } });
        });

        var (error, _) = await ExtractAsync<TodoCount>(model, "the number of todos");

        Assert.Equal("ASSERTION_INCONCLUSIVE", Assert.IsType<AgentException>(error).Code);
        Assert.Equal(2, model.CallCount);
        Assert.Contains("could not be converted", string.Join('\n', model.Requests[1].Messages.Select(message => message.Content)), StringComparison.Ordinal);
    }

    private sealed class TodoCount
    {
        public required int Count { get; init; }
    }

    private sealed class Phone
    {
        public required string Number { get; init; }
    }
}
