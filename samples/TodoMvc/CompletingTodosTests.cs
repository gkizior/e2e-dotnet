// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace TodoMvc;

public sealed class CompletingTodosTests : TodoMvcTest
{
    [Test]
    public async Task Completes_a_single_todo()
    {
        await AddTodosAsync("Buy groceries");

        await Agent.ActAsync("mark Buy groceries as complete");
        await Expect.That(Todo("Buy groceries").GetByRole("checkbox")).ToBeCheckedAsync();
        await Expect.That(Screen.GetByText("0 items left")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByRole("button", "Clear completed")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Completes_several_todos()
    {
        await AddTodosAsync("Buy milk", "Walk dog", "Finish report");

        // Every row has a "Toggle Todo" checkbox, so a locator names the row.
        await Todo("Buy milk").GetByRole("checkbox").CheckAsync();
        await Todo("Finish report").GetByRole("checkbox").CheckAsync();
        await Expect.That(Todo("Buy milk").GetByRole("checkbox")).ToBeCheckedAsync();
        await Expect.That(Todo("Walk dog").GetByRole("checkbox")).Not.ToBeCheckedAsync();
        await Expect.That(Todo("Finish report").GetByRole("checkbox")).ToBeCheckedAsync();
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Marks_every_todo_complete_at_once()
    {
        await AddTodosAsync("Task 1", "Task 2", "Task 3");

        await Agent.ActAsync("mark all todos as complete with one click");
        await Expect.That(Screen.GetByText("0 items left")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByRole("button", "Clear completed")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Marks_every_todo_incomplete_at_once()
    {
        await AddTodosAsync("First todo", "Second todo", "Third todo");

        await Agent.ActAsync("mark all todos as complete with one click");
        await Expect.That(Screen.GetByText("0 items left")).ToBeVisibleAsync();

        await Agent.ActAsync("mark all todos as not complete with one click");
        await Expect.That(Screen.GetByText("3 items left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Reopens_a_completed_todo()
    {
        await AddTodosAsync("Buy groceries");

        await Agent.ActAsync("mark Buy groceries as complete");
        await Expect.That(Todo("Buy groceries").GetByRole("checkbox")).ToBeCheckedAsync();

        await Agent.ActAsync("mark Buy groceries as not complete");
        await Expect.That(Todo("Buy groceries").GetByRole("checkbox")).Not.ToBeCheckedAsync();
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();
    }
}
