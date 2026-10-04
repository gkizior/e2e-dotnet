// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;

namespace E2E.OAuth;

/// <summary>
/// The one-shot local HTTP server a browser login redirects back to. It accepts one answer for the expected
/// state, a code or the vendor's refusal, replies to the browser with a page, and hands the answer to the flow.
/// When the port is taken, <see cref="TryStart"/> returns null and the flow falls back to a pasted code.
/// </summary>
internal sealed class CallbackServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _path;
    private readonly string _state;
    private readonly string _product;
    private readonly TaskCompletionSource<(string? Code, string? Error)> _answer = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private CallbackServer(HttpListener listener, int port, string path, string state, string product)
    {
        _listener = listener;
        _path = path;
        _state = state;
        _product = product;
        RedirectUri = "http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + path;
    }

    /// <summary>Vendors register <c>localhost</c>, not the loopback address, as the redirect host.</summary>
    public string RedirectUri { get; }

    public static CallbackServer? TryStart(int port, string path, string state, string product)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:" + port.ToString(CultureInfo.InvariantCulture) + "/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            listener.Close();
            return null;
        }

        var server = new CallbackServer(listener, port, path, state, product);
        _ = server.ServeAsync();
        return server;
    }

    /// <summary>The browser's answer: a code, or the vendor's error. Throws <see cref="OAuthException.Timeout"/> after <paramref name="timeout"/>.</summary>
    public async Task<(string? Code, string? Error)> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            return await _answer.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new OAuthException(OAuthException.Timeout, "the browser did not return in time", ex);
        }
        catch (OperationCanceledException ex)
        {
            throw new OAuthException(OAuthException.Cancelled, "the login was cancelled", ex);
        }
    }

    public void Dispose()
    {
        _listener.Close();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            Handle(context);
        }
    }

    private void Handle(HttpListenerContext context)
    {
        var url = context.Request.Url!;
        var query = System.Web.HttpUtility.ParseQueryString(url.Query);
        if (url.AbsolutePath != _path)
        {
            Reply(context, 404, "Not found", "This address is not part of the login.");
            return;
        }

        // The state binds the answer to this attempt; anything else on the port is ignored.
        if (query["state"] != _state)
        {
            Reply(context, 400, "Login failed", "The state does not match this login attempt.");
            return;
        }

        if (query["error"] is { } error)
        {
            Reply(context, 400, "Login failed", query["error_description"] ?? error);
            _answer.TrySetResult((null, error));
            return;
        }

        if (string.IsNullOrEmpty(query["code"]))
        {
            Reply(context, 400, "Login failed", "The redirect carried no authorization code.");
            _answer.TrySetResult((null, "invalid_callback"));
            return;
        }

        Reply(context, 200, "Signed in", "You can close this window and return to the terminal.");
        _answer.TrySetResult((query["code"], null));
    }

    private void Reply(HttpListenerContext context, int status, string heading, string message)
    {
        var html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\" /><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />"
            + "<title>" + WebUtility.HtmlEncode(_product + ": " + heading) + "</title>"
            + "<style>html{color-scheme:light dark}body{margin:0;min-height:100vh;display:grid;place-items:center;font-family:ui-sans-serif,system-ui,sans-serif}"
            + "main{max-width:32rem;padding:2rem;text-align:center}h1{font-size:1.5rem;margin:0 0 .5rem}p{margin:0;opacity:.75}</style></head>"
            + "<body><main><h1>" + WebUtility.HtmlEncode(heading) + "</h1><p>" + WebUtility.HtmlEncode(message) + "</p></main></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        try
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }
        catch (HttpListenerException)
        {
            // The browser went away; the answer still counts.
        }
    }
}
