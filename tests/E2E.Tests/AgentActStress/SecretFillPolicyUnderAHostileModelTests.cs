// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using WebFixtureApp = E2E.Tests.WebPlatform.FixtureApp;

namespace E2E.Tests.AgentActStress;

/// <summary>
/// Secret fills under a model that tries every sink. Upstream's "never leaks the plaintext into prompts or the transcript"
/// is ported without the transcript: the port writes no transcript file, so only the prompts and tool results are read.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class SecretFillPolicyUnderAHostileModelTests
{
    private const string Generic = "sk_live_generic_4242";
    private const string AdminPassword = "admin-pass";

    [Fact]
    public async Task Fills_a_generic_secret_into_a_contenteditable_host_and_refuses_a_password_there()
    {
        using var site = await WebFixtureApp.StartAsync();
        var calls = 0;
        var toolResults = new List<string>();
        var model = new ScriptedModel(request =>
        {
            // The agent grows one message list across turns, so what each turn saw is read as the turn happens.
            toolResults.Add(LastToolResult(request));
            return calls++ switch
            {
                0 => ModelResponses.Call("fill_secret", new { role = "textbox", name = "Notes", secret = "admin" }),
                1 => ModelResponses.Call("fill_secret", new { role = "textbox", name = "Notes", secret = "stripe-key" }),
                _ => ModelResponses.Done("passed", "filled the API key"),
            };
        });
        await using var session = await StartAsync(site.Url, model);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.Context.App.OpenAsync("/editor");
            await session.Context.Agent.ActAsync("paste the API key into the notes editor", new ActOptions
            {
                Params = new Dictionary<string, object?>
                {
                    ["password"] = Secret.Create("admin", AdminPassword, "password"),
                    ["apiKey"] = Secret.Create("stripe-key", Generic),
                },
            });
        });

        Assert.Equal(3, model.CallCount);
        // The host is an editable textbox, so the sink is accepted; a password still needs a password field, and the editor has purpose none.
        Assert.Contains("field purpose none is incompatible with secret purpose password", toolResults[1], StringComparison.Ordinal);
        Assert.Contains("<secret:stripe-key>", toolResults[2], StringComparison.Ordinal);
        AssertNeverSeen(model, Generic);
    }

    [Fact]
    public async Task Never_leaks_the_plaintext_into_prompts_or_tool_results()
    {
        using var site = await WebFixtureApp.StartAsync();
        var model = new ScriptedModel(request => LastToolResult(request) == ""
            ? ModelResponses.Call("fill_secret", new { role = "textbox", name = "Password", secret = "admin" })
            : ModelResponses.Done("passed", "filled the secret"));
        await using var session = await StartAsync(site.Url, model);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.Context.App.OpenAsync("/");
            await session.Context.Agent.ActAsync("probe", new ActOptions
            {
                Params = new Dictionary<string, object?> { ["password"] = Secret.Create("admin", AdminPassword, "password") },
            });
        });

        // The model never saw the value: not in any prompt, only the placeholder.
        AssertNeverSeen(model, AdminPassword);
        Assert.Contains("<secret:admin>", string.Join('\n', model.Requests[^1].Messages.Select(message => message.Content)), StringComparison.Ordinal);
    }

    private static Task<E2ESession> StartAsync(string url, ScriptedModel model) =>
        E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = url,
            CacheEnabled = false,
            TestTitle = "secret probe",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });

    private static string LastToolResult(ModelRequest request) =>
        request.Messages.LastOrDefault(message => message.Role == "tool")?.Content ?? "";

    private static void AssertNeverSeen(ScriptedModel model, string plaintext)
    {
        foreach (var request in model.Requests)
        {
            Assert.DoesNotContain(plaintext, request.System, StringComparison.Ordinal);
            foreach (var message in request.Messages)
            {
                Assert.DoesNotContain(plaintext, message.Content ?? "", StringComparison.Ordinal);
            }
        }
    }
}
