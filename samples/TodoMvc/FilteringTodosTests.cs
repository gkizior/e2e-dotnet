// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace TodoMvc;

public sealed class FilteringTodosTests : TodoMvcTest
{
    [Test]
    public async Task Shows_only_active_todos()
    {
        await AddTodosAsync("Active 1", "Active 2", "Will complete");

        await Todo("Will complete").GetByRole("checkbox").CheckAsync();
        await Expect.That(Todo("Will complete").GetByRole("checkbox")).ToBeCheckedAsync();

        await Agent.ActAsync("show only the active todos");
        await Expect.Poll(() => Browser.UrlAsync()).ToSatisfyAsync(url => url.EndsWith("#/active", StringComparison.Ordinal), "the URL ends with #/active");
        await Expect.That(Todo("Active 1")).ToBeVisibleAsync();
        await Expect.That(Todo("Active 2")).ToBeVisibleAsync();
        await Expect.That(Todo("Will complete")).ToBeHiddenAsync();
    }
}
