// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ClosedShadowLocators;

/// <summary>The pages of upstream's fixture app (<c>tests/helpers/fixture-app.ts</c>) the closed-root locator tests open.</summary>
internal static class FixtureApp
{
    /// <summary>
    /// A closed root behind a host, holding a hint paragraph and its hidden twin, a placeholder input carrying a
    /// test id, a labelled input with a value, an input named by <c>aria-labelledby</c>, a named button and its
    /// hidden twin, and two <c>Twin</c> buttons beside a third in the light DOM. Below the form an open root nests
    /// inside the closed one, with a button and a second <c>aria-labelledby</c> input, and a closed root inside
    /// that; a second host in the light DOM nests a closed root inside an open one. Submitting writes the verdict
    /// into the light DOM.
    /// </summary>
    private const string ClosedForm = """
        <!doctype html>
        <html>
        <head><title>Fixture Closed Form</title></head>
        <body>
        <h1>Access</h1>
        <button type="button" data-testid="twin-light">Twin</button>
        <x-access></x-access>
        <x-side></x-side>
        <p id="status">locked</p>
        <script>
          const root = document.querySelector('x-access').attachShadow({ mode: 'closed' });
          root.innerHTML = [
            '<p data-testid="hint">Access code hint: SHADOW-42</p>',
            '<p hidden data-testid="hint-ghost">Access code hint: SHADOW-42</p>',
            '<input placeholder="Access code" data-testid="code">',
            '<label>Nickname <input name="nickname" value="ada"></label>',
            '<span id="pin-label">PIN</span><input aria-labelledby="pin-label" data-testid="pin">',
            '<button type="button" data-testid="submit">Submit</button>',
            '<button type="button" hidden data-testid="submit-ghost">Submit</button>',
            '<button type="button" data-testid="twin-a">Twin</button>',
            '<button type="button" data-testid="twin-b">Twin</button>',
            '<section aria-label="Advanced"><x-inner></x-inner></section>',
          ].join('');
          const submit = () => {
            const code = root.querySelector('input').value;
            document.getElementById('status').textContent = code === 'SHADOW-42' ? 'granted' : 'denied';
          };
          root.querySelector('[data-testid="submit"]').addEventListener('click', submit);
          root.querySelector('input').addEventListener('keydown', (event) => { if (event.key === 'Enter') submit(); });
          const inner = root.querySelector('x-inner').attachShadow({ mode: 'open' });
          inner.innerHTML = '<button type="button">Open inside closed</button><span id="tone-label">Tone</span><input aria-labelledby="tone-label" data-testid="tone"><x-deep></x-deep>';
          const deep = inner.querySelector('x-deep').attachShadow({ mode: 'closed' });
          deep.innerHTML = '<button type="button">Closed inside open</button>';
          const side = document.querySelector('x-side').attachShadow({ mode: 'open' });
          side.innerHTML = '<x-side-closed></x-side-closed>';
          const sideClosed = side.querySelector('x-side-closed').attachShadow({ mode: 'closed' });
          sideClosed.innerHTML = '<button type="button">Sidecar</button>';
        </script>
        </body>
        </html>
        """;

    /// <summary>
    /// One closed root whose host precedes a light-DOM match of the same query, so the reader's in-place order and
    /// a light-first order differ.
    /// </summary>
    private const string ClosedOrder = """
        <!doctype html>
        <html>
        <head><title>Fixture Closed Order</title></head>
        <body>
        <h1>Order</h1>
        <x-widget></x-widget>
        <button type="button">Beta</button>
        <script>
          const root = document.querySelector('x-widget').attachShadow({ mode: 'closed' });
          root.innerHTML = '<button type="button">Alpha</button>';
        </script>
        </body>
        </html>
        """;

    /// <summary>Starts the app: <c>/closed-form</c> and <c>/closed-order</c>.</summary>
    public static Task<TinySite> StartAsync()
    {
        return TinySite.StartAsync(context => TinySite.RespondAsync(context, context.Request.Url!.AbsolutePath == "/closed-order" ? ClosedOrder : ClosedForm));
    }
}
