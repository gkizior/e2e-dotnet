// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net;
using System.Text;

namespace E2E.Playground;

/// <summary>
/// The playground pages the testbed tests run against, served on a loopback port for the whole
/// test run. A port of upstream's <c>apps/testbed</c> with only the pages these tests use:
/// the landing page, a todo list kept in localStorage, a checkout with one planted bug, the
/// browser fixture's own page, and the about page navigation lands on. E2E.NUnit.Tests links
/// this file for its browser fixture tests.
/// </summary>
internal static class Testbed
{
    private static readonly (string Path, string Label)[] Nav =
    [
        ("/", "Home"),
        ("/todos", "Todos"),
        ("/checkout", "Checkout"),
        ("/browser", "Browser"),
        ("/about", "About"),
    ];

    private static readonly Dictionary<string, (string Title, string Body)> Pages = new(StringComparer.Ordinal)
    {
        ["/"] = ("Playground", """
            <h1>Playground</h1>
                   <p>A tiny app exercised by the e2e dogfood suite.</p>
            """),

        ["/todos"] = ("Todos", """
            <h1>Todos</h1>
                   <label for="new-todo">New todo</label>
                   <input id="new-todo" placeholder="What needs doing?" />
                   <button id="add">Add</button>

                   <div role="tablist" aria-label="Filter">
                     <button role="tab" aria-selected="true" data-filter="all">All</button>
                     <button role="tab" aria-selected="false" data-filter="open">Open</button>
                     <button role="tab" aria-selected="false" data-filter="done">Done</button>
                   </div>

                   <ul id="list" data-testid="todo-list"></ul>
                   <output role="status" aria-label="Remaining"></output>

                   <script>
                     const load = () => JSON.parse(localStorage.getItem('todos') ?? '[]');
                     const store = (todos) => localStorage.setItem('todos', JSON.stringify(todos));
                     let filter = 'all';

                     function render() {
                       const todos = load();
                       const list = document.getElementById('list');
                       list.innerHTML = '';
                       for (const [index, todo] of todos.entries()) {
                         if (filter === 'open' && todo.done) continue;
                         if (filter === 'done' && !todo.done) continue;
                         const item = document.createElement('li');
                         item.className = todo.done ? 'done' : '';
                         item.setAttribute('data-testid', 'todo');
                         const box = document.createElement('input');
                         box.type = 'checkbox';
                         box.id = 'todo-' + index;
                         box.checked = todo.done;
                         box.addEventListener('change', () => {
                           const next = load();
                           next[index].done = box.checked;
                           store(next);
                           render();
                         });
                         const label = document.createElement('label');
                         label.htmlFor = box.id;
                         label.textContent = todo.title;
                         const remove = document.createElement('button');
                         remove.textContent = 'Delete ' + todo.title;
                         remove.addEventListener('click', () => {
                           const next = load();
                           next.splice(index, 1);
                           store(next);
                           render();
                         });
                         item.append(box, label, remove);
                         list.append(item);
                       }
                       const open = load().filter((todo) => !todo.done).length;
                       document.querySelector('output').textContent = open + ' remaining';
                     }

                     function add() {
                       const input = document.getElementById('new-todo');
                       const title = input.value.trim();
                       if (title === '') return;
                       store([...load(), { title, done: false }]);
                       input.value = '';
                       render();
                     }

                     document.getElementById('add').addEventListener('click', add);
                     document.getElementById('new-todo').addEventListener('keydown', (event) => {
                       if (event.key === 'Enter') add();
                     });
                     for (const tab of document.querySelectorAll('[role=tab]')) {
                       tab.addEventListener('click', () => {
                         for (const other of document.querySelectorAll('[role=tab]')) {
                           other.setAttribute('aria-selected', String(other === tab));
                         }
                         filter = tab.dataset.filter;
                         render();
                       });
                     }
                     render();
                   </script>
            """),

        // Planted bug: the pay button keeps the total from page load, so after a
        // quantity change the summary and the button show different totals.
        ["/checkout"] = ("Checkout", """
            <h1>Checkout</h1>
                   <table aria-label="Cart">
                     <thead><tr><th>Item</th><th>Price</th><th>Quantity</th><th>Line total</th></tr></thead>
                     <tbody>
                       <tr>
                         <td>Notebook</td><td>$12.00</td>
                         <td><input id="notebook-qty" type="number" min="1" value="1" aria-label="Notebook quantity" data-price="12" /></td>
                         <td data-line>$12.00</td>
                       </tr>
                       <tr>
                         <td>Pen</td><td>$3.00</td>
                         <td><input id="pen-qty" type="number" min="1" value="2" aria-label="Pen quantity" data-price="3" /></td>
                         <td data-line>$6.00</td>
                       </tr>
                     </tbody>
                   </table>
                   <section aria-label="Order summary">
                     <h2>Order summary</h2>
                     <p>Order total: <strong id="summary-total">$18.00</strong></p>
                   </section>
                   <button id="pay">Pay $18.00</button>

                   <script>
                     const money = (amount) => '$' + amount.toFixed(2);
                     for (const input of document.querySelectorAll('input[data-price]')) {
                       input.addEventListener('input', () => {
                         let total = 0;
                         for (const row of document.querySelectorAll('tbody tr')) {
                           const qty = row.querySelector('input');
                           const line = Number(qty.dataset.price) * Number(qty.value);
                           row.querySelector('[data-line]').textContent = money(line);
                           total += line;
                         }
                         document.getElementById('summary-total').textContent = money(total);
                       });
                     }
                   </script>
            """),

        // The browser fixture's own surface: navigation with a delay, the viewport
        // size, the cookies the page sees, and a load counter for reload.
        ["/browser"] = ("Browser", """
            <h1>Browser</h1>
                   <button id="go-about">Go to about, soon</button>
                   <output aria-label="Viewport"></output>
                   <output aria-label="Cookies"></output>
                   <output aria-label="Loads"></output>
                   <output aria-label="Random"></output>
                   <script>
                     document.getElementById('go-about').addEventListener('click', () => {
                       setTimeout(() => {
                         location.assign('/about');
                       }, 400);
                     });
                     const viewport = document.querySelector('output[aria-label="Viewport"]');
                     const report = () => {
                       viewport.textContent = innerWidth + 'x' + innerHeight;
                     };
                     addEventListener('resize', report);
                     report();
                     document.querySelector('output[aria-label="Cookies"]').textContent = document.cookie || 'no cookies';
                     const loads = Number(sessionStorage.getItem('loads') ?? '0') + 1;
                     sessionStorage.setItem('loads', String(loads));
                     document.querySelector('output[aria-label="Loads"]').textContent = 'loads: ' + loads;
                     document.querySelector('output[aria-label="Random"]').textContent = 'random: ' + (Math.random() === 0.5 ? 'seeded' : 'unseeded');
                   </script>
            """),

        ["/about"] = ("About page", """
            <h1>About</h1>
                   <p>The playground, described.</p>
            """),
    };

    private static readonly Lazy<string> Server = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The base URL of the running playground, started on first use.</summary>
    public static string Url => Server.Value;

    private static string Start()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var url = "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/";
        var listener = new HttpListener();
        listener.Prefixes.Add(url);
        listener.Start();
        _ = Task.Run(() => ListenAsync(listener));
        return url.TrimEnd('/');
    }

    private static async Task ListenAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            var response = context.Response;
            if (Pages.TryGetValue(context.Request.Url?.AbsolutePath ?? "", out var page))
            {
                var bytes = Encoding.UTF8.GetBytes(Layout(page.Title, page.Body));
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            }
            else
            {
                response.StatusCode = 404;
            }

            response.Close();
        }
    }

    // The document shell every playground page shares: the stylesheet and the nav.
    private static string Layout(string title, string body)
    {
        var nav = string.Join("\n    ", Nav.Select(item => $"<a href=\"{item.Path}\">{item.Label}</a>"));
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>{{title}}</title>
              <style>
                body { font-family: system-ui, sans-serif; margin: 2rem; max-width: 640px; }
                nav a { margin-right: 0.75rem; }
                li { margin: 0.25rem 0; }
                .done label { text-decoration: line-through; }
                [hidden] { display: none !important; }
              </style>
            </head>
            <body>
              <nav aria-label="Main">
                {{nav}}
              </nav>
              {{body}}
            </body>
            </html>
            """;
    }
}
