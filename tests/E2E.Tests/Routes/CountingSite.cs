// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace E2E.Tests.Routes;

/// <summary>
/// A server that counts what reaches it: <c>/echo</c> answers with what it received,
/// <c>/api/*</c> answers <c>network:&lt;path&gt;</c>, and any other path is a page.
/// Every request is counted by path. It reads raw HTTP, so the same server answers
/// under any host name, such as <c>localhost</c>, a site of its own.
/// </summary>
internal sealed class CountingSite : IDisposable
{
    private readonly TcpListener _listener;
    private readonly ConcurrentBag<TcpClient> _clients = [];

    private CountingSite(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Port { get; }

    /// <summary>The site's origin, with no trailing slash.</summary>
    public string Origin => "http://127.0.0.1:" + Port;

    /// <summary>The same server under another host name.</summary>
    public string OtherSite => "http://localhost:" + Port;

    public ConcurrentDictionary<string, int> Hits { get; } = new(StringComparer.Ordinal);

    public static CountingSite Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var site = new CountingSite(listener);
        _ = Task.Run(site.ListenAsync);
        return site;
    }

    /// <summary>How many requests reached <paramref name="path"/>; null for none, as an unset map entry.</summary>
    public int? HitsOf(string path) => Hits.TryGetValue(path, out var count) ? count : null;

    public void Dispose()
    {
        _listener.Stop();
        foreach (var client in _clients)
        {
            client.Dispose();
        }
    }

    private async Task ListenAsync()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync();
            }
            catch (Exception)
            {
                return;
            }

            _clients.Add(client);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var (method, target, headers, body) = await ReadRequestAsync(stream);
            var path = target.Split('?')[0];
            Hits.AddOrUpdate(path, 1, (_, count) => count + 1);
            if (path == "/echo")
            {
                var echo = JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["path"] = path,
                    ["method"] = method,
                    ["headers"] = headers,
                    ["body"] = body,
                });
                await EndAsync(client, "application/json", echo);
                return;
            }

            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                await EndAsync(client, "text/plain", "network:" + path);
                return;
            }

            await EndAsync(client, "text/html", "<!doctype html><title>Routes</title><h1>Routes</h1>");
        }
        catch (Exception)
        {
            client.Dispose();
        }
    }

    private static async Task<(string Method, string Target, Dictionary<string, string> Headers, string Body)> ReadRequestAsync(NetworkStream stream)
    {
        var received = new List<byte>();
        var buffer = new byte[4096];
        int end;
        while ((end = IndexOfBlankLine(received)) < 0)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                throw new IOException("connection closed before the request ended");
            }

            received.AddRange(buffer.AsSpan(0, read));
        }

        var lines = Encoding.ASCII.GetString([.. received.Take(end)]).Split("\r\n");
        var start = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            headers[line[..colon].Trim().ToLowerInvariant()] = line[(colon + 1)..].Trim();
        }

        var length = headers.TryGetValue("content-length", out var declared) ? int.Parse(declared, System.Globalization.CultureInfo.InvariantCulture) : 0;
        var content = received.Skip(end + 4).ToList();
        while (content.Count < length)
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            content.AddRange(buffer.AsSpan(0, read));
        }

        return (start[0], start[1], headers, Encoding.UTF8.GetString([.. content]));
    }

    private static int IndexOfBlankLine(List<byte> bytes)
    {
        for (var i = 0; i + 3 < bytes.Count; i++)
        {
            if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }

    private static async Task EndAsync(TcpClient client, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = "HTTP/1.1 200 OK\r\ncontent-type: " + contentType + "\r\ncontent-length: "
            + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\r\nconnection: close\r\n\r\n";
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        client.Close();
    }
}
