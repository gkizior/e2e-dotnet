// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.ProviderFailureMessage;

// Upstream's retry chain and parsed-body cases have no port: the port has no AI SDK, so no error body is
// parsed, and no transport retries. Its ai-trace case has no port either: there is no --ai-trace.
public sealed class ProviderFailureMessageTests
{
    private const string Detail = """{"detail":"Unsupported service_tier: fast"}""";

    /// <summary>Answers every request with <c>status</c> and <c>body</c> and an empty status text, as HTTP/2 has none.</summary>
    private sealed class RejectingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(status)
            {
                ReasonPhrase = "",
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static OpenAiCompatibleModel Rejecting(HttpClient http)
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "fake-model", ApiKey = "key", BaseUrl = "https://provider.test/v1" }, http);
    }

    private static async Task<string> FailureOf(HttpStatusCode status, string body, Redactor? redactor = null)
    {
        using var http = new HttpClient(new RejectingHandler(status, body));
        using var model = Rejecting(http);
        var request = new ModelRequest { System = "rules", Messages = [new ModelMessage { Role = "user", Content = "hi" }], Tools = [], Redactor = redactor ?? Redactor.None };
        var error = await Assert.ThrowsAsync<AgentException>(() => model.CompleteAsync(request, CancellationToken.None));
        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        return error.Message;
    }

    [Fact]
    public async Task Quotes_the_status_and_the_body_the_SDK_could_not_parse()
    {
        Assert.Equal("The model provider failed: HTTP 400: " + Detail, await FailureOf(HttpStatusCode.BadRequest, Detail));
        Assert.Equal("The model provider failed: HTTP 502", await FailureOf(HttpStatusCode.BadGateway, ""));
    }

    [Fact]
    public async Task Cuts_the_body_at_1_KB_without_splitting_a_character()
    {
        var message = await FailureOf(HttpStatusCode.BadRequest, new string('é', 600));

        Assert.Equal("The model provider failed: HTTP 400: " + new string('é', 512) + "…", message);
    }

    [Fact]
    public async Task Redacts_secrets_in_the_body_before_cutting_it_short()
    {
        var redactor = Redactor.For([("password", "hunter2")]);

        Assert.Equal(
            """The model provider failed: HTTP 400: {"detail":"bad field: <secret:password>"}""",
            await FailureOf(HttpStatusCode.BadRequest, """{"detail":"bad field: hunter2"}""", redactor));

        // The secret straddles the cut: redacted whole first, no prefix of it survives.
        var straddling = await FailureOf(HttpStatusCode.InternalServerError, new string('x', 1020) + "hunter2" + new string('y', 100), redactor);
        Assert.Equal("The model provider failed: HTTP 500: " + new string('x', 1020) + "<sec…", straddling);
        Assert.DoesNotContain("hun", straddling, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Names_the_status_and_the_body_in_the_failed_agent_step()
    {
        var error = await Assert.ThrowsAsync<AgentException>(() => RunAsync(ctx => ctx.Agent.ActAsync("open the page")));

        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.Equal("The model provider failed: HTTP 400: " + Detail, error.Message);
    }

    [Fact]
    public async Task Names_the_status_and_the_body_in_a_failed_judgment()
    {
        var error = await Assert.ThrowsAsync<AgentException>(() => RunAsync(ctx => ctx.Agent.AssertAsync("the page is open")));

        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.Equal("The model provider failed: HTTP 400: " + Detail, error.Message);
    }

    private static async Task RunAsync(Func<TestContext, Task> step)
    {
        using var http = new HttpClient(new RejectingHandler(HttpStatusCode.BadRequest, Detail));
        using var model = Rejecting(http);
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            CacheEnabled = false,
            TestTitle = "provider failure message > case",
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });

        await session.Context.App.OpenAsync("/settings/billing");
        await step(session.Context);
        session.Complete(null);
    }
}
