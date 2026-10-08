// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Lifecycle;

/// <summary>The pages of upstream's fixture app (<c>tests/helpers/fixture-app.ts</c>) the lifecycle tests open.</summary>
internal static class FixtureApp
{
    public const string Home = """
        <!doctype html>
        <html>
        <head><title>Fixture Home</title></head>
        <body>
        <h1>Home</h1>
        <input id="readonly" readonly>
        <div id="class-card" class="card active" data-extra="node-only">Card</div>
        </body>
        </html>
        """;

    public const string Form = """
        <!doctype html>
        <html>
        <head><title>Fixture Form</title></head>
        <body>
        <h1>Form</h1>
        <label>First <input name="first" value="alpha"></label>
        <label>Second <input name="second" value="beta"></label>
        <label>Third <input name="third" value="alpha"></label>
        </body>
        </html>
        """;

    // Three controls share one value; the last shares it as a textarea.
    public const string Values = """
        <!doctype html>
        <html>
        <head><title>Fixture Values</title></head>
        <body>
        <h1>Values</h1>
        <label>First <input name="first" value="shared"></label>
        <label>Second <input name="second" value="shared"></label>
        <label>Third <textarea name="third">shared</textarea></label>
        <label>Other <input name="other" value="different"></label>
        </body>
        </html>
        """;

    public const string Contents = """
        <!doctype html>
        <html>
        <head><title>Fixture Contents</title></head>
        <body>
        <h1>Checkout</h1>
        <form id="checkout" style="display:contents">
          <label>Email <input name="email"></label>
          <label>First name <input name="first"></label>
        </form>
        </body>
        </html>
        """;

    // Every node of interest twice: a hidden copy first, then the copy a person sees.
    public const string Twins = """
        <!doctype html>
        <html>
        <head><title>Fixture Twins</title></head>
        <body>
        <h1>Twins</h1>
        <section id="stale" style="display:none">
          <p>No memories yet</p>
          <input aria-label="Memory search" placeholder="Search memory..." value="alpha">
          <span data-testid="memory-empty">empty</span>
          <button>Save</button>
        </section>
        <section id="live">
          <p>No memories yet</p>
          <input aria-label="Memory search" placeholder="Search memory..." value="alpha">
          <span data-testid="memory-empty">empty</span>
          <button>Save</button>
        </section>
        <p style="visibility:hidden">Decorative twin</p>
        <p aria-hidden="true">Decorative spinner</p>
        <div id="contents-twin" style="display:contents;visibility:hidden">Contents twin</div>
        <label for="required-name">Display name<span aria-hidden="true">*</span></label>
        <input id="required-name">
        <label for="infix-name">Team <span aria-hidden="true">&bull;</span> name</label>
        <input id="infix-name">
        <label for="mixed-name">Mixed<span style="display:none">secret</span><span aria-hidden="true">*</span></label>
        <input id="mixed-name">
        <label for="overridden">Visible label</label>
        <input id="overridden" aria-label="Override">
        <label for="two-labels">First label</label>
        <label for="two-labels">Second label</label>
        <input id="two-labels">
        <label for="labeled-button">Run the check</label>
        <button id="labeled-button">Go</button>
        <label for="score">Score</label>
        <meter id="score" value="0.5"></meter>
        <button id="insert-field" onclick="const l=document.createElement('label');l.textContent='Inserted';const i=document.createElement('input');i.id='inserted';l.htmlFor='inserted';document.body.prepend(i);document.body.prepend(l);">Insert a field</button>
        <p>Decorative twin</p>
        <p id="contents-shown">Contents twin</p>
        <div data-testid="memory-panel" style="visibility:hidden"><span style="visibility:visible">Open</span></div>
        <div data-testid="memory-panel"><span>Open</span></div>
        </body>
        </html>
        """;

    private static readonly Dictionary<string, string> Pages = new(StringComparer.Ordinal)
    {
        ["/"] = Home,
        ["/form"] = Form,
        ["/values"] = Values,
        ["/contents"] = Contents,
        ["/twins"] = Twins,
    };

    /// <summary>Starts the app. <c>/slow</c> answers with the home page after five seconds, long enough to cancel first.</summary>
    public static Task<TinySite> StartAsync()
    {
        return TinySite.StartAsync(async context =>
        {
            var path = context.Request.Url!.AbsolutePath;
            if (path == "/slow")
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            await TinySite.RespondAsync(context, Pages.GetValueOrDefault(path, Home));
        });
    }
}
