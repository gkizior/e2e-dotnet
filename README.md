# E2E for .NET

Community [.NET](https://dotnet.microsoft.com) port of [e2e](https://github.com/tester-army/e2e), the agentic end-to-end testing framework. Describe a goal in natural language, let an agent drive the app, then check the result with locators.

This is **not** an official TesterArmy product and is not endorsed by TesterArmy. It reimplements the public testing flow. It does not copy the TypeScript sources. Behavior that is intentionally different is listed in [COMPATIBILITY.md](COMPATIBILITY.md).

License: Apache License 2.0. Copyright 2026 TesterArmy.

## Install

```bash
dotnet add package E2E
dotnet add package E2E.NUnit
```

The library targets `net10.0` and includes `WebEngine`, which drives Chromium, Firefox, and WebKit through [Microsoft.Playwright](https://playwright.dev/dotnet/). Install a browser once, from the build output of the project that references `E2E`:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

## A test

```csharp
using E2E;
using E2E.NUnit;
using NUnit.Framework;

public sealed class BillingTests : E2ETest
{
    protected override IAgentModel? CreateModel() =>
        new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "gpt-4.1-mini" });

    [Test]
    public async Task Member_upgrades_to_Pro()
    {
        await App.OpenAsync("/settings/billing");
        await Agent.ActAsync("upgrade the workspace to the Pro plan");
        await Agent.AssertAsync("the invoice preview shows a prorated amount");
        await Expect.That(Screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}
```

`E2ETest` starts a `WebEngine` session for each `[Test]`. Override `CreateEngine` with a `DocumentEngine` when the test should not open a browser. An `act` that a later `assert` or locator `Expect` verifies is recorded. The next run replays those actions with no model calls until the screen no longer matches. A failure deletes unverified acts. `Assert.Ignore` leaves the cache alone. `[Retry]` runs the later attempts live. Tests that never call the agent need no model.

There is no default model and no shared API key. A host can load the same shape from `e2e.config.json` with `E2EConfig.Load`.

```json
{
  "app": { "url": "http://127.0.0.1:4173" },
  "agent": {
    "model": "gpt-4.1-mini",
    "baseUrl": "https://api.openai.com/v1",
    "apiKeyEnv": "OPENAI_API_KEY"
  }
}
```

`baseUrl` can point at any OpenAI-compatible server, including a local one. Tests without agent steps ignore it.

## Sample

The sample is that billing test as an NUnit project. It uses the document engine and a scripted model, so it needs no browser and no API key. The second test replays the tap and does not ask the model to act.

```bash
dotnet test --project samples/E2E.Sample
```

## Tests

```bash
dotnet test E2E.slnx -c Release
```

Unit tests use `DocumentEngine` and a scripted model. They do not need an API key or a browser. The Playwright test returns without failing when Chromium is not installed.

## What is in this port

| JavaScript | .NET |
| --- | --- |
| `e2e` test, expect, agent, cache, and `@e2e-dev/web` | `E2E` (`WebEngine`) |
| NUnit | `E2E.NUnit` (`E2ETest`) |

`@e2e-dev/mobile`, `@e2e-dev/github`, `@e2e-dev/kernel`, and `@e2e-dev/eas` are not ported. Details are in [COMPATIBILITY.md](COMPATIBILITY.md).
