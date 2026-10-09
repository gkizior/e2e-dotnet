// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;

namespace E2E.Tests.Controls;

/// <summary>
/// Ported from upstream <c>apps/testbed/tests/controls.e2e.ts</c>, against its
/// testbed <c>/controls</c> page. Upstream's <c>LOCATOR_AMBIGUOUS</c> and
/// <c>LOCATOR_NOT_FOUND</c> are the port's <c>STRICT_MODE</c> and <c>NOT_FOUND</c>.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ControlsTests
{
    // Upstream's testbed /controls page body.
    private const string ControlsPage = """
        <!DOCTYPE html>
        <html><head><title>Controls</title></head><body>
        <h1>Controls</h1>

        <ul aria-label="Files">
          <li id="file">report.pdf</li>
        </ul>
        <menu id="file-menu" role="menu" aria-label="File actions" hidden>
          <li><button role="menuitem" id="rename">Rename</button></li>
          <li><button role="menuitem" id="trash">Move to trash</button></li>
        </menu>
        <output role="status" aria-label="File state">untouched</output>

        <button id="hold">Hold me</button>
        <button id="twice">Tap me twice</button>
        <output role="status" aria-label="Gesture state">none</output>

        <button id="details-toggle" aria-expanded="false" aria-controls="details">Details</button>
        <p id="details" hidden>The fine print.</p>

        <button id="prepare">Prepare</button>
        <button id="publish" disabled>Publish</button>

        <fieldset>
          <legend>Size</legend>
          <label><input type="radio" name="size" value="s" /> Small</label>
          <label><input type="radio" name="size" value="m" /> Medium</label>
          <label><input type="radio" name="size" value="l" /> Large</label>
        </fieldset>

        <label for="agree">Agree to terms</label>
        <input id="agree" type="checkbox" />

        <label for="newsletter">Subscribe to newsletter</label>
        <input id="newsletter" type="checkbox" />
        <button id="dark-mode" role="switch" aria-checked="false">Dark mode</button>

        <label for="color">Color</label>
        <select id="color" size="3">
          <option value="red">Red</option>
          <option value="green" selected>Green</option>
          <option value="blue">Blue</option>
        </select>

        <label for="coupon">Coupon</label>
        <input id="coupon" value="SAVE10" />

        <label for="first">First</label>
        <input id="first" />
        <label for="second">Second</label>
        <input id="second" />

        <label for="keys">Key log input</label>
        <input id="keys" />
        <output aria-label="Key log"></output>

        <a id="docs" href="/about" data-kind="external" aria-label="Documentation" class="link primary">Docs</a>

        <span data-testid="banner" hidden>Sale</span>
        <span data-testid="banner">Sale</span>

        <ul aria-label="Tickets">
          <li>Ticket A <span class="badge">urgent</span> <button>Close A</button></li>
          <li>Ticket B <button>Close B</button></li>
          <li>Ticket C <span class="badge">urgent</span></li>
        </ul>

        <label for="attachments">Attachments</label>
        <input id="attachments" type="file" multiple />
        <output aria-label="Attachments state">none</output>

        <div style="height: 3000px"></div>
        <p id="footnote">Footnote</p>
        <output aria-label="Footnote state">out of view</output>

        <script>
          const fileState = document.querySelector('output[aria-label="File state"]');
          const menu = document.getElementById('file-menu');
          document.getElementById('file').addEventListener('contextmenu', (event) => {
            event.preventDefault();
            menu.hidden = false;
            fileState.textContent = 'menu open';
          });
          document.getElementById('rename').addEventListener('click', () => {
            menu.hidden = true;
            fileState.textContent = 'renamed';
          });
          document.getElementById('trash').addEventListener('click', () => {
            menu.hidden = true;
            fileState.textContent = 'trashed';
          });

          const gesture = document.querySelector('output[aria-label="Gesture state"]');
          let heldAt = 0;
          const hold = document.getElementById('hold');
          hold.addEventListener('pointerdown', () => {
            heldAt = performance.now();
          });
          hold.addEventListener('pointerup', () => {
            const held = Math.round(performance.now() - heldAt);
            gesture.textContent = held >= 500 ? 'long-pressed' : 'tapped';
          });
          document.getElementById('twice').addEventListener('dblclick', () => {
            gesture.textContent = 'double-tapped';
          });

          const toggle = document.getElementById('details-toggle');
          toggle.addEventListener('click', () => {
            const expanded = toggle.getAttribute('aria-expanded') === 'true';
            toggle.setAttribute('aria-expanded', String(!expanded));
            document.getElementById('details').hidden = expanded;
          });

          // Controlled toggles that commit their state after a save round
          // trip, as a React checkbox or a headless switch does: the click
          // leaves the control as it was, and the new state lands later.
          document.getElementById('newsletter').addEventListener('click', (event) => {
            const box = event.target;
            const next = box.checked;
            event.preventDefault();
            setTimeout(() => {
              box.checked = next;
            }, 400);
          });
          const darkMode = document.getElementById('dark-mode');
          darkMode.addEventListener('click', () => {
            const next = darkMode.getAttribute('aria-checked') !== 'true';
            setTimeout(() => {
              darkMode.setAttribute('aria-checked', String(next));
            }, 400);
          });

          document.getElementById('prepare').addEventListener('click', () => {
            setTimeout(() => {
              document.getElementById('publish').disabled = false;
            }, 1000);
          });

          const keyLog = document.querySelector('output[aria-label="Key log"]');
          document.getElementById('keys').addEventListener('keydown', (event) => {
            const parts = [];
            if (event.ctrlKey) parts.push('Control');
            if (event.altKey) parts.push('Alt');
            if (event.shiftKey) parts.push('Shift');
            if (event.metaKey) parts.push('Meta');
            parts.push(event.key);
            keyLog.textContent = parts.join('+');
          });

          document.getElementById('attachments').addEventListener('change', (event) => {
            document.querySelector('output[aria-label="Attachments state"]').textContent =
              Array.from(event.target.files, (file) => file.name).join(', ') || 'none';
          });

          const footnoteState = document.querySelector('output[aria-label="Footnote state"]');
          new IntersectionObserver((entries) => {
            footnoteState.textContent = entries[0].isIntersecting ? 'in view' : 'out of view';
          }).observe(document.getElementById('footnote'));
        </script>
        </body></html>
        """;

    [Fact]
    public Task Expanded_state_follows_the_disclosure_toggle() => RunAsync(async screen =>
    {
        var toggle = screen.GetByRole("button", "Details");
        await Expect.That(toggle).Not.ToBeExpandedAsync();
        await Expect.That(screen.GetByText("The fine print.")).ToBeHiddenAsync();
        await Expect.That(screen.GetByRole("button", new RoleOptions { Name = "Details", Expanded = false })).ToHaveCountAsync(1);

        await toggle.TapAsync();
        await Expect.That(toggle).ToBeExpandedAsync();
        await Expect.That(screen.GetByText("The fine print.")).ToBeVisibleAsync();
        await Expect.That(screen.GetByRole("button", new RoleOptions { Name = "Details", Expanded = true })).ToHaveCountAsync(1);
    });

    [Fact]
    public Task Enabled_state_arrives_late_and_every_read_agrees() => RunAsync(async screen =>
    {
        var publish = screen.GetByRole("button", "Publish");
        await Expect.That(publish).ToBeDisabledAsync();
        Assert.True(await publish.IsDisabledAsync());
        Assert.False(await publish.IsEnabledAsync());
        await Expect.That(screen.GetByRole("button", new RoleOptions { Name = "Publish", Disabled = true })).ToHaveCountAsync(1);

        await screen.GetByRole("button", "Prepare").TapAsync();
        await Expect.That(publish).ToBeEnabledAsync();
        await Expect.Poll(() => publish.IsEnabledAsync()).ToBeAsync(true);
        Assert.False(await publish.IsDisabledAsync());
        await Expect.That(screen.GetByRole("button", new RoleOptions { Name = "Publish", Disabled = false })).ToHaveCountAsync(1);
    });

    [Fact]
    public Task Check_and_uncheck_move_checked_state_and_one_radio_checks_at_a_time() => RunAsync(async screen =>
    {
        var agree = screen.GetByLabel("Agree to terms");
        Assert.False(await agree.IsCheckedAsync());
        await agree.CheckAsync();
        await Expect.That(agree).ToBeCheckedAsync();
        Assert.True(await agree.IsCheckedAsync());
        await agree.UncheckAsync();
        await Expect.That(agree).Not.ToBeCheckedAsync();

        await Expect.That(screen.GetByRole("radio", new RoleOptions { Checked = true })).ToHaveCountAsync(0);
        await screen.GetByRole("radio", "Medium").CheckAsync();
        await Expect.That(screen.GetByRole("radio", "Medium")).ToBeCheckedAsync();
        await Expect.That(screen.GetByRole("radio", "Small")).Not.ToBeCheckedAsync();
        await Expect.That(screen.GetByRole("radio", new RoleOptions { Checked = true })).ToHaveCountAsync(1);
        Assert.False(await screen.GetByRole("radio", "Large").IsCheckedAsync());
    });

    [Fact]
    public Task Check_and_uncheck_wait_for_a_controlled_checkbox_and_switch_that_commit_late() => RunAsync(async screen =>
    {
        var newsletter = screen.GetByLabel("Subscribe to newsletter");
        await newsletter.CheckAsync();
        Assert.True(await newsletter.IsCheckedAsync());
        await newsletter.UncheckAsync();
        Assert.False(await newsletter.IsCheckedAsync());

        var darkMode = screen.GetByRole("switch", "Dark mode");
        await darkMode.CheckAsync();
        Assert.True(await darkMode.IsCheckedAsync());
        await darkMode.UncheckAsync();
        Assert.False(await darkMode.IsCheckedAsync());
    });

    [Fact]
    public Task Selecting_an_option_moves_selected_state_onto_it() => RunAsync(async screen =>
    {
        var color = screen.GetByLabel("Color");
        await Expect.That(screen.GetByRole("option", "Green")).ToBeSelectedAsync();
        await Expect.That(screen.GetByRole("option", "Blue")).Not.ToBeSelectedAsync();

        await color.SelectOptionAsync("Blue");
        await Expect.That(color).ToHaveValueAsync("blue");
        await Expect.That(screen.GetByRole("option", "Blue")).ToBeSelectedAsync();
        await Expect.That(screen.GetByRole("option", "Green")).Not.ToBeSelectedAsync();
        await Expect.That(screen.GetByRole("option", new RoleOptions { Selected = true })).ToHaveTextAsync("Blue");
    });

    [Fact]
    public Task Focus_moves_with_Tab_and_Shift_Tab() => RunAsync(async screen =>
    {
        var first = screen.GetByLabel("First");
        var second = screen.GetByLabel("Second");

        await first.FocusAsync();
        await Expect.That(first).ToBeFocusedAsync();
        await Expect.That(second).Not.ToBeFocusedAsync();

        await first.PressAsync("Tab");
        await Expect.That(second).ToBeFocusedAsync();
        await Expect.That(first).Not.ToBeFocusedAsync();

        await second.PressAsync("Shift+Tab");
        await Expect.That(first).ToBeFocusedAsync();
    });

    [Fact]
    public Task Press_spells_modifiers_the_way_the_app_sees_them() => RunAsync(async screen =>
    {
        var keys = screen.GetByLabel("Key log input");
        var log = screen.GetByLabel("Key log");

        await keys.PressAsync("Shift+ArrowLeft");
        await Expect.That(log).ToHaveTextAsync("Shift+ArrowLeft");
        await keys.PressAsync("Control+a");
        await Expect.That(log).ToHaveTextAsync("Control+a");
        await keys.PressAsync("Alt+Shift+Enter");
        await Expect.That(log).ToHaveTextAsync("Alt+Shift+Enter");
        // A lone character is pressed as itself: no modifier is held for it, whatever the layout needs.
        await keys.PressAsync("$");
        await Expect.That(log).ToHaveTextAsync("$");
        await keys.PressAsync("Escape");
        await Expect.That(log).ToHaveTextAsync("Escape");
    });

    [Fact]
    public Task A_hidden_twin_is_skipped_by_a_visible_query_and_reached_by_index() => RunAsync(async screen =>
    {
        var banner = screen.GetByTestId("banner");
        await Expect.That(banner).ToHaveCountAsync(2);
        var error = await Assert.ThrowsAsync<TestException>(() => banner.TapAsync(new ActionOptions { Timeout = TimeSpan.FromMilliseconds(500) }));
        Assert.Equal("STRICT_MODE", error.Code);

        await Expect.That(screen.GetByTestId("banner", new TextMatchOptions { Visible = true })).ToHaveCountAsync(1);
        await Expect.That(screen.GetByTestId("banner", new TextMatchOptions { Visible = true })).ToBeVisibleAsync();
        await Expect.That(screen.GetByText("Sale", new TextMatchOptions { Visible = true })).ToHaveTextAsync("Sale");

        await Expect.That(banner.First()).ToBeHiddenAsync();
        await Expect.That(banner.First()).ToBeAttachedAsync();
        Assert.True(await banner.First().IsHiddenAsync());
        await Expect.That(banner.Last()).ToBeVisibleAsync();
        Assert.True(await banner.Last().IsVisibleAsync());
    });

    [Fact]
    public Task Filters_narrow_a_list_by_text_or_a_nested_match_and_nth_picks_one_row() => RunAsync(async screen =>
    {
        var tickets = screen.GetByRole("list", "Tickets").GetByRole("listitem");
        await Expect.That(tickets).ToHaveCountAsync(3);
        await Expect.That(tickets.Filter(screen.GetByText("urgent"))).ToHaveCountAsync(2);
        await Expect.That(tickets.Filter(screen.GetByRole("button"))).ToHaveCountAsync(2);
        await Expect.That(tickets.Filter("Ticket B")).ToHaveCountAsync(1);
        await Expect.That(tickets.Filter(screen.GetByText("urgent")).Filter(screen.GetByRole("button"))).ToContainTextAsync("Ticket A");
        await Expect.That(tickets.Nth(1)).ToContainTextAsync("Ticket B");
        await Expect.That(tickets.Nth(2).GetByRole("button")).ToHaveCountAsync(0);
        Assert.Equal(3, (await tickets.AllTextContentsAsync()).Count);
        Assert.Equal(3, (await tickets.AllAsync()).Count);

        // Upstream's INVALID_LOCATOR for filter({}) and nth(-1) is the C# argument check.
        Assert.Throws<ArgumentNullException>(() => tickets.Filter((TextMatch)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => tickets.Nth(-1));
    });

    [Fact]
    public Task Display_value_queries_follow_the_value() => RunAsync(async screen =>
    {
        await Expect.That(screen.GetByDisplayValue("SAVE10")).ToBeVisibleAsync();
        await Expect.That(screen.GetByDisplayValue("SAVE10")).ToHaveValueAsync("SAVE10");

        await screen.GetByLabel("Coupon").FillAsync("FREESHIP");
        await Expect.That(screen.GetByDisplayValue("SAVE10")).ToHaveCountAsync(0);
        await Expect.That(screen.GetByDisplayValue("FREESHIP")).ToHaveValueAsync("FREESHIP");
        Assert.Equal("FREESHIP", await screen.GetByLabel("Coupon").InputValueAsync());
    });

    [Fact]
    public Task ScrollIntoView_brings_a_node_far_below_into_the_viewport() => RunAsync(async screen =>
    {
        var footnote = screen.GetByText("Footnote", exact: true);
        await Expect.That(screen.GetByLabel("Footnote state")).ToHaveTextAsync("out of view");

        await footnote.ScrollIntoViewAsync();
        await Expect.That(screen.GetByLabel("Footnote state")).ToHaveTextAsync("in view");
        var box = await footnote.BoundingBoxAsync();
        Assert.NotNull(box);
        Assert.True(box.Y >= 0);
    });

    [Fact]
    public Task A_missing_locator_fails_as_LOCATOR_NOT_FOUND_after_its_timeout() => RunAsync(async screen =>
    {
        var error = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button", "Nope").TapAsync(new ActionOptions { Timeout = TimeSpan.FromMilliseconds(500) }));
        Assert.Equal("NOT_FOUND", error.Code);
        await Expect.That(screen.GetByRole("button", "Nope")).ToHaveCountAsync(0);
        Assert.Equal(0, await screen.GetByRole("button", "Nope").CountAsync());
    });

    // Upstream's beforeEach opens /controls.
    private static async Task RunAsync(Func<Screen, Task> test)
    {
        using var site = await TinySite.StartAsync(ControlsPage);
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")),
            BaseUrl = site.Url,
            TestTitle = "controls",
            ActionTimeout = TimeSpan.FromSeconds(5),
            AssertionTimeout = TimeSpan.FromSeconds(5),
        });
        await session.App.OpenAsync("/controls");
        await test(session.Screen);
        session.Complete(null);
    }
}
