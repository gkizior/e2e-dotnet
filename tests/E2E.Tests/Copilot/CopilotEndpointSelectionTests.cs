// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.Copilot;

public sealed class CopilotEndpointSelectionTests
{
    private static string Entry(string[] supported, string? policy = null) =>
        "{ \"id\": \"m\"" + (policy is null ? "" : ", \"policy\": { \"state\": \"" + policy + "\" }")
        + ", \"supported_endpoints\": [" + string.Join(", ", supported.Select(endpoint => "\"" + endpoint + "\"")) + "] }";

    private static HttpClient Listing(string data, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new FakeApi(_ => (status, "{ \"data\": " + data + " }")));

    private static Task<string?> ProtocolForAsync(HttpClient http, CancellationToken cancellationToken = default) =>
        CopilotProvider.ProtocolForAsync("m", http, cancellationToken);

    [Fact]
    public async Task Reads_the_endpoint_from_the_entry_alone_whatever_the_plan_policy_says()
    {
        Assert.Equal("responses", await ProtocolForAsync(Listing("[" + Entry(["/responses", "ws:/responses"]) + "]")));
        Assert.Equal("responses", await ProtocolForAsync(Listing("[" + Entry(["/responses"], "enabled") + "]")));
        Assert.Equal("responses", await ProtocolForAsync(Listing("[" + Entry(["/responses"], "disabled") + "]")));
    }

    [Fact]
    public async Task Keeps_chat_for_a_model_chat_completions_serves_one_with_no_endpoints_and_one_the_listing_cannot_place()
    {
        var cases = new Dictionary<string, string>
        {
            ["chat named"] = "[" + Entry(["/chat/completions", "/v1/messages"]) + "]",
            ["chat named beside responses"] = "[" + Entry(["/responses", "/chat/completions"]) + "]",
            ["no supported_endpoints"] = """[{ "id": "m" }]""",
            ["another endpoint only"] = "[" + Entry(["/v1/messages"]) + "]",
            ["not in the listing"] = """[{ "id": "other", "supported_endpoints": ["/responses"] }]""",
        };

        foreach (var (name, data) in cases)
        {
            Assert.True("chat" == await ProtocolForAsync(Listing(data)), name);
        }
    }

    [Fact]
    public async Task Answers_undefined_when_the_listing_cannot_be_read_so_nothing_is_remembered()
    {
        Assert.Null(await ProtocolForAsync(Listing("[" + Entry(["/responses"]) + "]", HttpStatusCode.InternalServerError)));
        Assert.Null(await ProtocolForAsync(new HttpClient(new Replying((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("not json") })))));
        Assert.Null(await ProtocolForAsync(new HttpClient(new Replying((_, _) => throw new HttpRequestException("offline")))));

        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        Assert.Null(await ProtocolForAsync(new HttpClient(new Replying((_, token) => new TaskCompletionSource<HttpResponseMessage>().Task.WaitAsync(token))), aborted.Token));
    }

    [Fact]
    public async Task Asks_the_listing_again_after_it_could_not_be_read_then_remembers_the_answer()
    {
        var listings = 0;
        var api = new FakeApi(request =>
        {
            if (request.Uri.AbsolutePath == "/models")
            {
                return ++listings == 1
                    ? (HttpStatusCode.ServiceUnavailable, """{ "message": "busy" }""")
                    : (HttpStatusCode.OK, "{ \"data\": [" + Entry(["/responses"]) + "] }");
            }

            return request.Uri.AbsolutePath == "/chat/completions"
                ? (HttpStatusCode.BadRequest, """{ "error": { "message": "model \"m\" is not accessible via the /chat/completions endpoint", "code": "unsupported_api_for_model" } }""")
                : (HttpStatusCode.OK, """
                    { "id": "resp_1", "created_at": 1, "model": "m",
                      "output": [{ "type": "message", "id": "msg_1", "role": "assistant", "content": [{ "type": "output_text", "text": "ok", "annotations": [] }] }],
                      "usage": { "input_tokens": 1, "output_tokens": 1, "total_tokens": 2 } }
                    """);
        });
        var store = new MemoryCredentialStore(new() { ["github-copilot"] = new OAuthCredentials { Access = "gho_x", Refresh = "", Expires = 0 } });
        using var model = new CopilotModel("m", Subscriptions.Client(new CopilotProvider(), store, inner: api));

        var failure = await Assert.ThrowsAsync<AgentException>(() => model.CompleteAsync(Request(), CancellationToken.None));
        Assert.Contains("not accessible", failure.Message, StringComparison.Ordinal);
        Assert.Equal("ok", (await model.CompleteAsync(Request(), CancellationToken.None)).Content);
        Assert.Equal("ok", (await model.CompleteAsync(Request(), CancellationToken.None)).Content);

        Assert.Equal(["/models", "/chat/completions", "/models", "/responses", "/responses"], api.Requests.Select(request => request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task Lets_one_caller_give_up_on_the_listing_without_failing_the_others_waiting_on_it()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listings = 0;
        var api = new Replying(async (request, token) =>
        {
            if (request.RequestUri!.AbsolutePath == "/models")
            {
                Interlocked.Increment(ref listings);
                listing.TrySetResult();
                await release.Task.WaitAsync(token);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{ "data": [{ "id": "m", "supported_endpoints": ["/chat/completions"] }] }""") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "id": "c", "object": "chat.completion", "created": 1, "model": "m", "choices": [{ "index": 0, "message": { "role": "assistant", "content": "ok" }, "finish_reason": "stop" }] }"""),
            };
        });
        var store = new MemoryCredentialStore(new() { ["github-copilot"] = new OAuthCredentials { Access = "gho_x", Refresh = "", Expires = 0 } });
        using var model = new CopilotModel("m", Subscriptions.Client(new CopilotProvider(), store, inner: api));
        using var quitter = new CancellationTokenSource();

        var first = model.CompleteAsync(Request(), quitter.Token);
        var second = model.CompleteAsync(Request(), CancellationToken.None);
        await listing.Task;
        await quitter.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        release.SetResult();

        Assert.Equal("ok", (await second).Content);
        Assert.Equal(1, listings);
    }

    private static ModelRequest Request() => new() { System = "", Tools = [], Messages = [new ModelMessage { Role = "user", Content = "x" }] };

    /// <summary>A handler that answers each request as its callback says.</summary>
    private sealed class Replying(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return reply(request, cancellationToken);
        }
    }
}
