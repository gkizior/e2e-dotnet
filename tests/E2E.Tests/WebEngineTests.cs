// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using E2E;
using E2E.Web;

namespace E2E.Tests;

public sealed class WebEngineTests
{
    [Fact]
    public async Task Chromium_upgrades_the_plan_when_a_browser_is_installed()
    {
        using var site = await TinySite.StartAsync();
        var suite = new Suite("web");
        suite.Test("upgrades", async ctx =>
        {
            await ctx.App.OpenAsync("/");
            await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
            await Expect.That(ctx.Screen.GetByRole("status")).ToContainTextAsync("Pro");
        });

        var result = await Runner.RunAsync(suite, new RunOptions
        {
            Engine = new WebEngine(headless: true),
            Model = new ScriptedModel(request =>
            {
                var text = string.Join('\n', request.Messages.Select(message => message.Content));
                if (text.Contains("tapped", StringComparison.Ordinal))
                {
                    return ModelResponses.Done("passed", "upgraded");
                }

                return ModelResponses.Tap("button", "Upgrade to Pro");
            }),
            BaseUrl = site.Url,
            CacheEnabled = false,
            ReportPath = null,
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });

        if (result.Tests.Count == 1 && result.Tests[0].ErrorCode == "ENVIRONMENT_UNAVAILABLE")
        {
            return;
        }

        Assert.Equal(0, result.ExitCode);
    }
}

internal sealed class TinySite : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();

    private TinySite(string url) => Url = url;

    public string Url { get; }

    public static async Task<TinySite> StartAsync()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var site = new TinySite("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
        site._listener.Prefixes.Add(site.Url);
        site._listener.Start();
        _ = Task.Run(() => site.ListenAsync(site._stop.Token));
        await Task.Delay(30);
        return site;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes("""
            <!DOCTYPE html>
            <html><body>
            <h1>Billing</h1>
            <button type="button">Upgrade to Pro</button>
            <p id="invoice" hidden>Prorated amount: $12</p>
            <div role="status" hidden>Pro</div>
            <script>
              document.querySelector("button").addEventListener("click", () => {
                document.getElementById("invoice").hidden = false;
                document.querySelector("[role=status]").hidden = false;
              });
            </script>
            </body></html>
            """);
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception)
            {
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }
}
