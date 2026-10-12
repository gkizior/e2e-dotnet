// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.FixtureRuntime;

/// <summary>
/// The port takes the C# type as the schema, with no Standard Schema and no <c>found</c>, <c>value</c>, and
/// <c>missing</c> envelope, and it records no steps. So the upstream it "asks for the same envelope in text mode, when
/// the caller schema has no JSON Schema projection" is not ported: every C# type has one.
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

    [Fact]
    public async Task Sends_the_caller_schema_as_a_shape_types_and_required_fields_no_value_constraints()
    {
        var model = new ScriptedModel(_ => ModelResponses.Call("extract", new { data = new { count = 3 } }));

        var (error, value) = await ExtractAsync<TodoShape>(model, "the number of todos");

        Assert.Null(error);
        Assert.Equal(3, value!.Count);
        var sent = model.Requests[0].Tools.Single(tool => tool.Name == "extract").Parameters.GetRawText();
        Assert.Contains("\"count\":{\"type\":\"integer\"}", sent, StringComparison.Ordinal);
        Assert.Contains("\"required\":[\"count\"]", sent, StringComparison.Ordinal);
        Assert.Contains("\"label\":{\"type\":\"string\"}", sent, StringComparison.Ordinal);
        foreach (var constraint in new[] { "minimum", "maximum", "minLength", "pattern", "$schema" })
        {
            Assert.DoesNotContain(constraint, sent, StringComparison.Ordinal);
        }
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

    private sealed class TodoShape
    {
        public required int Count { get; init; }

        public string Label { get; init; } = "";
    }

    private sealed class Phone
    {
        public required string Number { get; init; }
    }
}
