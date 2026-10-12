// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.TruncatedSecrets;

/// <summary>
/// A secret the web engine's observation limits cut short is still redacted: a filled credential echoed after filler
/// so the text (512) and name (256) limits keep all but its last character, as typed or upper-cased by CSS, reaches
/// no model input, while a plain value placed the same way is still cut and shown as it is. Upstream's "keeps the cut
/// fragment out of the failure screen, the report, and every file under .e2e" is not ported: the port writes no
/// failure screen, no report, and no .e2e directory. The port fills the secret through <c>act</c>, the only way a
/// secret joins the redaction list, into a password field, the only field a secret fill accepts.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class SecretsCutShortByObservationLimitsTests
{
    private const string Secret = "cut-secret-Kq7ZrT2mWx9pLd4sNv8bHc3jFg6yQa1eUo5iRk0tYw2zXn7uM";

    // The shortest leading part checked for: any longer one contains it.
    private const string Fragment = "cut-secr";

    // The same part as the CSS text-transform: uppercase echo shows it.
    private const string UpperFragment = "CUT-SECR";

    // What the control paragraph keeps once the text limit cuts it.
    private const string ControlKept = "plain-control-plain-control-plain-control-plain-control-pla";

    private const string Page = """
        <!doctype html>
        <html>
        <head><title>Echo cut</title></head>
        <body>
          <label for="source">Source</label>
          <input id="source" type="password" />
          <p id="echo"></p>
          <div id="echo-upper" contenteditable="true" aria-label="Upper echo" style="text-transform: uppercase"></div>
          <button id="echo-name">Echo</button>
          <p id="control"></p>
          <script>
            const filler = (length) => '0123456789'.repeat(60).slice(0, length);
            const echo = (value) => {
              document.getElementById('echo').textContent = filler(512 - value.length + 1) + value;
              document.getElementById('echo-upper').textContent = filler(512 - value.length + 1) + value;
              document.getElementById('echo-name').setAttribute('aria-label', filler(256 - value.length + 1) + value);
            };
            document.getElementById('source').addEventListener('input', (event) => echo(event.target.value));
            document.getElementById('control').textContent = filler(512 - 59) + 'plain-control-'.repeat(5);
          </script>
        </body>
        </html>
        """;

    [Fact]
    public async Task Keeps_the_cut_fragment_out_of_the_model_input_and_still_cuts_a_plain_value()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, Page));
        var model = new ScriptedModel(request =>
        {
            var last = request.Messages[^1];
            if (request.Messages[0].Content?.StartsWith("Statement:", StringComparison.Ordinal) == true)
            {
                return ModelResponses.Done("passed", "the echo is shown");
            }

            return last.Role == "tool"
                ? ModelResponses.Done("passed", "filled")
                : ModelResponses.Call("fill_secret", new { role = "textbox", name = "Source", secret = "member" });
        });
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            CacheEnabled = false,
            TestTitle = "echoes a secret across the observation limits",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        Exception? error = null;
        try
        {
            await session.Context.App.OpenAsync("/");
            await session.Context.Agent.ActAsync("fill the source with {password}", new ActOptions
            {
                Params = new Dictionary<string, object?> { ["password"] = E2E.Secret.Create("member", Secret) },
            });
            await session.Context.Agent.AssertAsync("the echo is on screen");
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        Assert.Null(error);
        var observation = string.Join('\n', model.Requests.SelectMany(request => request.Messages.Select(message => message.Content)));
        Assert.Contains(ControlKept + "\"", observation, StringComparison.Ordinal);
        Assert.DoesNotContain(Fragment, observation, StringComparison.Ordinal);
        Assert.DoesNotContain(UpperFragment, observation, StringComparison.Ordinal);
        // The echoed texts and the button's name each end in the marker where the cut value stood.
        Assert.True(Regex.Matches(observation, "<secret:member>\"").Count >= 3);
    }
}
