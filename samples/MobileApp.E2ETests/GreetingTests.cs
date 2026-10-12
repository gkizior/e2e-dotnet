// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace MobileApp.E2ETests;

/// <summary>The greeting form. The first three tests use locators only, the last two let the agent drive the app.</summary>
public sealed class GreetingTests : MobileAppTest
{
    [Test]
    public async Task Shows_the_greeting_form()
    {
        await Expect.That(Screen.GetByText("Say hello")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByTestId("name")).ToBeVisibleAsync();
        await Expect.That(Screen.GetByTestId("greet")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Asks_for_a_name_when_the_field_is_empty()
    {
        await Screen.GetByTestId("greet").TapAsync();

        await Expect.That(Screen.GetByTestId("error")).ToHaveTextAsync("Enter a name first.");
    }

    [Test]
    public async Task Greets_the_name_you_type()
    {
        await Screen.GetByTestId("name").FillAsync("Ada");
        await Screen.GetByTestId("greet").TapAsync();

        await Expect.That(Screen.GetByTestId("greeting")).ToHaveTextAsync("Hello, Ada!");
    }

    [Test]
    public async Task The_agent_gets_a_greeting()
    {
        // The agent works out which field and button to use.
        await Agent.ActAsync("get the app to greet {name}", new ActOptions
        {
            Params = new Dictionary<string, object?> { ["name"] = "Grace" },
        });

        // Each agent step is paired with a check that does not call the model.
        await Expect.That(Screen.GetByTestId("greeting")).ToHaveTextAsync("Hello, Grace!");
    }

    [Test]
    public async Task The_agent_finds_the_empty_name_error()
    {
        await Agent.ActAsync("press Greet without typing a name");

        await Expect.That(Screen.GetByTestId("error")).ToHaveTextAsync("Enter a name first.");
    }
}
