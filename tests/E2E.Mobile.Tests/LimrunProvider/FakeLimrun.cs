// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using E2E.Mobile.Limrun;

namespace E2E.Mobile.Tests.LimrunProvider;

/// <summary>One request the fake Limrun API saw, with whether the caller's token had been cancelled when it arrived.</summary>
internal sealed record LimrunCall(string Method, Uri Uri, string? Authorization, JsonObject? Body, bool Cancelled)
{
    public string Resource => Uri.AbsolutePath.Split('/')[2];
}

/// <summary>A fake Limrun REST API behind an <see cref="HttpClient"/>: it records every request and answers what a test scripts, else a ready instance.</summary>
internal sealed class FakeLimrun : HttpMessageHandler
{
    private readonly Dictionary<string, Queue<Func<HttpResponseMessage>>> _scripted = [];

    public List<LimrunCall> Calls { get; } = [];

    public HttpClient Client => new(this);

    public static string IdOf(string resource) => resource == "ios_instances" ? "ios-test" : "android-test";

    public static JsonObject Instance(string id, string state = "ready") => new()
    {
        ["metadata"] = new JsonObject { ["id"] = id },
        ["status"] = new JsonObject
        {
            ["state"] = state,
            ["apiUrl"] = "https://device.example",
            ["token"] = "instance-secret",
            ["adbWebSocketUrl"] = "wss://adb.example",
        },
    };

    public IEnumerable<LimrunCall> Of(string method, string resource) => Calls.Where(call => call.Method == method && call.Resource == resource);

    /// <summary>Answers the next <paramref name="method"/> request to <paramref name="resource"/> with this response, once.</summary>
    public void Script(string method, string resource, Func<HttpResponseMessage> response)
    {
        if (!_scripted.TryGetValue($"{method} {resource}", out var queue))
        {
            _scripted[$"{method} {resource}"] = queue = new Queue<Func<HttpResponseMessage>>();
        }

        queue.Enqueue(response);
    }

    public void ScriptInstance(string method, string resource, string state) =>
        Script(method, resource, () => Json(Instance(IdOf(resource), state)));

    public static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))?.AsObject();
        var call = new LimrunCall(request.Method.Method, request.RequestUri!, request.Headers.Authorization?.ToString(), body, cancellationToken.IsCancellationRequested);
        lock (Calls)
        {
            Calls.Add(call);
        }

        Func<HttpResponseMessage>? scripted = null;
        lock (_scripted)
        {
            if (_scripted.TryGetValue($"{call.Method} {call.Resource}", out var queue) && queue.Count > 0)
            {
                scripted = queue.Dequeue();
            }
        }

        if (scripted is not null)
        {
            return scripted();
        }

        return call.Method == "DELETE" ? Status(HttpStatusCode.NoContent) : Json(Instance(IdOf(call.Resource)));
    }
}

/// <summary>The machine the provider runs on, faked: it records the daemons started and stopped and answers what a test scripts.</summary>
internal sealed class FakeLimrunHost : ILimrunHost
{
    public List<(LimrunDriverRequest Request, IReadOnlyDictionary<string, string?> Environment)> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    public int HeartbeatsStopped { get; private set; }

    public Exception? AndroidToolsFailure { get; set; }

    /// <summary>The binding the next start answers with, in place of the default one.</summary>
    public Func<LimrunDriverRequest, LimrunBinding>? OnStart { get; set; }

    public Queue<Exception> StopFailures { get; } = [];

    public Task AssertAndroidToolsAsync(IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken) =>
        AndroidToolsFailure is null ? Task.CompletedTask : Task.FromException(AndroidToolsFailure);

    public Task<LimrunBinding> StartDriverAsync(LimrunDriverRequest request, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        lock (Started)
        {
            Started.Add((request, environment));
        }

        return Task.FromResult(OnStart?.Invoke(request)
            ?? new LimrunBinding(new JsonObject { ["stateDir"] = "/state", ["leaseId"] = "driver-lease" }, "limrun:ios:driver-lease"));
    }

    public Task StopDriverAsync(string stateDirectory, IReadOnlyDictionary<string, string?> environment, CancellationToken cancellationToken)
    {
        lock (Stopped)
        {
            Stopped.Add(stateDirectory);
        }

        return StopFailures.Count > 0 ? Task.FromException(StopFailures.Dequeue()) : Task.CompletedTask;
    }

    public Task<Func<Task>> StartHeartbeatAsync(LimrunBinding binding, Action<string> log, CancellationToken cancellationToken) =>
        Task.FromResult<Func<Task>>(() =>
        {
            HeartbeatsStopped++;
            return Task.CompletedTask;
        });
}
