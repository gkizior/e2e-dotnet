// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.CoreTests;

namespace E2E.Tests.PlainStringSecrets;

/// <summary>
/// Ported from the second <c>describe</c> of upstream's <c>plain-string-secrets.test.ts</c>. Upstream registers
/// the secret in the config, so it is known before any step. The port registers a secret when an <c>act</c>
/// receives it in its params, so the secret is handed to the act as a <see cref="Secret"/>, and the same value
/// spelled as a plain string in the instruction, a param, and the agent context is what must stay out of the
/// model's input. The first <c>describe</c> (the runner, executor, report, and reporter files) is not ported,
/// because the port has none of them.
/// </summary>
public sealed class ASecretValuePassedAsAPlainStringToTheBuiltInAgentTests
{
    private const string Value = "plain-Zr8Kq2Wm5Tx9";

    private const string Marker = "<secret:probe>";

    [Fact]
    public async Task Sends_the_act_loop_and_the_judgment_the_redacted_instruction_params_and_context_of_a_secret_the_act_received()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("Statement:", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "nothing secret is shown")
                : ModelResponses.Done("passed", "inspected");
        });
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            AgentContext = "context " + Value,
            CacheEnabled = false,
            TestTitle = "built-in agent",
        });
        await session.App.OpenAsync("/settings/billing");
        await session.Agent.ActAsync("inspect " + Value + " with {note}", new ActOptions
        {
            Params = new Dictionary<string, object?>
            {
                ["probe"] = Secret.Create("probe", Value),
                ["note"] = Value,
            },
        });
        await session.Agent.AssertAsync("the page does not show " + Value);
        session.Complete();

        Assert.Equal(2, model.Requests.Count);
        var (act, judgment) = (model.Requests[0], model.Requests[1]);
        var actPrompt = string.Join('\n', act.Messages.Select(message => message.Content));
        Assert.Contains("inspect " + Marker + " with " + Marker, actPrompt, StringComparison.Ordinal);
        Assert.Contains("context " + Marker, act.System, StringComparison.Ordinal);
        var judgmentPrompt = string.Join('\n', judgment.Messages.Select(message => message.Content));
        Assert.Contains("the page does not show " + Marker, judgmentPrompt, StringComparison.Ordinal);
        Assert.Contains("context " + Marker, judgment.System, StringComparison.Ordinal);
        Assert.DoesNotContain(Value, string.Join('\n', model.Requests.SelectMany(request => request.Messages.Select(message => message.Content).Prepend(request.System))), StringComparison.Ordinal);
    }
}
