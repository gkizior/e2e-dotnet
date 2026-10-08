// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Text.Json;
using Microsoft.Playwright;

namespace E2E.Tests.AttemptSession;

/// <summary>
/// A Playwright interface answered by the members a test sets, by name; any other
/// member throws. It stands in for the remote browser, as upstream mocks <c>connectCdp</c>.
/// </summary>
public class Fake<T> : DispatchProxy
    where T : class
{
    public Dictionary<string, Func<object?[], object?>> Members { get; } = new(StringComparer.Ordinal);

    public static (T Proxy, Fake<T> Fake) New()
    {
        var proxy = Create<T, Fake<T>>();
        return (proxy, (Fake<T>)(object)proxy);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return Members.TryGetValue(targetMethod.Name, out var member)
            ? member(args ?? [])
            : throw new NotSupportedException(typeof(T).Name + "." + targetMethod.Name);
    }
}

/// <summary>A remote browser whose context identity may arrive after cancellation.</summary>
internal sealed class FakeRemote
{
    private bool _connected = true;

    private FakeRemote(string? identity, Task<JsonElement?>? response)
    {
        var (context, contextFake) = Fake<IBrowserContext>.New();
        Context = context;
        ContextFake = contextFake;
        contextFake.Members["get_Pages"] = _ => (IReadOnlyList<IPage>)Pages.ToList();
        contextFake.Members["NewPageAsync"] = _ => Task.FromResult(Pages[^1]);
        contextFake.Members["AddInitScriptAsync"] = _ => Task.FromResult(Registration);

        var (session, sessionFake) = Fake<ICDPSession>.New();
        sessionFake.Members["SendAsync"] = _ =>
        {
            Interlocked.Increment(ref SendCalls);
            return response ?? Task.FromResult<JsonElement?>(Json(identity is null
                ? """{"browserContextIds":[]}"""
                : $$"""{"browserContextIds":[],"defaultBrowserContextId":"{{identity}}"}"""));
        };
        sessionFake.Members["DetachAsync"] = _ => Task.CompletedTask;

        var (browser, browserFake) = Fake<IBrowser>.New();
        Browser = browser;
        browserFake.Members["NewBrowserCDPSessionAsync"] = _ => Task.FromResult(session);
        browserFake.Members["get_Contexts"] = _ => (IReadOnlyList<IBrowserContext>)[context];
        browserFake.Members["get_IsConnected"] = _ => _connected;
        browserFake.Members["CloseAsync"] = _ =>
        {
            Interlocked.Increment(ref CloseCalls);
            _connected = false;
            return Task.CompletedTask;
        };
    }

    public IBrowser Browser { get; }

    public IBrowserContext Context { get; }

    public Fake<IBrowserContext> ContextFake { get; }

    public List<IPage> Pages { get; } = [];

#pragma warning disable SA1401, CA1051 // Counted with Interlocked from the engine's threads.
    public int SendCalls;

    public int CloseCalls;
#pragma warning restore SA1401, CA1051

    /// <summary>What <c>AddInitScriptAsync</c> hands back: a handle that removes the script.</summary>
    public static IAsyncDisposable Registration { get; } = new NoRegistration();

    public static FakeRemote Create(string? identity = null, Task<JsonElement?>? response = null) => new(identity, response);

    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Drops the transport, as the engine sees it.</summary>
    public void Disconnect() => _connected = false;

    private sealed class NoRegistration : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>A page target whose identity response may outlive its attempt.</summary>
internal sealed class FakeTarget
{
    private FakeTarget(string id, Task<JsonElement?>? response)
    {
        var (page, pageFake) = Fake<IPage>.New();
        Page = page;
        PageFake = pageFake;
        var (session, sessionFake) = Fake<ICDPSession>.New();
        sessionFake.Members["SendAsync"] = _ =>
        {
            Interlocked.Increment(ref SendCalls);
            return response ?? Task.FromResult<JsonElement?>(FakeRemote.Json($$$"""{"targetInfo":{"targetId":"{{{id}}}"}}"""));
        };
        sessionFake.Members["DetachAsync"] = _ => Task.CompletedTask;
        var (context, contextFake) = Fake<IBrowserContext>.New();
        contextFake.Members["NewCDPSessionAsync"] = _ => Task.FromResult(session);
        pageFake.Members["get_Context"] = _ => context;
        pageFake.Members["SetViewportSizeAsync"] = args =>
        {
            ViewportCalls.Add(((int)args[0]!, (int)args[1]!));
            return Task.CompletedTask;
        };
        pageFake.Members["get_ViewportSize"] = _ => null;
        pageFake.Members["get_IsClosed"] = _ => Closed;
        pageFake.Members["SetDefaultTimeout"] = _ => null;
        pageFake.Members["GotoAsync"] = args =>
        {
            Gotos.Add(((string)args[0]!, ((PageGotoOptions?)args[1])?.Timeout));
            return Task.FromResult<IResponse?>(null);
        };
        pageFake.Members["CloseAsync"] = _ => Task.CompletedTask;
    }

    public IPage Page { get; }

    public Fake<IPage> PageFake { get; }

    public bool Closed { get; set; }

    public List<(int Width, int Height)> ViewportCalls { get; } = [];

    /// <summary>Each navigation's URL and the Playwright timeout it was given.</summary>
    public List<(string Url, float? Timeout)> Gotos { get; } = [];

#pragma warning disable SA1401, CA1051 // Counted with Interlocked from the engine's threads.
    public int SendCalls;
#pragma warning restore SA1401, CA1051

    public static FakeTarget Create(string id, Task<JsonElement?>? response = null) => new(id, response);
}

/// <summary>A clock that moves only when a test advances it; its timers never fire.</summary>
internal sealed class FakeClock : TimeProvider
{
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _now);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _now, by.Ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new NeverTimer();

    private sealed class NeverTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
