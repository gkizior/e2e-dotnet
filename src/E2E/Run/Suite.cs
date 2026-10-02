// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

namespace E2E;

/// <summary>Options on one test. A skip reports the test as skipped and does not run it.</summary>
public sealed class TestOptions
{
    public string? Skip { get; init; }

    /// <summary>When any test sets this, the run executes only those tests. Rejected when <c>CI=true</c>.</summary>
    public bool Only { get; init; }

    public int Retries { get; init; }

    public TimeSpan? Timeout { get; init; }

    public IReadOnlyList<string> Tags { get; init; } = [];
}

/// <summary>
/// A group of tests. Hooks registered before <see cref="Test"/> apply to the tests that follow.
/// The runner gives every test its own engine session.
/// </summary>
public sealed class Suite
{
    private readonly string _group = Guid.NewGuid().ToString("n");
    private readonly List<SuiteTest> _tests = [];
    private Func<TestContext, Task>? _beforeEach;
    private Func<TestContext, Task>? _afterEach;
    private Func<Task>? _beforeAll;
    private Func<Task>? _afterAll;

    public Suite(string? name = null)
    {
        Name = name;
    }

    public string? Name { get; }

    internal IReadOnlyList<SuiteTest> Tests => _tests;

    public Suite BeforeAll(Func<Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        var previous = _beforeAll;
        _beforeAll = previous is null ? hook : async () =>
        {
            await previous().ConfigureAwait(false);
            await hook().ConfigureAwait(false);
        };
        return this;
    }

    public Suite AfterAll(Func<Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        var previous = _afterAll;
        _afterAll = previous is null ? hook : async () =>
        {
            await previous().ConfigureAwait(false);
            await hook().ConfigureAwait(false);
        };
        return this;
    }

    public Suite BeforeEach(Func<TestContext, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        var previous = _beforeEach;
        _beforeEach = previous is null ? hook : async ctx =>
        {
            await previous(ctx).ConfigureAwait(false);
            await hook(ctx).ConfigureAwait(false);
        };
        return this;
    }

    public Suite AfterEach(Func<TestContext, Task> hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        var previous = _afterEach;
        _afterEach = previous is null ? hook : async ctx =>
        {
            await previous(ctx).ConfigureAwait(false);
            await hook(ctx).ConfigureAwait(false);
        };
        return this;
    }

    public Suite Test(string title, Func<TestContext, Task> body, TestOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(body);
        Add(new SuiteTest
        {
            Title = Name is null ? title : Name + " > " + title,
            SuiteName = Name,
            GroupKey = _group,
            Body = body,
            BeforeEach = _beforeEach,
            AfterEach = _afterEach,
            BeforeAll = _beforeAll,
            AfterAll = _afterAll,
            Options = options ?? new TestOptions(),
            Tags = options?.Tags ?? [],
        });
        return this;
    }

    internal void Add(SuiteTest test) => _tests.Add(test);
}

internal sealed class SuiteTest
{
    public required string Title { get; init; }

    public string? SuiteName { get; init; }

    public required string GroupKey { get; init; }

    public required Func<TestContext, Task> Body { get; init; }

    public Func<TestContext, Task>? BeforeEach { get; init; }

    public Func<TestContext, Task>? AfterEach { get; init; }

    public Func<Task>? BeforeAll { get; init; }

    public Func<Task>? AfterAll { get; init; }

    public TestOptions Options { get; init; } = new();

    public IReadOnlyList<string> Tags { get; init; } = [];
}
