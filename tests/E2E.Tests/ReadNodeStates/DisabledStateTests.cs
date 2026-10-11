// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ReadNodeStates;

[Collection(BrowserCollection.Name)]
public sealed class DisabledStateTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Inherits_from_a_disabled_fieldset_except_inside_its_first_legend()
    {
        await chromium.Page.SetContentAsync("""
            <fieldset disabled>
              <legend>Billing <button data-testid="legend-action">Edit</button></legend>
              <label>Card <input data-testid="fenced-input"></label>
              <button data-testid="fenced-button">Pay</button>
              <select data-testid="fenced-select"><option>One</option></select>
              <textarea data-testid="fenced-textarea"></textarea>
              <legend>Second <button data-testid="second-legend-action">Not exempt</button></legend>
            </fieldset>
            <fieldset>
              <legend>Shipping</legend>
              <button data-testid="free-button">Ship</button>
            </fieldset>
            <button data-testid="own-disabled" disabled>Own</button>
            <button data-testid="own-enabled">Enabled</button>
            """);
        var states = await DisabledByTestIdAsync();
        Assert.Equal(
            new Dictionary<string, bool>
            {
                ["legend-action"] = false,
                ["fenced-input"] = true,
                ["fenced-button"] = true,
                ["fenced-select"] = true,
                ["fenced-textarea"] = true,
                ["second-legend-action"] = true,
                ["free-button"] = false,
                ["own-disabled"] = true,
                ["own-enabled"] = false,
            },
            states);
        await AssertAgreesWithPlaywrightAsync(states);
    }

    [Fact]
    public async Task Follows_nested_fieldsets_an_inner_legend_frees_nothing_from_an_outer_disabled_fieldset()
    {
        await chromium.Page.SetContentAsync("""
            <fieldset disabled>
              <legend>Outer</legend>
              <fieldset data-testid="inner-fieldset">
                <legend>Inner <button data-testid="inner-legend-action">Still fenced</button></legend>
                <button data-testid="inner-button">Fenced twice</button>
              </fieldset>
            </fieldset>
            <fieldset>
              <legend>Outer open</legend>
              <fieldset disabled>
                <legend>Inner closed <button data-testid="closed-legend-action">Free</button></legend>
                <button data-testid="closed-button">Fenced</button>
              </fieldset>
            </fieldset>
            """);
        var states = await DisabledByTestIdAsync();
        Assert.True(states["inner-legend-action"]);
        Assert.True(states["inner-button"]);
        Assert.False(states["closed-legend-action"]);
        Assert.True(states["closed-button"]);
        states.Remove("inner-fieldset");
        await AssertAgreesWithPlaywrightAsync(states);
    }

    [Fact]
    public async Task Inherits_aria_disabled_from_an_ancestor_across_a_shadow_root_until_an_aria_disabled_false_cuts_the_chain()
    {
        await chromium.Page.SetContentAsync("""
            <div aria-disabled="true">
              <button data-testid="under-aria">Save</button>
              <div role="group" aria-disabled="false">
                <button data-testid="reenabled">Cancel</button>
              </div>
              <span data-testid="plain-text">Just text</span>
              <x-host data-testid="host"></x-host>
            </div>
            <div role="toolbar" aria-disabled="true" data-testid="own-aria">
              <button data-testid="in-toolbar">Bold</button>
            </div>
            <button data-testid="outside">Outside</button>
            <script>
              const host = document.querySelector('x-host');
              const root = host.attachShadow({ mode: 'open' });
              const button = document.createElement('button');
              button.textContent = 'Shadow';
              button.setAttribute('data-testid', 'in-shadow');
              root.appendChild(button);
            </script>
            """);
        var states = await DisabledByTestIdAsync();
        Assert.True(states["under-aria"]);
        Assert.False(states["reenabled"]);
        Assert.False(states["plain-text"]);
        Assert.True(states["in-shadow"]);
        Assert.True(states["own-aria"]);
        Assert.True(states["in-toolbar"]);
        Assert.False(states["outside"]);
        states.Remove("host");
        await AssertAgreesWithPlaywrightAsync(states);
    }

    // The `disabled` state per test id, false when the node reports none.
    private async Task<Dictionary<string, bool>> DisabledByTestIdAsync() =>
        (await chromium.CaptureAsync()).Where(node => node.TestId is not null).ToDictionary(node => node.TestId!, node => node.Disabled);

    // Playwright's answer for the same elements, the reference the reader must agree with.
    private async Task AssertAgreesWithPlaywrightAsync(Dictionary<string, bool> states)
    {
        foreach (var (testId, disabled) in states)
        {
            Assert.True(disabled == await chromium.Page.GetByTestId(testId).IsDisabledAsync(), testId);
        }
    }
}
