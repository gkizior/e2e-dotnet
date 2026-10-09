// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.LocatorActionOptions;

/// <summary>
/// "%s rejects an unknown option before any lookup, action, or step" is not ported: the port's options are a typed
/// class. The nested "modifiers" describe, "longPress sends a duration from 100 through 10000 and none when unset, so
/// the engine default holds", and "longPress refuses a duration of %s before any action" are not ported: the port has
/// no tap modifiers and no long press.
/// </summary>
public sealed class LocatorActionOptionsTests
{
    private static readonly SemanticNode Box = new() { Ref = "agree", Role = "checkbox", Name = "Agree" };

    private static readonly SemanticNode Bin = new() { Ref = "bin", Role = "region", Name = "Bin" };

    /// <summary>The upstream verbs the port has. It has no secondaryTap, longPress, hover, setInputFiles, dragTo, or swipe.</summary>
    private static readonly Dictionary<string, Func<Locator, ActionOptions, Task>> Calls = new()
    {
        ["tap"] = (locator, options) => locator.TapAsync(options),
        ["click"] = (locator, options) => locator.ClickAsync(options),
        ["doubleTap"] = (locator, options) => locator.DoubleTapAsync(options),
        ["dblclick"] = (locator, options) => locator.DblClickAsync(options),
        ["fill"] = (locator, options) => locator.FillAsync("yes", options),
        ["clear"] = (locator, options) => locator.ClearAsync(options),
        ["press"] = (locator, options) => locator.PressAsync("Enter", options),
        ["check"] = (locator, options) => locator.CheckAsync(options),
        ["uncheck"] = (locator, options) => locator.UncheckAsync(options),
        ["selectOption"] = (locator, options) => locator.SelectOptionAsync("Team", options),
        ["focus"] = (locator, options) => locator.FocusAsync(options),
        ["scrollIntoView"] = (locator, options) => locator.ScrollIntoViewAsync(options),
    };

    public static TheoryData<string> Verbs => new(Calls.Keys);

    [Theory]
    [MemberData(nameof(Verbs))]
    public async Task Performs_with_a_timeout(string verb)
    {
        var performed = new List<LocatorAction>();
        var observation = new Observation { Route = "/", Roots = [Box, Bin] };
        var screen = new Screen(
            _ => Task.FromResult(observation),
            (_, action, _) =>
            {
                performed.Add(action);
                return Task.CompletedTask;
            },
            () => CancellationToken.None,
            () => { },
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));

        await Calls[verb](screen.GetByLabel("Agree"), new ActionOptions { Timeout = TimeSpan.FromSeconds(1) });

        Assert.Single(performed);
    }
}
