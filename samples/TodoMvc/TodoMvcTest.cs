// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.NUnit;

namespace TodoMvc;

/// <summary>
/// Opens https://demo.playwright.dev/todomvc before each test. The list lives in
/// the browser's storage, and every test starts in a fresh browser context, so
/// each test starts with an empty list.
/// </summary>
[Category("RealModel")]
public abstract class TodoMvcTest : E2ETest
{
    [SetUp]
    public Task OpenTodoMvcAsync() => App.OpenAsync();

    /// <summary>Every todo row. The footer filters are list items too, so rows are found by test id.</summary>
    protected Locator Todos => Screen.GetByTestId("todo-item");

    /// <summary>The row for one todo.</summary>
    protected Locator Todo(string title) => Todos.Filter(title);

    /// <summary>Asks the agent to add the todos in order, then checks that each one is listed.</summary>
    protected async Task AddTodosAsync(params string[] titles)
    {
        await Agent.ActAsync("add these todos, in order: {titles}", new ActOptions
        {
            Params = new Dictionary<string, object?> { ["titles"] = titles },
        });

        foreach (var title in titles)
        {
            await Expect.That(Todo(title)).ToBeVisibleAsync();
        }
    }
}
