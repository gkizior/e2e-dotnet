// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E;
using E2E.Engine;

namespace E2E.Tests.AgentActVerbs;

/// <summary>
/// The gestures fixture page, the flows run on it, a scripted model, and a session,
/// shared by the <c>agent.act</c> grammar verb tests ported from upstream
/// <c>agent-act-verbs.test.ts</c>.
/// </summary>
internal static class GesturesSession
{
    /// <summary>
    /// Upstream's /gestures page: a hover-revealed control, a right-click menu, long-press and
    /// double-tap detection, a drag target, a checkbox, a file input, a memo field whose
    /// selection the status echoes, a radio the pick replaces with its summary, a windowed
    /// ledger, and a footnote far below the fold.
    /// </summary>
    public const string GesturesPage = """
        <!doctype html>
        <html>
        <head><title>Gestures</title></head>
        <body style="margin:0;padding:90px 16px 16px">
          <div id="account" style="position:fixed;left:40px;top:40px;width:200px;height:30px;line-height:30px;background:#eee">Account</div>
          <h1>Gestures</h1>
          <a href="/about">About</a>
          <output id="state" role="status" aria-label="Gesture state">idle</output>
          <button id="redeem" hidden>Redeem</button>

          <span id="file">report.pdf</span>
          <div id="file-menu" role="menu" aria-label="File actions" hidden><button id="rename" role="menuitem">Rename</button></div>

          <button id="hold">Hold me</button>
          <button id="twice">Tap me twice</button>

          <ul aria-label="Todo column"><li id="card" draggable="true">Design review</li></ul>
          <section id="done" aria-label="Done column" style="min-height:40px;border:1px solid #000">Done column</section>

          <label for="agree">Agree to terms</label>
          <input id="agree" type="checkbox" />

          <label for="attachment">Attachment</label>
          <input id="attachment" type="file" multiple />

          <label for="memo">Memo</label>
          <input id="memo" value="release approved" />

          <fieldset id="delivery"><legend>Delivery</legend><label><input type="radio" name="delivery" value="Express" />Express</label></fieldset>

          <p>Page the ledger down to Row 24 and stop there.</p>
          <span>Jump to Row 24</span>
          <div id="ledger" role="list" aria-label="Ledger" style="position:relative;height:200px;overflow:auto;border:1px solid #000"><div id="ledger-spacer"></div></div>
          <output aria-label="Ledger state">golden out of view</output>
          <div style="height:3000px"></div>
          <p id="footnote">Footnote</p>
          <output aria-label="Footnote state">out of view</output>
          <script>
            const state = document.getElementById('state');
            const redeem = document.getElementById('redeem');
            document.getElementById('account').addEventListener('mouseenter', () => { redeem.hidden = false; });
            redeem.addEventListener('click', () => { state.textContent = 'redeemed'; });
            const menu = document.getElementById('file-menu');
            document.getElementById('file').addEventListener('contextmenu', (event) => {
              event.preventDefault();
              menu.hidden = false;
              state.textContent = 'menu open';
            });
            document.getElementById('rename').addEventListener('click', () => { menu.hidden = true; state.textContent = 'renamed'; });
            let heldAt = 0;
            const hold = document.getElementById('hold');
            hold.addEventListener('pointerdown', () => { heldAt = performance.now(); });
            hold.addEventListener('pointerup', () => { state.textContent = performance.now() - heldAt >= 400 ? 'long-pressed' : 'tapped'; });
            document.getElementById('twice').addEventListener('dblclick', () => { state.textContent = 'double-tapped'; });
            const done = document.getElementById('done');
            done.addEventListener('dragover', (event) => event.preventDefault());
            done.addEventListener('drop', (event) => { event.preventDefault(); state.textContent = 'Design review is done'; });
            document.getElementById('agree').addEventListener('change', (event) => { state.textContent = 'agreed: ' + event.target.checked; });
            const delivery = document.getElementById('delivery');
            delivery.addEventListener('change', (event) => {
              delivery.innerHTML = '<p>' + event.target.value + ' delivery selected</p>';
              state.textContent = 'delivery: ' + event.target.value;
            });
            document.getElementById('attachment').addEventListener('change', (event) => {
              state.textContent = 'attached: ' + Array.from(event.target.files, (file) => file.name).join(', ');
            });
            // A windowed list: 400 rows of 40 px exist as data, and only the rows
            // inside the container's scrolled window are in the DOM, so the golden
            // row is nowhere in the tree until the list is paged down to it.
            const ROWS = 400, ROW_PX = 40, GOLDEN = 24;
            const ledger = document.getElementById('ledger');
            document.getElementById('ledger-spacer').style.height = ROWS * ROW_PX + 'px';
            const ledgerState = document.querySelector('output[aria-label="Ledger state"]');
            const renderLedger = () => {
              for (const row of ledger.querySelectorAll('[role="listitem"]')) row.remove();
              const first = Math.floor(ledger.scrollTop / ROW_PX);
              const last = Math.min(ROWS, Math.ceil((ledger.scrollTop + ledger.clientHeight) / ROW_PX));
              for (let index = first; index < last; index += 1) {
                const row = document.createElement('div');
                row.setAttribute('role', 'listitem');
                row.setAttribute('aria-label', 'Ledger row');
                row.style.cssText = 'position:absolute;left:0;right:0;height:' + ROW_PX + 'px;top:' + index * ROW_PX + 'px';
                row.textContent = index === GOLDEN ? 'Row ' + GOLDEN + ' · Golden' : 'Row ' + index;
                ledger.appendChild(row);
              }
              ledgerState.textContent = first <= GOLDEN && GOLDEN < last ? 'golden in view' : 'golden out of view';
            };
            ledger.addEventListener('scroll', renderLedger);
            renderLedger();
            const memo = document.getElementById('memo');
            // A click parks the caret at the end: Home and End scroll the page on macOS, so a flow cannot rely on them.
            memo.addEventListener('click', () => { memo.setSelectionRange(memo.value.length, memo.value.length); });
            memo.addEventListener('select', () => { state.textContent = 'selected: ' + memo.value.slice(memo.selectionStart, memo.selectionEnd); });
            const footnoteState = document.querySelector('output[aria-label="Footnote state"]');
            new IntersectionObserver((entries) => {
              footnoteState.textContent = entries[0].isIntersecting ? 'in view' : 'out of view';
            }).observe(document.getElementById('footnote'));
          </script>
        </body>
        </html>
        """;

    /// <summary>Upstream's /about page.</summary>
    public const string AboutPage = """
        <!doctype html>
        <html>
        <head><title>About page</title></head>
        <body>
          <h1>About</h1>
          <a href="/">Home</a>
        </body>
        </html>
        """;

    /// <summary>
    /// The flows of upstream's record-then-replay pass this port can run: the instruction, the
    /// scripted model's tool call for each turn (null concludes the step), and the check after it.
    /// </summary>
    public static readonly IReadOnlyList<Flow> ReplayFlows =
    [
        new(
            "checks the box and leaves a checked box alone",
            "agree to the terms, then make sure the box stays checked",
            // The same check twice: the second finds the box already checked and flips nothing.
            (calls, prompt) => calls <= 1 ? ModelResponses.Call("check", new { @ref = RefFor(prompt, "checkbox \"Agree to terms\"") }) : null,
            async screen =>
            {
                await Expect.That(screen.GetByLabel("Agree to terms")).ToBeCheckedAsync();
                await Expect.That(screen.GetByRole("status", "Gesture state")).ToHaveTextAsync("agreed: true");
            }),
        new(
            "checks a radio the pick replaces with its summary",
            "pick Express delivery",
            (calls, prompt) => calls == 0 ? ModelResponses.Call("check", new { @ref = RefFor(prompt, "radio \"Express\"") }) : null,
            screen => Expect.That(screen.GetByRole("status", "Gesture state")).ToHaveTextAsync("delivery: Express")),
        new(
            "scrolls a listed node into view",
            "scroll to the footnote",
            (calls, prompt) => calls == 0 ? ModelResponses.Call("scroll_to", new { @ref = RefFor(prompt, "paragraph \"Footnote\"") }) : null,
            screen => Expect.That(screen.GetByLabel("Footnote state")).ToHaveTextAsync("in view")),
        new(
            "pages a windowed list to a row it has not rendered",
            "scroll the ledger to the golden row",
            (calls, prompt) => calls == 0 ? ModelResponses.Call("scroll_to", new { text = "Row 24", @ref = RefFor(prompt, "list \"Ledger\"") }) : null,
            screen => Expect.That(screen.GetByLabel("Ledger state")).ToHaveTextAsync("golden in view")),
        new(
            "opens a page and comes back",
            "open the about page and come back",
            (calls, prompt) => calls switch
            {
                0 => ModelResponses.Call("tap", new { @ref = RefFor(prompt, "link \"About\"") }),
                1 => ModelResponses.Call("back", new { }),
                _ => null,
            },
            screen => Expect.That(screen.GetByRole("heading", "Gestures")).ToBeVisibleAsync()),
    ];

    /// <summary>Serves the gestures page, and the about page at /about.</summary>
    public static Task<TinySite> StartSiteAsync()
    {
        return TinySite.StartAsync(context => TinySite.RespondAsync(
            context,
            context.Request.Url!.AbsolutePath == "/about" ? AboutPage : GesturesPage));
    }

    /// <summary>The act model for one flow: its script for the turn, then a passing verdict.</summary>
    public static ScriptedModel ModelFor(Flow flow)
    {
        var calls = 0;
        return new ScriptedModel(request =>
        {
            var prompt = string.Join('\n', request.Messages.Select(message => message.Content));
            return flow.Script(calls++, prompt) ?? ModelResponses.Done("passed", "done");
        });
    }

    /// <summary>
    /// Runs one flow as its own test: opens the gestures page, acts on the instruction, and
    /// runs the flow's check. Returns the act's result.
    /// </summary>
    public static async Task<ActResult> RunFlowAsync(TinySite site, Flow flow, ScriptedModel model, string cacheDirectory)
    {
        ActResult? result = null;
        var session = await StartAsync(site, model, new FileStepCache(cacheDirectory), flow.Title);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/gestures");
            result = await session.Agent.ActAsync(flow.Instruction);
            await flow.Check(session.Screen);
        });
        return result!;
    }

    /// <summary>The ref of the last listed node whose line matches <paramref name="line"/>.</summary>
    public static string RefFor(string prompt, string line)
    {
        return Regex.Matches(prompt, Regex.Escape(line) + @"(?: text=""[^""]*"")? \[ref=(e\d+)\]")[^1].Groups[1].Value;
    }

    public static async Task<E2ESession> StartAsync(TinySite site, ScriptedModel model, FileStepCache cache, string title)
    {
        return await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            Cache = cache,
            CacheEnabled = true,
            TestTitle = "verbs > " + title,
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
    }

    public static async Task RunAsync(E2ESession session, Func<Task> body)
    {
        await using (session)
        {
            Exception? error = null;
            try
            {
                await body();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            session.Complete(error);
            Assert.Null(error);
        }
    }
}

/// <summary>One agentic flow: what the test asks, how the scripted model answers each turn, and the check that decides it.</summary>
internal sealed record Flow(string Title, string Instruction, Func<int, string, ModelResponse?> Script, Func<Screen, Task> Check);
