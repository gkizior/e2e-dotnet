# TodoMVC sample

This sample tests the [TodoMVC demo](https://demo.playwright.dev/todomvc) with the `E2E` and `E2E.NUnit` NuGet packages. It covers the same behavior as Playwright's [`examples/todomvc`](https://github.com/microsoft/playwright/tree/main/examples/todomvc) (Apache-2.0, Microsoft), but the agent performs the actions. The test cases and their data come from that example.

## Run it

```bash
dotnet tool install --global E2E.Cli
e2e login github-copilot
dotnet test --project samples/TodoMvc
```

The tests use `claude-sonnet-5.5` on a GitHub Copilot plan. To use an API key, change `agents.default` in `e2e.config.json`. All tests are in the `RealModel` category.

## Playwright and e2e side by side

Playwright, `should-save-edit-on-blur`:

```ts
await page.getByRole('textbox', { name: 'What needs to be done?' }).fill('Call dentist');
await page.getByRole('textbox', { name: 'What needs to be done?' }).press('Enter');
await page.getByTestId('todo-title').dblclick();
await page.getByRole('textbox', { name: 'Edit' }).fill('Schedule dentist appointment');
await page.getByRole('heading', { name: 'todos' }).click();
await expect(page.getByText('Schedule dentist appointment')).toBeVisible();
```

e2e, `Saves_an_edit_when_the_field_loses_focus`:

```csharp
await AddTodosAsync("Call dentist");
await Agent.ActAsync(
    "rename the todo Call dentist to Schedule dentist appointment. Save it by clicking the todos heading, not by pressing Enter");
await Expect.That(Todo("Schedule dentist appointment")).ToBeVisibleAsync();
```

The test describes what the user does. It does not say how to find the edit box, that a double-click opens it, or that the toggle-all checkbox's accessible name starts with `❯`. When the markup changes, the agent finds the new controls, and the cache records the new path.

The checks stay exact. `Expect.That` checks the counter, the checkboxes, the row count, and the URL with no model call.

One kind of step uses a locator instead of the agent: acting on one row when the list has several. Every row has the same "Toggle Todo" checkbox and the same Delete button, and the replay cache records a control by its role and name only. So `Todo("Task 2").GetByRole("button", "Delete")` names the row exactly, and the step replays the same way every time.

| | Playwright | e2e |
| --- | --- | --- |
| Tests | 23 | 20 |
| Lines of test code | 625 | 310 |
| Selectors in tests | One per control, such as `'❯Mark all as complete'`, `getByTestId('todo-title')`, and `.todo-list li` | The `todo-item` test id, plus the checkbox and Delete button inside a row |

## What changed from the Playwright suite

- **Merged duplicates.** "Add a single todo", "add multiple todos", and "prevent empty todo" each appeared in both `adding-todos/` and `todo-creation/`.
- **Fixed a test with no checks.** `should-uncomplete-completed-todo` toggled a todo twice and asserted nothing. `Reopens_a_completed_todo` checks the checkbox and the counter.
- **Fixed a test that missed the deletion.** `should-delete-specific-todo-from-multiple` never checked that Task 2 was gone. `Deletes_one_todo_and_keeps_the_others` does.

## The tradeoff

The first run asks the model to perform each `ActAsync`. A passing run records each verified step in `.e2e/cache`, and the next run replays it with no model call. The cache is not committed, so your first run records it.

On one machine, the first run of all 20 tests took about 4 minutes. The next run replayed every step and took 17 to 32 seconds.

`AssertAsync` calls the model on every run, so this sample avoids it. The model judges the accessibility tree, not pixels, so it cannot check styling such as strike-through text.
