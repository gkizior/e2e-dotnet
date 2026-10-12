// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceTemplate;

/// <summary>
/// "puts a placeholder where each unique value was and leaves everything else literal" is not ported: the port keys on the
/// params and templates the recorded text with <see cref="CacheKeys.Template"/>, and has no params tree, JSON pointers, or
/// <c>templateParams</c>. Its keying half is the next test.
/// </summary>
public sealed class TemplateParamsAndTheKeyTests
{
    private static string Key(string name, string plan, bool marked = true) => CacheKeys.ParamsDigest(new Dictionary<string, object?>
    {
        ["name"] = marked ? Values.Unique(name) : name,
        ["plan"] = plan,
    });

    [Fact]
    public void Keys_two_calls_apart_only_by_what_they_did_not_mark()
    {
        Assert.Equal(Key("E2E abc", "pro"), Key("E2E xyz", "pro"));
        Assert.NotEqual(Key("E2E abc", "pro"), Key("E2E abc", "starter"));

        // Marking is part of the shape: the same values, marked and unmarked, are two entries.
        Assert.NotEqual(Key("E2E abc", "pro"), Key("E2E abc", "pro", marked: false));
    }
}
