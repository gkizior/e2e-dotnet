// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.WebReplacedNodes;

/// <summary>
/// Ported from upstream's <c>web-replaced-nodes.test.ts</c>, whose tests run a project through the CLI. The port runs each
/// body in its own session. The code that "fails toBeHidden on the live field" accepts is <c>ASSERTION_FAILED</c> only: the
/// port has no <c>LOCATOR_NOT_FOUND</c> code for a last sample that lands on a replaced node.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ReadsOfANodeReplacedEveryFrameTests
{
    private const int Reads = 10;

    // A field a framework re-renders on every animation frame: the element is swapped for an identical clone, so exactly
    // one visible match is in the document at any moment while every handle taken a frame earlier points at a detached
    // node. "City" stays put beside it. "Remove" takes the nickname out for good and stops the swap.
    private const string Replaced = """
        <!doctype html>
        <html>
        <head><title>Replaced</title></head>
        <body>
          <label>Nickname <input data-testid="nickname" value="ada"></label>
          <label>City <input data-testid="city" value="paris"></label>
          <button id="remove">Remove</button>
          <script>
            let removed = false;
            const swap = () => {
              if (removed) return;
              const field = document.querySelector('[data-testid="nickname"]');
              const clone = field.cloneNode(true);
              clone.value = field.value;
              field.replaceWith(clone);
              requestAnimationFrame(swap);
            };
            requestAnimationFrame(swap);
            document.getElementById('remove').addEventListener('click', () => {
              removed = true;
              document.querySelector('[data-testid="nickname"]').remove();
            });
          </script>
        </body>
        </html>
        """;

    // A stable "City" field among 250 labelled fields, each swapped for a clone on every animation frame: candidates of the
    // same exact label and display-value queries that are never a match for "City".
    private const string ReplacedCrowd = """
        <!doctype html>
        <html>
        <head><title>Replaced crowd</title></head>
        <body>
          <div id="crowd"></div>
          <label>City <input data-testid="city" value="paris"></label>
          <script>
            const crowd = document.getElementById('crowd');
            for (let i = 0; i < 250; i += 1) {
              const label = document.createElement('label');
              label.textContent = 'Churn ' + i + ' ';
              const input = document.createElement('input');
              input.className = 'churn';
              input.value = 'c' + i;
              label.append(input);
              crowd.append(label);
            }
            const swap = () => {
              for (const field of document.querySelectorAll('.churn')) {
                const clone = field.cloneNode(true);
                clone.value = field.value;
                field.replaceWith(clone);
              }
              requestAnimationFrame(swap);
            };
            requestAnimationFrame(swap);
          </script>
        </body>
        </html>
        """;

    [Fact]
    public async Task Never_reads_the_live_field_as_hidden_through_a_detached_node()
    {
        await RunAsync("/replaced", async session =>
        {
            Assert.Equal(new Dictionary<string, int> { ["testId"] = 0, ["label"] = 0, ["displayValue"] = 0 }, await HiddenReadsAsync(Fields(session.Screen), Reads));
        });
    }

    [Fact]
    public async Task Never_reads_the_live_field_as_hidden_while_the_runner_is_starved()
    {
        await RunAsync("/replaced", async session =>
        {
            var hidden = await StarvedAsync(() => HiddenReadsAsync(Fields(session.Screen), 3));
            Assert.Equal(new Dictionary<string, int> { ["testId"] = 0, ["label"] = 0, ["displayValue"] = 0 }, hidden);
        });
    }

    [Fact]
    public async Task Reads_a_stable_neighbor_of_a_replaced_field_without_tripping_on_it()
    {
        await RunAsync("/replaced", async session =>
        {
            var city = new Dictionary<string, Locator>
            {
                ["label"] = session.Screen.GetByLabel("City", exact: true),
                ["displayValue"] = session.Screen.GetByDisplayValue("paris"),
            };
            Assert.Equal(new Dictionary<string, int> { ["label"] = 0, ["displayValue"] = 0 }, await HiddenReadsAsync(city, Reads));
        });
    }

    [Theory]
    [InlineData("testId")]
    [InlineData("label")]
    [InlineData("displayValue")]
    public async Task Fails_toBeHidden_on_the_live_field_by(string kind)
    {
        var error = Assert.IsType<TestException>(await Record.ExceptionAsync(() => RunAsync("/replaced", session =>
            Expect.That(Fields(session.Screen)[kind]).ToBeHiddenAsync(TimeSpan.FromMilliseconds(500)))));
        Assert.Equal("ASSERTION_FAILED", error.Code);
    }

    [Fact]
    public async Task Resolves_a_stable_field_among_churning_candidates_within_a_short_deadline()
    {
        await RunAsync("/replaced-crowd", async session =>
        {
            Locator[] city = [session.Screen.GetByLabel("City", exact: true), session.Screen.GetByDisplayValue("paris")];
            for (var round = 0; round < 5; round++)
            {
                foreach (var field in city)
                {
                    await Expect.That(field).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(1));
                    await Expect.That(field).ToHaveCountAsync(1, TimeSpan.FromSeconds(1));
                }
            }

            foreach (var absent in new[] { session.Screen.GetByLabel("Nickname", exact: true), session.Screen.GetByDisplayValue("ada") })
            {
                await Expect.That(absent).ToBeHiddenAsync(TimeSpan.FromSeconds(1));
            }
        });
    }

    [Fact]
    public async Task Still_reads_a_genuinely_removed_field_as_hidden()
    {
        await RunAsync("/replaced", async session =>
        {
            await Expect.That(session.Screen.GetByTestId("nickname")).ToBeVisibleAsync();
            await session.Screen.GetByRole("button", "Remove").TapAsync();
            foreach (var field in Fields(session.Screen).Values)
            {
                await Expect.That(field).ToBeHiddenAsync();
                Assert.True(await field.IsHiddenAsync());
            }
        });
    }

    private static Dictionary<string, Locator> Fields(Screen screen)
    {
        return new Dictionary<string, Locator>
        {
            ["testId"] = screen.GetByTestId("nickname"),
            ["label"] = screen.GetByLabel("Nickname", exact: true),
            ["displayValue"] = screen.GetByDisplayValue("ada"),
        };
    }

    // How many of the reads of each locator report it hidden.
    private static async Task<Dictionary<string, int>> HiddenReadsAsync(Dictionary<string, Locator> locators, int reads)
    {
        var hidden = new Dictionary<string, int>();
        foreach (var (kind, locator) in locators)
        {
            hidden[kind] = 0;
            for (var read = 0; read < reads; read++)
            {
                if (await locator.IsHiddenAsync())
                {
                    hidden[kind]++;
                }
            }
        }

        return hidden;
    }

    // Runs the body while every thread pool thread is blocked in 20 ms slices, so a frame passes between any two protocol
    // calls, as on a loaded CI runner.
    private static async Task<T> StarvedAsync<T>(Func<Task<T>> body)
    {
        var stopped = false;
        void Block(object? state)
        {
            Thread.Sleep(20);
            if (!Volatile.Read(ref stopped))
            {
                ThreadPool.QueueUserWorkItem(Block);
            }
        }

        for (var i = 0; i < Environment.ProcessorCount; i++)
        {
            ThreadPool.QueueUserWorkItem(Block);
        }

        try
        {
            return await body();
        }
        finally
        {
            Volatile.Write(ref stopped, true);
        }
    }

    // Opens one page of the fixture and runs the body in a session of its own. A failure of the body reaches the caller.
    private static async Task RunAsync(string path, Func<E2ESession, Task> body)
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, path == "/replaced" ? Replaced : ReplacedCrowd));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = new ScriptedModel(_ => ModelResponses.Done("passed", "unused")),
            BaseUrl = site.Url,
            TestTitle = "replaced nodes",
            ActionTimeout = TimeSpan.FromSeconds(5),
            AssertionTimeout = TimeSpan.FromSeconds(4),
            TestTimeout = TimeSpan.FromSeconds(90),
        });
        await session.App.OpenAsync(path);
        try
        {
            await body(session);
        }
        catch (Exception ex)
        {
            session.Complete(ex);
            throw;
        }

        session.Complete();
    }
}
