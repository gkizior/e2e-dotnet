// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

public enum TestStatus
{
    Passed,
    Failed,
    Skipped,
}

public sealed class TestResult
{
    public required string Title { get; init; }

    public TestStatus Status { get; init; }

    public string? ErrorCode { get; init; }

    public string? Error { get; init; }

    public TimeSpan Duration { get; init; }

    public int Replayed { get; init; }

    public int HandedOff { get; init; }

    public int Missed { get; init; }

    public int ModelCalls { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }
}

public sealed class RunResult
{
    public int ExitCode { get; init; }

    public IReadOnlyList<TestResult> Tests { get; init; } = [];

    public int ModelCalls { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int Replayed { get; init; }

    public int HandedOff { get; init; }

    public int Missed { get; init; }

    public string? ModelName { get; init; }
}

public sealed class RunOptions
{
    public required IEngine Engine { get; init; }

    public IAgentModel? Model { get; init; }

    public string? BaseUrl { get; init; }

    public string CacheDirectory { get; init; } = ".e2e/cache";

    public bool CacheEnabled { get; init; } = true;

    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan AssertionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxModelCalls { get; init; } = 12;

    public string? Grep { get; init; }

    /// <summary>Written at the end of the run. Null skips the file.</summary>
    public string? ReportPath { get; init; } = ".e2e/report.json";

    public Action<string>? WriteLine { get; init; }
}

/// <summary>Runs a <see cref="Suite"/> one test at a time. Each test gets its own engine session.</summary>
public static class Runner
{
    public static async Task<RunResult> RunAsync(Suite suite, RunOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(options);
        var write = options.WriteLine ?? Console.WriteLine;
        var tests = suite.Tests.ToList();
        if (tests.Any(test => test.Options.Only))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
            {
                throw new TestException("ONLY_IN_CI", "test.only is rejected in CI.");
            }

            tests = tests.Where(test => test.Options.Only).ToList();
        }

        if (!string.IsNullOrWhiteSpace(options.Grep))
        {
            tests = tests.Where(test => Matches(test, options.Grep)).ToList();
        }

        IStepCache? cache = options.CacheEnabled ? new FileStepCache(options.CacheDirectory) : null;
        var results = new List<TestResult>();
        foreach (var group in tests.GroupBy(test => test.GroupKey))
        {
            var members = group.ToList();
            Exception? groupError = null;
            var beforeAll = members.Select(test => test.BeforeAll).FirstOrDefault(hook => hook is not null);
            if (beforeAll is not null)
            {
                try
                {
                    await beforeAll().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    groupError = ex;
                }
            }

            foreach (var test in members)
            {
                results.Add(groupError is null
                    ? await RunTestAsync(test, options, cache, write, cancellationToken).ConfigureAwait(false)
                    : Failed(test, groupError, TimeSpan.Zero));
            }

            var afterAll = members.Select(test => test.AfterAll).FirstOrDefault(hook => hook is not null);
            if (afterAll is not null && groupError is null)
            {
                await afterAll().ConfigureAwait(false);
            }
        }

        var result = new RunResult
        {
            ExitCode = ExitCodeOf(results),
            Tests = results,
            ModelCalls = results.Sum(test => test.ModelCalls),
            InputTokens = results.Sum(test => test.InputTokens),
            OutputTokens = results.Sum(test => test.OutputTokens),
            Replayed = results.Sum(test => test.Replayed),
            HandedOff = results.Sum(test => test.HandedOff),
            Missed = results.Sum(test => test.Missed),
            ModelName = options.Model?.Name,
        };
        var passed = results.Count(test => test.Status == TestStatus.Passed);
        var failed = results.Count(test => test.Status == TestStatus.Failed);
        var skipped = results.Count(test => test.Status == TestStatus.Skipped);
        write("");
        write(SummaryLine(passed, failed, skipped));
        write("");
        write("AI     " + (result.ModelName ?? "none") + " · " + result.ModelCalls.ToString(CultureInfo.InvariantCulture) + " model calls");
        write("Cache  " + result.Replayed.ToString(CultureInfo.InvariantCulture) + " replayed · " + result.HandedOff.ToString(CultureInfo.InvariantCulture) + " handed off · " + result.Missed.ToString(CultureInfo.InvariantCulture) + " missed");
        if (!string.IsNullOrWhiteSpace(options.ReportPath))
        {
            Report.Write(options.ReportPath, result);
        }

        return result;
    }

    private static async Task<TestResult> RunTestAsync(
        SuiteTest test,
        RunOptions options,
        IStepCache? cache,
        Action<string> write,
        CancellationToken cancellationToken)
    {
        if (test.Options.Skip is not null)
        {
            write("  skip  " + test.Title);
            write("        " + test.Options.Skip);
            return new TestResult { Title = test.Title, Status = TestStatus.Skipped, Error = test.Options.Skip };
        }

        var attempts = Math.Max(1, test.Options.Retries + 1);
        var clock = Stopwatch.StartNew();
        Exception? error = null;
        E2ESession? scope = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            AttemptOutcome outcome;
            try
            {
                outcome = await RunAttemptAsync(test, options, cache, attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                error = ex;
                continue;
            }

            scope = outcome.Session;
            if (outcome.Error is SkipException skip)
            {
                clock.Stop();
                write("  skip  " + test.Title);
                write("        " + skip.Message);
                return new TestResult { Title = test.Title, Status = TestStatus.Skipped, Error = skip.Message, Duration = clock.Elapsed };
            }

            if (outcome.Error is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw outcome.Error;
            }

            error = outcome.Error;
            if (error is null)
            {
                break;
            }
        }

        clock.Stop();
        if (error is null && scope is not null)
        {
            var line = "  pass  " + test.Title + " (" + Milliseconds(clock.Elapsed) + "ms)";
            if (scope.Replayed > 0)
            {
                line += "  cache replayed";
            }

            write(line);
            return FromScope(test.Title, TestStatus.Passed, null, null, clock.Elapsed, scope);
        }

        var described = Describe(error ?? new TestException("TEST_FAILED", "The test failed."));
        write("  fail  " + test.Title + " (" + Milliseconds(clock.Elapsed) + "ms)");
        write("        " + described.Code + ": " + described.Message);
        return FromScope(test.Title, TestStatus.Failed, described.Code, described.Message, clock.Elapsed, scope);
    }

    private static async Task<AttemptOutcome> RunAttemptAsync(
        SuiteTest test,
        RunOptions options,
        IStepCache? cache,
        int attempt,
        CancellationToken cancellationToken)
    {
        var timeout = test.Options.Timeout ?? options.TestTimeout;
        await using var session = await E2ESession.StartAsync(
            new E2ESessionOptions
            {
                Engine = options.Engine,
                Model = options.Model,
                BaseUrl = options.BaseUrl,
                Cache = cache,
                CacheEnabled = options.CacheEnabled && attempt == 1,
                TestTitle = test.Title,
                TestTimeout = timeout,
                ActionTimeout = options.ActionTimeout,
                AssertionTimeout = options.AssertionTimeout,
                StepTimeout = options.StepTimeout,
                MaxModelCalls = options.MaxModelCalls,
                Attempt = attempt,
            },
            cancellationToken).ConfigureAwait(false);

        Exception? bodyError = null;
        try
        {
            if (test.BeforeEach is not null)
            {
                await test.BeforeEach(session.Context).ConfigureAwait(false);
            }

            await test.Body(session.Context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            bodyError = new TestException("STEP_TIMEOUT", "Test timed out after " + Milliseconds(timeout) + "ms.");
        }
        catch (Exception ex)
        {
            bodyError = ex;
        }

        if (test.AfterEach is not null)
        {
            try
            {
                await test.AfterEach(session.Context).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                bodyError ??= ex;
            }
        }

        session.Complete(bodyError, cancellationToken);
        return new AttemptOutcome(session, bodyError);
    }

    private readonly record struct AttemptOutcome(E2ESession Session, Exception? Error);

    private static TestResult FromScope(string title, TestStatus status, string? code, string? error, TimeSpan duration, E2ESession? scope)
    {
        return new TestResult
        {
            Title = title,
            Status = status,
            ErrorCode = code,
            Error = error,
            Duration = duration,
            Replayed = scope?.Replayed ?? 0,
            HandedOff = scope?.HandedOff ?? 0,
            Missed = scope?.Missed ?? 0,
            ModelCalls = scope?.ModelCalls ?? 0,
            InputTokens = scope?.InputTokens ?? 0,
            OutputTokens = scope?.OutputTokens ?? 0,
        };
    }

    private static TestResult Failed(SuiteTest test, Exception error, TimeSpan duration)
    {
        var described = Describe(error);
        return new TestResult
        {
            Title = test.Title,
            Status = TestStatus.Failed,
            ErrorCode = described.Code,
            Error = described.Message,
            Duration = duration,
        };
    }

    private static (string Code, string Message) Describe(Exception exception)
    {
        if (exception is E2EException e2e)
        {
            return (e2e.Code, e2e.Message);
        }

        return ("TEST_FAILED", exception.Message);
    }

    private static bool Matches(SuiteTest test, string grep)
    {
        if (test.Title.Contains(grep, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return test.Tags.Any(tag => tag.Contains(grep, StringComparison.OrdinalIgnoreCase));
    }

    private static int ExitCodeOf(IReadOnlyList<TestResult> results)
    {
        if (results.Any(result => result.ErrorCode == "ENVIRONMENT_UNAVAILABLE"))
        {
            return 3;
        }

        if (results.Any(result => result.ErrorCode is "AUTH_CREDENTIAL_INVALID" or "AUTH_CREDENTIAL_UNAVAILABLE" or "SEED_DATA_MISSING" or "TEST_SETUP_FAILED"))
        {
            return 2;
        }

        return results.Any(result => result.Status == TestStatus.Failed) ? 1 : 0;
    }

    private static string SummaryLine(int passed, int failed, int skipped)
    {
        var line = passed.ToString(CultureInfo.InvariantCulture) + " passed";
        if (failed > 0)
        {
            line += ", " + failed.ToString(CultureInfo.InvariantCulture) + " failed";
        }

        if (skipped > 0)
        {
            line += ", " + skipped.ToString(CultureInfo.InvariantCulture) + " skipped";
        }

        return line;
    }

    private static string Milliseconds(TimeSpan duration)
    {
        return ((int)duration.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>JSON report and a short markdown page.</summary>
public static class Report
{
    public static void Write(string path, RunResult result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(result);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(result, JsonDefaults.Options));
    }

    public static string ToMarkdown(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var builder = new System.Text.StringBuilder();
        builder.Append("# e2e\n\n");
        foreach (var test in result.Tests)
        {
            builder.Append("- **").Append(test.Status.ToString().ToLowerInvariant()).Append("** ");
            builder.Append(test.Title);
            if (test.ErrorCode is not null)
            {
                builder.Append(" — ").Append(test.ErrorCode);
            }

            builder.Append('\n');
        }

        builder.Append("\nCache: ").Append(result.Replayed).Append(" replayed, ").Append(result.HandedOff).Append(" handed off, ").Append(result.Missed).Append(" missed.\n");
        return builder.ToString();
    }
}
