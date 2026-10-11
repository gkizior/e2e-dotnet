// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.AgentConfig;

// Upstream's retry case has no port: the port has no transport retries (see COMPATIBILITY.md, Models). The port has no
// CANCELLED code: an aborted attempt surfaces as the cancellation of its token.
public sealed class ModelErrorClassificationTests
{
    [Fact]
    public async Task Separates_an_aborted_attempt_from_an_elapsed_step_budget()
    {
        using var abort = new CancellationTokenSource();
        await using var session = await StartAsync(new HangingModel(), abort.Token);
        await session.App.OpenAsync("/settings/billing");

        // A slow provider is a test timeout; only an aborted attempt is a cancellation.
        var timedOut = await Assert.ThrowsAsync<AgentException>(() => session.Agent.ActAsync("upgrade", new ActOptions { Timeout = TimeSpan.FromSeconds(1) }));
        Assert.Equal("STEP_TIMEOUT", timedOut.Code);

        await abort.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.Agent.ActAsync("upgrade", new ActOptions { Timeout = TimeSpan.FromSeconds(30) }));
        session.Complete(timedOut);
    }

    [Fact]
    public async Task Does_not_retry_a_provider_failure_the_provider_called_permanent()
    {
        var handler = new CountingHandler(HttpStatusCode.BadRequest, "provider said no");
        using var http = new HttpClient(handler);
        using var model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "always-fails", ApiKey = "key", BaseUrl = "https://provider.test/v1" }, http);
        var request = new ModelRequest { System = "s", Messages = [new ModelMessage { Role = "user", Content = "p" }], Tools = [], Redactor = Redactor.None };

        var error = await Assert.ThrowsAsync<AgentException>(() => model.CompleteAsync(request, CancellationToken.None));

        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.Contains("provider said no", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Attempts);
    }

    private static Task<E2ESession> StartAsync(IAgentModel model, CancellationToken cancellationToken)
    {
        return E2ESession.StartAsync(
            new E2ESessionOptions
            {
                Engine = new DocumentEngine(BillingWorld.Create()),
                Model = model,
                BaseUrl = "https://billing.test",
                TestTitle = "model errors",
            },
            cancellationToken);
    }

    private sealed class HangingModel : IAgentModel
    {
        public string Name => "hanging";

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class CountingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") });
        }
    }
}
