// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;

namespace E2E.Tests.WebPlatform;

/// <summary>The pages of upstream's fixture app (tests/helpers/fixture-pages/home.ts) that the web platform tests open.</summary>
internal static class FixtureApp
{
    public const string Home = """
        <!doctype html>
        <html>
        <head><title>Fixture Home</title></head>
        <body>
          <h1>Home</h1>
          <a href="/about">About</a>
          <a href="/about?token=super-secret-token#frag">About with token</a>
          <button id="increment" onclick="document.getElementById('count').textContent = String(Number(document.getElementById('count').textContent) + 1)">Increment</button>
          <output id="count" role="status" aria-label="Counter">0</output>

          <label for="email">Email</label>
          <input id="email" type="email" placeholder="you@example.test" autocomplete="username" />

          <label for="readonly">Readonly</label>
          <input id="readonly" readonly value="read-only value" />

          <div id="class-card" class="card active" data-extra="node-only" constructor="own">Card</div>
          <img id="fixture-image" src="/fixture.png" alt="Fixture" />

          <label for="focus-target">Focus target</label>
          <input id="focus-target" />

          <label for="password">Password</label>
          <input id="password" type="password" autocomplete="current-password" />

          <label for="notifications">Notifications</label>
          <input id="notifications" type="checkbox" />

          <label for="plan">Plan</label>
          <select id="plan">
            <option value="free">Free</option>
            <option value="pro">Pro</option>
            <option value="team">Team</option>
          </select>

          <label for="digest">Digest</label>
          <select id="digest" multiple>
            <option value="daily">Daily</option>
            <option value="weekly">Weekly</option>
            <option value="monthly">Monthly</option>
          </select>

          <button disabled>Disabled action</button>
          <fieldset disabled>
            <legend>Fenced <button>Legend action</button></legend>
            <button>Fenced action</button>
          </fieldset>
          <div hidden>Hidden content</div>
          <svg data-testid="hidden-svg" width="40" height="40" style="visibility:hidden"><rect width="40" height="40"></rect></svg>
          <div data-testid="zero-box" style="width:0;height:0;overflow:hidden"><span>Clipped away</span></div>
          <details><summary>Folded</summary><button>Inside folded details</button></details>
          <div data-testid="empty-contents" style="display:contents"></div>
          <div data-testid="painted-contents" style="display:contents"><span>Laid out by contents</span></div>
          <button aria-expanded="false" id="menu" onclick="this.setAttribute('aria-expanded', this.getAttribute('aria-expanded') === 'true' ? 'false' : 'true')">Menu</button>

          <ul data-testid="items">
            <li data-testid="item">Item Alpha</li>
            <li data-testid="item">Item Beta</li>
            <li data-testid="item">Item Gamma</li>
          </ul>
          <span>Duplicated</span>
          <span>Duplicated</span>

          <input id="prefilled" aria-label="Prefilled" value="hello-value" />
          <textarea id="notes" aria-label="Notes">line1

        line2  </textarea>

          <script>
            setTimeout(() => {
              const late = document.createElement('button');
              late.textContent = 'Late arrival';
              document.body.appendChild(late);
            }, 400);
          </script>
        </body>
        </html>
        """;

    // One value attribute on a password input, the same on a text input, and a text input without one.
    public const string ValueAttributes = """
        <!doctype html>
        <html>
        <head><title>Value attributes</title></head>
        <body>
          <input aria-label="Secret" type="password" value="marker-5e0c" />
          <input aria-label="Plain" type="text" value="marker-5e0c" />
          <input aria-label="Blank" type="text" />
        </body>
        </html>
        """;

    private const string About = """
        <!doctype html>
        <html>
        <head><title>About page</title></head>
        <body>
          <h1>About</h1>
          <a href="/">Home</a>
        </body>
        </html>
        """;

    // Class assertions: a node that arrives late, duplicates, and the two shapes of an empty class.
    private const string Classes = """
        <!doctype html>
        <html>
        <head><title>Classes</title></head>
        <body>
          <h1>Classes</h1>
          <span class="dup">Duplicated</span>
          <span class="dup">Duplicated</span>
          <div id="blank-card" class="">Blank</div>
          <ul data-testid="items"><li>Item</li></ul>
          <script>
            setTimeout(() => {
              const card = document.createElement('div');
              card.id = 'late-card';
              card.className = 'card late';
              card.textContent = 'Late card';
              document.body.appendChild(card);
            }, 600);
          </script>
        </body>
        </html>
        """;

    // Rich-text editors: a bare contenteditable host named through aria-labelledby, and one that also carries the explicit role.
    private const string Editor = """
        <!doctype html>
        <html>
        <head><title>Editor</title></head>
        <body>
          <h1>Editor</h1>
          <span id="notes-label">Notes</span>
          <div id="notes" contenteditable aria-labelledby="notes-label" data-testid="notes"><p><br></p></div>
          <span id="message-label">Message</span>
          <div id="message" contenteditable role="textbox" aria-labelledby="message-label" data-testid="message"><p><br></p></div>
        </body>
        </html>
        """;

    public static Task<TinySite> StartAsync()
    {
        return TinySite.StartAsync(context => context.Request.Url!.AbsolutePath switch
        {
            "/" => TinySite.RespondAsync(context, Home),
            "/about" => TinySite.RespondAsync(context, About),
            "/classes" => TinySite.RespondAsync(context, Classes),
            "/editor" => TinySite.RespondAsync(context, Editor),
            "/value-attributes" => TinySite.RespondAsync(context, ValueAttributes),
            _ => NotFoundAsync(context),
        });
    }

    private static Task NotFoundAsync(HttpListenerContext context)
    {
        context.Response.StatusCode = 404;
        context.Response.Close();
        return Task.CompletedTask;
    }
}
