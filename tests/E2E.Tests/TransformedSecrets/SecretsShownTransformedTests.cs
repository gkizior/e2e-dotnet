// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.TransformedSecrets;

/// <summary>
/// Ported from upstream's <c>transformed-secrets.test.ts</c>, through the real Chromium engine: a token echoed
/// under CSS <c>text-transform</c> and a two-line note echoed in a <c>&lt;pre&gt;</c> reach the model's
/// observation, and no form of either value may survive there. The port has the model where upstream has an
/// executor, and registers a secret when an <c>act</c> receives it in its params. Not ported: "fails on each
/// echo with the received text redacted" (a failed matcher's message is not redacted with the session's
/// secrets), "matches the transformed text as the page shows it" (the port reads text as written, so a CSS
/// upper-cased echo is not upper-cased in the observation or to a matcher), and "leaves no form of either value
/// in the report or any file the reporters write" (no report, no reporters).
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class SecretsShownTransformedTests
{
    private const string Token = "Tok-9f3aC0DeB1e7";

    private const string Note = "first line 4417\nsecond line Qx";

    // Every form of the two values a reader or a transform can produce.
    private static readonly string[] Forms =
    [
        Token,
        Token.ToUpperInvariant(),
        Token.ToLowerInvariant(),
        Note,
        Note.Replace('\n', ' '),
        Note.Replace("\n", "\\n", StringComparison.Ordinal),
    ];

    // Upstream's testbed /echo page body.
    private const string EchoPage = """
        <!doctype html>
        <html>
        <head><title>Echo</title></head>
        <body>
          <label>Token <input id="token" type="text" oninput="render()" /></label>
          <label>Note <textarea id="note" oninput="render()"></textarea></label>
          <p data-testid="plain">nothing yet</p>
          <p data-testid="upper" style="text-transform: uppercase">nothing yet</p>
          <p data-testid="lower" style="text-transform: lowercase">nothing yet</p>
          <pre data-testid="note-echo">nothing yet</pre>
          <script>
            function render() {
              const token = document.getElementById('token').value;
              for (const id of ['plain', 'upper', 'lower']) {
                document.querySelector('[data-testid="' + id + '"]').textContent = token;
              }
              document.querySelector('[data-testid="note-echo"]').textContent = document.getElementById('note').value;
            }
          </script>
        </body>
        </html>
        """;

    [Fact]
    public async Task Shows_the_model_each_echo_redacted_with_no_form_of_either_value()
    {
        using var site = await TinySite.StartAsync(EchoPage);
        var model = new ScriptedModel(_ => ModelResponses.Done("passed", "read"));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            CacheEnabled = false,
            TestTitle = "observes the echoes",
            ActionTimeout = TimeSpan.FromSeconds(5),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        var token = Secret.Create("token", Token);
        var note = Secret.Create("note", Note);
        await session.App.OpenAsync("/");
        await session.Screen.GetByLabel("Token").FillAsync(token);
        await session.Screen.GetByLabel("Note").FillAsync(note);
        await session.Agent.ActAsync("read the echoes", new ActOptions
        {
            Params = new Dictionary<string, object?> { ["token"] = token, ["note"] = note },
        });
        session.Complete();

        var text = string.Join('\n', model.Requests.SelectMany(request => request.Messages.Select(message => message.Content).Prepend(request.System)));
        Assert.Contains("<secret:token>", text, StringComparison.Ordinal);
        Assert.Contains("<secret:note>", text, StringComparison.Ordinal);
        foreach (var form in Forms)
        {
            Assert.DoesNotContain(form, text, StringComparison.Ordinal);
        }
    }
}
