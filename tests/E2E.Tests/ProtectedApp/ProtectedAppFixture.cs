// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;

namespace E2E.Tests.ProtectedApp;

/// <summary>
/// The pages of upstream's fixture app (<c>tests/helpers/fixture-app.ts</c>) the protected-app tests open:
/// <c>/headers</c> shows the <c>x-fixture-header</c> request header, <c>/protected</c> answers a basic-auth
/// challenge, and <c>/sw.js</c> is a service worker.
/// </summary>
internal static class ProtectedAppFixture
{
    public const string Username = "ada";

    public const string Password = "secret";

    private static readonly string Authorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Username + ":" + Password));

    /// <summary>Starts the app on <paramref name="host"/>: <c>127.0.0.1</c> is the app's site and <c>localhost</c> stands in for a third-party one.</summary>
    public static Task<TinySite> StartAsync(string host = "127.0.0.1") => TinySite.StartAsync(HandleAsync, host);

    private static async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        switch (request.Url!.AbsolutePath)
        {
            case "/sw.js":
                var script = Encoding.UTF8.GetBytes("self.addEventListener('fetch', () => {});");
                context.Response.ContentType = "text/javascript; charset=utf-8";
                context.Response.ContentLength64 = script.Length;
                await context.Response.OutputStream.WriteAsync(script);
                context.Response.Close();
                return;
            case "/headers":
                await TinySite.RespondAsync(context, Page("Fixture Headers", request.Headers["x-fixture-header"] ?? "none"));
                return;
            case "/protected":
                if (request.Headers["Authorization"] == Authorization)
                {
                    await TinySite.RespondAsync(context, Page("Fixture Protected", "Protected"));
                    return;
                }

                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"fixture\"");
                await TinySite.RespondAsync(context, Page("Fixture Unauthorized", "Unauthorized"));
                return;
            default:
                await TinySite.RespondAsync(context, Page("Fixture Home", "Home"));
                return;
        }
    }

    private static string Page(string title, string heading) =>
        $"<!doctype html><html><head><title>{title}</title></head><body><h1>{WebUtility.HtmlEncode(heading)}</h1></body></html>";
}
