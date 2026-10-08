// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;

namespace E2E.Tests.InitScripts;

/// <summary>
/// Init scripts: the configured <c>InitScripts</c> and <c>Browser.AddInitScriptAsync</c>
/// run in every document before its own scripts, in order, in new tabs and
/// frames, and again on each context the attempt replaces. Each page here
/// records, from its first inline script, the trail the init scripts left.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class InitScriptsTests : IDisposable
{
    private const string Boot = "<script>window.seenAtBoot = [...(window.trail ?? [])];</script>";

    private readonly string _directory = Directory.CreateTempSubdirectory("e2e-init-scripts-").FullName;

    public InitScriptsTests()
    {
        File.WriteAllText(FromFile, "(window.trail ??= []).push('file');\n");
    }

    private string FromFile => Path.Combine(_directory, "from-file.js");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task Runs_the_configured_scripts_in_order_before_the_page_in_frames_new_tabs_and_replaced_contexts()
    {
        using var site = await StartSiteAsync();
        var initScripts = new WebInitScript[]
        {
            "(window.trail ??= []).push('source');",
            WebInitScript.FromPath(FromFile),
            WebInitScript.FromFunction("() => { (window.trail ??= []).push('function'); }"),
        };
        await AttemptAsync(site, initScripts, async session =>
        {
            string[] expected = ["source", "file", "function"];
            await session.App.OpenAsync("/framed");
            Assert.Equal(expected, await SeenAtBootAsync(session));
            Assert.Equal(expected, await PollAsync(session, "document.querySelector('iframe').contentWindow.seenAtBoot ?? null"));

            await session.Browser.EvaluateAsync<object>("(target) => { window.tab = window.open(target); return null; }", site.Url);
            Assert.Equal(expected, await PollAsync(session, "window.tab.seenAtBoot ?? null"));

            await session.App.ClearStateAsync();
            await session.App.OpenAsync("/");
            Assert.Equal(expected, await SeenAtBootAsync(session));
        });
    }

    [Fact]
    public async Task Adds_a_test_script_after_the_configured_ones_from_the_next_document_on_for_the_rest_of_the_attempt()
    {
        using var site = await StartSiteAsync();
        await AttemptAsync(site, ["(window.trail ??= []).push('config');"], async session =>
        {
            await session.App.OpenAsync("/");
            await session.Browser.AddInitScriptAsync(WebInitScript.FromFunction("(wallet) => { (window.trail ??= []).push(`wallet:${wallet.address}`); }"), new { address = "0xabc" });
            Assert.Equal(["config"], await SeenAtBootAsync(session));
            await session.Browser.ReloadAsync();
            Assert.Equal(["config", "wallet:0xabc"], await SeenAtBootAsync(session));

            var arg = System.Text.Json.JsonDocument.Parse("""{"__proto__":{"own":true}}""").RootElement;
            await session.Browser.AddInitScriptAsync(
                WebInitScript.FromFunction("(value) => { (window.trail ??= []).push(`own __proto__:${String(Object.hasOwn(value, '__proto__'))}`); }"),
                arg);
            await session.Browser.AddInitScriptAsync(WebInitScript.FromPath(FromFile));
            await session.App.ClearStateAsync();
            await session.App.OpenAsync("/");
            Assert.Equal(["config", "wallet:0xabc", "own __proto__:true", "file"], await SeenAtBootAsync(session));
        });
        await AttemptAsync(site, null, async session =>
        {
            await session.App.OpenAsync("/");
            Assert.Empty(await SeenAtBootAsync(session));
        });
    }

    [Fact]
    public async Task Refuses_a_script_it_cannot_run_and_a_secret_argument()
    {
        using var site = await StartSiteAsync();
        await AttemptAsync(site, null, async session =>
        {
            var argument = await Assert.ThrowsAsync<TestException>(() => session.Browser.AddInitScriptAsync("window.x = 1", 3));
            Assert.Equal("INVALID_ARGUMENT", argument.Code);
            Assert.Matches("only with a function", argument.Message);
            var missing = await Assert.ThrowsAsync<TestException>(() => session.Browser.AddInitScriptAsync(WebInitScript.FromPath("missing.js")));
            Assert.Equal("INVALID_ARGUMENT", missing.Code);
            Assert.Matches(@"missing\.js \(ENOENT\)", missing.Message);
            var secret = await Assert.ThrowsAsync<TestException>(() => session.Browser.AddInitScriptAsync(WebInitScript.FromFunction("() => undefined"), Secret.Create("key", "s3cret-value")));
            Assert.Equal("POLICY_DENIED", secret.Code);
        });
    }

    [Fact]
    public async Task Fails_the_run_and_a_worker_with_INVALID_CONFIG_when_a_configured_file_cannot_be_read()
    {
        var engine = new WebEngine(new WebEngineOptions { Headless = true, InitScripts = [WebInitScript.FromPath(Path.Combine(_directory, "nope.js"))] });
        var error = await Assert.ThrowsAsync<EngineException>(() => engine.StartAsync(new EngineStartOptions(), CancellationToken.None));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Matches(@"^initScripts\[0\] path cannot be read: .*nope\.js \(ENOENT\)$", error.Message);
    }

    private static Task<TinySite> StartSiteAsync()
    {
        return TinySite.StartAsync(context => TinySite.RespondAsync(context, context.Request.Url!.AbsolutePath == "/framed"
            ? "<!doctype html><html><head>" + Boot + "</head><body><iframe src=\"/\"></iframe></body></html>"
            : "<!doctype html><html><head>" + Boot + "</head><body><h1>Home</h1></body></html>"));
    }

    private static async Task<string[]> SeenAtBootAsync(E2ESession session) =>
        await session.Browser.EvaluateAsync<string[]>("window.seenAtBoot ?? null") ?? throw new InvalidOperationException("The boot script did not run.");

    // A frame or tab loads on its own schedule; read it once its boot script ran.
    private static async Task<string[]> PollAsync(E2ESession session, string expression)
    {
        for (var tries = 0; tries < 50; tries++)
        {
            if (await session.Browser.EvaluateAsync<string[]>(expression) is { } seen)
            {
                return seen;
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException("The boot script did not run: " + expression);
    }

    private static async Task AttemptAsync(TinySite site, IReadOnlyList<WebInitScript>? initScripts, Func<E2ESession, Task> body)
    {
        var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(new WebEngineOptions { Headless = true, InitScripts = initScripts }),
            BaseUrl = site.Url,
            TestTitle = "web > init scripts",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        await using (session)
        {
            Exception? error = null;
            try
            {
                await body(session);
            }
            catch (Exception ex)
            {
                error = ex;
            }

            session.Complete(error);
            Assert.Null(error);
        }
    }
}
