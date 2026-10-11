// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Tests.ModelOverflow;

// Upstream reads an error's own fields; the port reads an exception's message and its inner exceptions, and takes
// the HTTP status and the response body as arguments. It has no retry chain to look through, so the spent-retry
// half of "looks through wrappers" has no port, and no untyped value to reject beyond null.
public sealed class IsContextOverflowTests
{
    private const string Overflow = "prompt is too long: 250000 tokens > 200000 maximum";

    [Theory]
    [InlineData("prompt is too long: 213462 tokens > 200000 maximum")]
    [InlineData("""413 {"error":{"type":"request_too_large","message":"Request exceeds the maximum size"}}""")]
    [InlineData("Your input exceeds the context window of this model")]
    [InlineData("Requested token count exceeds the model's maximum context length of 131072 tokens")]
    [InlineData("Input length (265330) exceeds model's maximum context length (262144).")]
    [InlineData("The input token count (1196265) exceeds the maximum number of tokens allowed (1048575)")]
    [InlineData("This model's maximum prompt length is 131072 but the request contains 537812 tokens")]
    [InlineData("Please reduce the length of the messages or completion")]
    [InlineData("This endpoint's maximum context length is 128000 tokens. However, you requested about 140000 tokens")]
    [InlineData("Input length 300000 exceeds the maximum allowed input length of 262144 tokens.")]
    [InlineData("The input (300000 tokens) is longer than the model's context length (262144 tokens).")]
    [InlineData("the request exceeds the available context size, try increasing it")]
    [InlineData("prompt token count of 200000 exceeds the limit of 128000")]
    [InlineData("Prompt contains 300000 tokens and 0 draft tokens, too large for model with 262144 maximum context length")]
    [InlineData("context_length_exceeded")]
    [InlineData("model_context_window_exceeded")]
    [InlineData("Range of input length should be [1, 129024]")]
    public void Recognizes(string message)
    {
        Assert.True(ContextOverflow.Describes(new Exception(message)));
    }

    [Theory]
    [InlineData("Rate limit reached for requests")]
    [InlineData("Too many requests, please retry later")]
    [InlineData("ThrottlingException: Too many tokens, please wait before trying again.")]
    [InlineData("Internal server error")]
    [InlineData("invalid_api_key")]
    [InlineData("")]
    public void Does_not_mistake_a_failure_for_an_overflow(string message)
    {
        Assert.False(ContextOverflow.Describes(new Exception(message)));
    }

    [Fact]
    public void Reads_the_HTTP_status_and_the_response_body_when_the_message_says_nothing()
    {
        Assert.True(ContextOverflow.Describes(413, "Bad Request", null));
        Assert.False(ContextOverflow.Describes(400, "Bad Request", null));
        Assert.True(ContextOverflow.Describes(400, "Bad Request", """{"error":{"message":"prompt is too long: 250000 tokens > 200000 maximum"}}"""));
    }

    [Fact]
    public void Vetoes_per_field_a_throttling_message_does_not_hide_an_overflow_in_the_body_and_413_stands_alone()
    {
        Assert.True(ContextOverflow.Describes(400, "Rate limit reached", """{"error":{"message":"prompt is too long: 250000 tokens > 200000 maximum"}}"""));
        Assert.True(ContextOverflow.Describes(413, "Too many requests", null));
        Assert.True(ContextOverflow.Describes(null, "prompt is too long", "ThrottlingException: Too many tokens"));
    }

    [Fact]
    public void Looks_through_a_cause_chain()
    {
        Assert.True(ContextOverflow.Describes(new Exception("gateway failed", new Exception(Overflow))));
        Assert.False(ContextOverflow.Describes(new Exception("outer", new Exception("middle", new Exception("rate limit")))));
    }

    [Fact]
    public void Rejects_a_null_error()
    {
        Assert.False(ContextOverflow.Describes(null));
    }
}
