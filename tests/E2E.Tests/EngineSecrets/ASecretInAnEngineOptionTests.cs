// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using E2E.Engine;

namespace E2E.Tests.EngineSecrets;

/// <summary>
/// Ported from upstream's <c>engine-secrets.test.ts</c>. The handle comes from <see cref="Secret.Create"/>, since the port
/// has no <c>e2e.config.ts</c> to call <c>secrets.get()</c>. Not ported, because the port has no such feature: "redacts the
/// value the page echoes from the failure, the report, and the reporters' files" and "redacts the base64 credential of the
/// echoed header everywhere" (an engine option does not add its value to the session's redaction list, and there are no
/// reporters), "protects the secret as text only: screenshots are kept, the text download rewritten" (<c>app.screenshot</c>
/// and <c>waitForDownload</c>), "reads the redacted tree, never the credential, with its pixels as with no secret" (vision),
/// and "hands the engine a declared value and refuses one it did not declare" (the engine has no declared-secrets API).
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ASecretInAnEngineOptionTests
{
    private const string Password = "basic-Pa55-7Qz";

    [Fact]
    public async Task Answers_the_challenge_with_the_resolved_value()
    {
        using var site = await TinySite.StartAsync(async context =>
        {
            var header = context.Request.Headers["Authorization"];
            if (header is null)
            {
                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"staging\"");
                context.Response.Close();
                return;
            }

            var credential = Encoding.UTF8.GetString(Convert.FromBase64String(header["Basic ".Length..]));
            var user = credential[..credential.IndexOf(':', StringComparison.Ordinal)];
            await TinySite.RespondAsync(context, $"<!doctype html><html><head><title>Basic auth</title></head><body><h1>Signed in as {user}</h1></body></html>");
        });
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(new WebEngineOptions
            {
                Headless = true,
                BasicAuth = new WebBasicAuth("ada", Secret.Create("stagingPassword", Password)),
            }),
            BaseUrl = site.Url,
            CacheEnabled = false,
            TestTitle = "signs in through basic auth",
            AssertionTimeout = TimeSpan.FromSeconds(4),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        await session.App.OpenAsync("/basic-auth");
        await Expect.That(session.Screen.GetByRole("heading", "Signed in as ada")).ToBeVisibleAsync();
    }
}
