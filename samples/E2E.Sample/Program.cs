// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using E2E;
using E2E.Engine;
using E2E.Web;

var web = args.Contains("--web");
var cache = Path.Combine(Path.GetTempPath(), "e2e-sample", Guid.NewGuid().ToString("n"));
using var site = web ? await BillingSite.StartAsync() : null;
var engine = web
    ? (IEngine)new WebEngine()
    : new DocumentEngine(BillingApp.Create());
var baseUrl = site?.Url ?? "https://billing.test";

Console.WriteLine(web ? "Web engine at " + baseUrl : "Document engine (no browser, no model key)");
Console.WriteLine();
Console.WriteLine("Run 1");
var first = await RunAsync(engine, baseUrl, cache);
Console.WriteLine();
Console.WriteLine("Run 2");
var second = await RunAsync(engine, baseUrl, cache);
Console.WriteLine();
Console.WriteLine(second.Replayed > 0
    ? "The second run replayed the upgrade without calling the model."
    : "The second run did not replay. See the cache summary above.");
Environment.Exit(first.ExitCode == 0 && second.ExitCode == 0 ? 0 : 1);

static Task<RunResult> RunAsync(IEngine engine, string baseUrl, string cache)
{
    var suite = SuiteDiscovery.Discover(typeof(BillingTests).Assembly);
    return Runner.RunAsync(suite, new RunOptions
    {
        Engine = engine,
        Model = new BillingModel(),
        BaseUrl = baseUrl,
        CacheDirectory = cache,
        CacheEnabled = true,
        ReportPath = null,
        AssertionTimeout = TimeSpan.FromSeconds(5),
        ActionTimeout = TimeSpan.FromSeconds(5),
        StepTimeout = TimeSpan.FromSeconds(20),
    });
}

internal static class BillingApp
{
    public static DocumentWorld Create()
    {
        return new DocumentWorld().Map("/settings/billing", page =>
        {
            page.Heading("Billing");
            var status = page.Status("Pro", hidden: true);
            var invoice = page.Paragraph("Invoice preview");
            page.Button("Upgrade to Pro", () =>
            {
                status.Hidden = false;
                invoice.Text = "Prorated amount: $12";
            });
        });
    }
}

internal sealed class BillingModel : IAgentModel
{
    public string Name => "sample";

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var text = string.Join('\n', request.Messages.Select(message => message.Content));
        if (text.Contains("Statement:", StringComparison.Ordinal))
        {
            var passed = text.Contains("Prorated", StringComparison.Ordinal);
            return Task.FromResult(passed
                ? ModelResponses.Done("passed", "The invoice preview shows a prorated amount.")
                : ModelResponses.Done("failed", "The invoice preview has no prorated amount.", "ASSERTION_FAILED"));
        }

        if (text.Contains("tapped", StringComparison.Ordinal))
        {
            return Task.FromResult(ModelResponses.Done("passed", "Upgraded the workspace to Pro."));
        }

        return Task.FromResult(ModelResponses.Tap("button", "Upgrade to Pro"));
    }
}

[E2ESuite("billing")]
public sealed class BillingTests
{
    [E2ETest("a member upgrades to Pro")]
    public async Task Upgrades(App app, Agent agent, Screen screen)
    {
        await app.OpenAsync("/settings/billing");
        await agent.ActAsync("upgrade the workspace to the Pro plan");
        await agent.AssertAsync("the invoice preview shows a prorated amount");
        await Expect.That(screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}

internal sealed class BillingSite : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();

    private BillingSite(string url)
    {
        Url = url;
    }

    public string Url { get; }

    public static async Task<BillingSite> StartAsync()
    {
        var port = FreePort();
        var site = new BillingSite("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
        site._listener.Prefixes.Add(site.Url);
        site._listener.Start();
        _ = Task.Run(() => site.ListenAsync(site._stop.Token));
        await Task.Delay(50);
        return site;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
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

            var bytes = Encoding.UTF8.GetBytes(Page);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private const string Page = """
        <!DOCTYPE html>
        <html>
        <head><title>Billing</title></head>
        <body>
          <h1>Billing</h1>
          <button id="upgrade" type="button">Upgrade to Pro</button>
          <p id="invoice" hidden>Prorated amount: $12</p>
          <div id="status" role="status" hidden>Pro</div>
          <script>
            document.getElementById("upgrade").addEventListener("click", () => {
              document.getElementById("invoice").hidden = false;
              document.getElementById("status").hidden = false;
            });
          </script>
        </body>
        </html>
        """;
}
