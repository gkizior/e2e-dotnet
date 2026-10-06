// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace TodoMvc;

public sealed class EditingTodosTests : TodoMvcTest
{
    [Test]
    public async Task Renames_a_todo()
    {
        await AddTodosAsync("Buy milk");

        await Agent.ActAsync("rename the todo Buy milk to Buy organic milk");
        await Expect.That(Todo("Buy organic milk")).ToBeVisibleAsync();
        await Expect.That(Todo("Buy milk")).ToBeHiddenAsync();
        await Expect.That(Todos).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Saves_an_edit_when_the_field_loses_focus()
    {
        await AddTodosAsync("Call dentist");

        await Agent.ActAsync(
            "rename the todo Call dentist to Schedule dentist appointment. Save it by clicking the todos heading, not by pressing Enter");
        await Expect.That(Todo("Schedule dentist appointment")).ToBeVisibleAsync();
        await Expect.That(Todo("Call dentist")).ToBeHiddenAsync();
    }

    [Test]
    public async Task Cancels_an_edit_with_Escape()
    {
        await AddTodosAsync("Original text");

        await Agent.ActAsync("start renaming the todo Original text to Modified text, then press Escape instead of saving");
        await Expect.That(Todo("Original text")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByText("Modified text")).ToBeHiddenAsync();
    }

    [Test]
    public async Task Deletes_a_todo_whose_text_is_cleared()
    {
        await AddTodosAsync("Temporary task");

        await Agent.ActAsync("edit the todo Temporary task, clear all of its text, and save");
        await Expect.That(Todos).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Trims_whitespace_from_an_edit()
    {
        await AddTodosAsync("Original task");

        await Agent.ActAsync("rename the todo Original task to this exact text, spaces included, and save: {text}", new ActOptions
        {
            Params = new Dictionary<string, object?> { ["text"] = "   Edited task   " },
        });
        await Expect.Poll(() => Todos.GetByTestId("todo-title").TextContentAsync()).ToBeAsync("Edited task");
    }
}
