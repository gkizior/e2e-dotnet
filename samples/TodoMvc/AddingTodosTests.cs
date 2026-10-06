// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace TodoMvc;

public sealed class AddingTodosTests : TodoMvcTest
{
    [Test]
    public async Task Adds_a_single_todo()
    {
        await AddTodosAsync("Buy groceries");

        await Expect.That(Screen.GetByRole("textbox", "What needs to be done?")).ToHaveValueAsync("");
        await Expect.That(Todo("Buy groceries").GetByRole("checkbox")).Not.ToBeCheckedAsync();
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Counts_each_todo_as_it_is_added()
    {
        await AddTodosAsync("Buy milk");
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();

        await AddTodosAsync("Walk the dog");
        await Expect.That(Screen.GetByText("2 items left")).ToBeVisibleAsync();

        await AddTodosAsync("Finish report");
        await Expect.That(Screen.GetByText("3 items left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Keeps_special_characters()
    {
        await AddTodosAsync("Buy @groceries & supplies (urgent!)");
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Trims_whitespace_from_a_new_todo()
    {
        await Agent.ActAsync("add a todo with this exact text, spaces included: {text}", new ActOptions
        {
            Params = new Dictionary<string, object?> { ["text"] = "   Todo with spaces   " },
        });

        await Expect.Poll(() => Todos.GetByTestId("todo-title").TextContentAsync()).ToBeAsync("Todo with spaces");
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Ignores_an_empty_todo()
    {
        await AddTodosAsync("Buy milk");

        await Agent.ActAsync("submit the new todo field while it is empty");
        await Expect.That(Todos).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Ignores_a_todo_with_only_spaces()
    {
        await AddTodosAsync("Buy milk");

        await Agent.ActAsync("submit a new todo whose text is only spaces: {text}", new ActOptions
        {
            Params = new Dictionary<string, object?> { ["text"] = "   " },
        });

        await Expect.That(Todos).ToHaveCountAsync(1);
    }
}
