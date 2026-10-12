// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using E2E.Mobile.Eas;

namespace E2E.Mobile.Tests.Eas;

internal enum EasOperation
{
    Create,
    State,
    Stop,
    Cancel,
}

internal sealed record EasCall(EasOperation Operation, string? Authorization, string? Session, JsonObject Variables);

/// <summary>A fake Expo GraphQL API behind an <see cref="HttpClient"/>: it records every call and answers what a test scripts.</summary>
internal sealed class FakeEas : HttpMessageHandler
{
    public static readonly JsonObject Ready = JsonNode.Parse("""
        {
          "status": "IN_PROGRESS",
          "turtleJobRun": { "status": "IN_PROGRESS" },
          "remoteConfig": {
            "agentDeviceRemoteSessionUrl": "https://agent-device-s1.eas-simulator.ngrok.dev",
            "agentDeviceRemoteSessionToken": "daemon-token",
            "webPreviewUrl": "https://expo.dev/simulator-preview/s1"
          }
        }
        """)!.AsObject();

    public const string SessionUrl = "https://expo.dev/accounts/acme/projects/shop/simulator-sessions/s1";

    public List<EasCall> Calls { get; set; } = [];

    /// <summary>What each state query returns, in order; the last one repeats.</summary>
    public List<JsonObject> States { get; set; } = [(JsonObject)Ready.DeepClone()];

    /// <summary>A GraphQL error an operation answers with instead of data: message and error code.</summary>
    public Dictionary<EasOperation, (string Message, string Code)> Errors { get; } = [];

    public Action? OnState { get; set; }

    public Action? OnCreate { get; set; }

    /// <summary>State polls that answer with a 502 page before the fake answers again.</summary>
    public int StateOutages { get; set; }

    /// <summary>The session's job as the stop mutation reports it.</summary>
    public (string Id, string Status)? StopJob { get; set; } = ("j1", "IN_PROGRESS");

    public string[] Operations => [.. Calls.Select(call => call.Operation.ToString().ToLowerInvariant())];

    public HttpClient Client => new(this);

    public static JsonObject Queued(string job = "IN_QUEUE") => Session("NEW", job);

    public static JsonObject Session(string status, string? job) => new()
    {
        ["status"] = status,
        ["turtleJobRun"] = job is null ? null : new JsonObject { ["status"] = job },
        ["remoteConfig"] = null,
    };

    public JsonObject Create(int index) => Calls.Where(call => call.Operation == EasOperation.Create).ElementAt(index).Variables["input"]!.AsObject();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Assert.Equal("https://api.expo.dev/graphql", request.RequestUri!.ToString());
        var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!.AsObject();
        var query = body["query"]!.GetValue<string>();
        var variables = body["variables"]!.AsObject();
        var operation = query.Contains("createDeviceRunSession", StringComparison.Ordinal) ? EasOperation.Create
            : query.Contains("ensureDeviceRunSessionStopped", StringComparison.Ordinal) ? EasOperation.Stop
            : query.Contains("cancelJobRun", StringComparison.Ordinal) ? EasOperation.Cancel
            : EasOperation.State;
        request.Headers.TryGetValues("Authorization", out var authorization);
        request.Headers.TryGetValues("expo-session", out var session);
        lock (this)
        {
            Calls.Add(new EasCall(operation, authorization?.Single(), session?.Single(), variables));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Errors.TryGetValue(operation, out var error))
        {
            return Json(new JsonObject
            {
                ["errors"] = new JsonArray(new JsonObject { ["message"] = error.Message, ["extensions"] = new JsonObject { ["errorCode"] = error.Code } }),
                ["data"] = null,
            });
        }

        if (operation == EasOperation.Create)
        {
            OnCreate?.Invoke();
        }

        if (operation == EasOperation.State && StateOutages > 0)
        {
            StateOutages--;
            return new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("Bad gateway") };
        }

        switch (operation)
        {
            case EasOperation.Create:
                return Data("""{ "deviceRunSession": { "createDeviceRunSession": { "id": "s1", "app": { "slug": "shop", "ownerAccount": { "name": "acme" } } } } }""");
            case EasOperation.Stop:
                var job = StopJob is { } stop ? new JsonObject { ["id"] = stop.Id, ["status"] = stop.Status } : null;
                return Json(new JsonObject { ["data"] = new JsonObject { ["deviceRunSession"] = new JsonObject { ["ensureDeviceRunSessionStopped"] = new JsonObject { ["turtleJobRun"] = job } } } });
            case EasOperation.Cancel:
                return Json(new JsonObject { ["data"] = new JsonObject { ["jobRun"] = new JsonObject { ["cancelJobRun"] = new JsonObject { ["id"] = variables["id"]!.DeepClone() } } } });
            default:
                OnState?.Invoke();
                var next = States[0];
                if (States.Count > 1)
                {
                    States.RemoveAt(0);
                }

                return Json(new JsonObject { ["data"] = new JsonObject { ["deviceRunSessions"] = new JsonObject { ["byId"] = next.DeepClone() } } });
        }
    }

    private static HttpResponseMessage Data(string data) => Json(new JsonObject { ["data"] = JsonNode.Parse(data) });

    private static HttpResponseMessage Json(JsonObject body) => new(HttpStatusCode.OK) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
}

/// <summary>The clock a test polls with: each reading moves it on by <see cref="Step"/>, and a sleep takes no time.</summary>
internal sealed class FakeClock
{
    private long _now;

    public long Step { get; init; }

    public EasClock Clock => new(
        () => _now += Step,
        (_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
}
