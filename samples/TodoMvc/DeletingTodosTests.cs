// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace TodoMvc;

public sealed class DeletingTodosTests : TodoMvcTest
{
    [Test]
    public async Task Deletes_a_single_todo()
    {
        await AddTodosAsync("Task to delete");

        await Agent.ActAsync("delete the todo Task to delete");
        await Expect.That(Todos).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Deletes_one_todo_and_keeps_the_others()
    {
        await AddTodosAsync("Task 1", "Task 2", "Task 3");

        // Every row has a Delete button, so a locator names the row. The button shows while the pointer is
        // over the row, and the click on the row moves the pointer there.
        await Todo("Task 2").ClickAsync();
        await Todo("Task 2").GetByRole("button", "Delete").ClickAsync();
        await Expect.That(Todo("Task 2")).ToBeHiddenAsync();
        await Expect.That(Todo("Task 1")).ToBeVisibleAsync();
        await Expect.That(Todo("Task 3")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByText("2 items left")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Clears_completed_todos()
    {
        await AddTodosAsync("Task 1", "Task 2", "Task 3");

        await Todo("Task 1").GetByRole("checkbox").CheckAsync();
        await Todo("Task 3").GetByRole("checkbox").CheckAsync();
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();

        await Agent.ActAsync("clear the completed todos");
        await Expect.That(Todos).ToHaveCountAsync(1);
        await Expect.That(Todo("Task 2")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByText("1 item left")).ToBeVisibleAsync();
    }
}
