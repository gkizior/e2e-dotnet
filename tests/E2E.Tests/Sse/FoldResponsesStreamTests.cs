// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json.Nodes;

namespace E2E.Tests.Sse;

/// <summary>
/// The port folds the text of a response body, with no <c>Response</c> object, so a fold answers its body and an error
/// text, with no status and no content-type header. So these tests are partial where upstream asserts them, and the upstream
/// it "returns an untyped response with no events as it was, bytes included" is not ported: there is no response whose
/// bytes a fold could consume.
/// </summary>
public sealed class FoldResponsesStreamTests
{
    private static readonly JsonObject Completed = new()
    {
        ["id"] = "resp_1",
        ["object"] = "response",
        ["output"] = new JsonArray(new JsonObject
        {
            ["type"] = "message",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "hi" }),
        }),
        ["usage"] = new JsonObject { ["input_tokens"] = 1, ["output_tokens"] = 2 },
    };

    private static string Sse(params (string? Event, JsonNode Data)[] events)
    {
        var text = new StringBuilder();
        foreach (var (name, data) in events)
        {
            text.Append(name is null ? "" : "event: " + name + "\n").Append("data: ").Append(data.ToJsonString()).Append("\n\n");
        }

        return text.ToString();
    }

    private static JsonObject Event(string type, JsonNode? fields = null)
    {
        var record = new JsonObject { ["type"] = type };
        foreach (var (key, value) in fields?.AsObject() ?? [])
        {
            record[key] = value?.DeepClone();
        }

        return record;
    }

    [Fact]
    public void Returns_the_completed_response_object_as_JSON()
    {
        var (body, error) = ResponsesStream.Fold(Sse(
            ("response.created", Event("response.created", new JsonObject { ["response"] = new JsonObject { ["id"] = "resp_1" } })),
            ("response.output_text.delta", Event("response.output_text.delta", new JsonObject { ["delta"] = "hi" })),
            ("response.completed", Event("response.completed", new JsonObject { ["response"] = Completed.DeepClone() }))));

        Assert.Null(error);
        Assert.True(JsonNode.DeepEquals(Completed, JsonNode.Parse(body)));
    }

    /// <summary>Partial: the fold answers the error the response carries, with no status 400 and no error body.</summary>
    [Fact]
    public void Turns_a_failed_response_into_the_error_it_carries()
    {
        var failed = Event("response.failed", new JsonObject
        {
            ["response"] = new JsonObject { ["error"] = new JsonObject { ["code"] = "rate_limit_exceeded", ["message"] = "slow down" } },
        });

        var (_, error) = ResponsesStream.Fold(Sse((null, failed)));

        Assert.True(JsonNode.DeepEquals(new JsonObject { ["code"] = "rate_limit_exceeded", ["message"] = "slow down" }, JsonNode.Parse(error!)));
    }

    /// <summary>Partial: the fold answers an error, with no status 502.</summary>
    [Fact]
    public void Reports_a_stream_that_never_completes_as_an_error()
    {
        var (_, error) = ResponsesStream.Fold(Sse((null, Event("response.created"))) + "data: [DONE]\n\n");

        Assert.Equal("the stream ended without a completed response", error);
    }

    [Fact]
    public void Assembles_the_output_from_the_streamed_items_when_the_final_event_carries_none()
    {
        var message = new JsonObject
        {
            ["id"] = "msg_1",
            ["type"] = "message",
            ["status"] = "completed",
            ["role"] = "assistant",
            ["content"] = new JsonArray(new JsonObject { ["type"] = "output_text", ["text"] = "hi" }),
        };
        var call = new JsonObject { ["id"] = "fc_1", ["type"] = "function_call", ["call_id"] = "call_1", ["name"] = "report", ["arguments"] = "{}" };
        var withoutOutput = (JsonObject)Completed.DeepClone();
        withoutOutput["output"] = new JsonArray();

        var (body, error) = ResponsesStream.Fold(Sse(
            ("response.output_item.done", Event("response.output_item.done", new JsonObject { ["output_index"] = 1, ["item"] = call.DeepClone() })),
            ("response.output_item.done", Event("response.output_item.done", new JsonObject { ["output_index"] = 0, ["item"] = message.DeepClone() })),
            ("response.completed", Event("response.completed", new JsonObject { ["response"] = withoutOutput }))));

        var expected = (JsonObject)Completed.DeepClone();
        expected["output"] = new JsonArray(message.DeepClone(), call.DeepClone());
        Assert.Null(error);
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(body)));
    }

    /// <summary>Partial: a fold has no headers, so the test folds a stream the way a body without a content type reaches it, by its text alone.</summary>
    [Fact]
    public void Folds_a_stream_by_its_text_when_the_backend_sent_no_content_type_header()
    {
        var (body, error) = ResponsesStream.Fold(Sse(
            ("response.created", Event("response.created", new JsonObject { ["response"] = new JsonObject { ["id"] = "resp_1" } })),
            ("response.completed", Event("response.completed", new JsonObject { ["response"] = Completed.DeepClone() }))));

        Assert.Null(error);
        Assert.True(JsonNode.DeepEquals(Completed, JsonNode.Parse(body)));
    }

    /// <summary>Partial: the fold takes a body and has no status, so only the body that is not a stream is checked.</summary>
    [Fact]
    public void Passes_a_body_that_is_not_a_stream_through_untouched()
    {
        var (body, error) = ResponsesStream.Fold("""{"a":1}""");

        Assert.Equal("""{"a":1}""", body);
        Assert.Null(error);
    }

    /// <summary>Partial: the port reads the data of each event and has no event name to return.</summary>
    [Fact]
    public void Parses_CRLF_streams_and_multi_line_data()
    {
        Assert.Equal(["1\n2", """{"b":1}"""], ResponsesStream.Events("event: a\r\ndata: 1\r\ndata: 2\r\n\r\ndata: {\"b\":1}\r\n\r\n"));
    }
}
