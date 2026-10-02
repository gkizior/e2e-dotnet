# E2E for .NET

Community [.NET](https://dotnet.microsoft.com) port of [e2e](https://github.com/tester-army/e2e), the agentic end-to-end testing framework. Describe a goal in natural language, let an agent drive the app, then check the result with locators.

This is **not** an official TesterArmy product and is not endorsed by TesterArmy. It reimplements the public testing flow. It does not copy the TypeScript sources. Behavior that is intentionally different is listed in [COMPATIBILITY.md](COMPATIBILITY.md).

License: Apache License 2.0. Copyright 2026 TesterArmy.

## Install

```bash
dotnet add package E2E
```

The library targets `net10.0` and includes `WebEngine`, which drives Chromium, Firefox, and WebKit through [Microsoft.Playwright](https://playwright.dev/dotnet/). The CLI is a .NET tool:

```bash
dotnet tool install -g E2E.Cli
```

Install a browser once, from the build output of the project that references `E2E`:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

## A test

```csharp
using E2E;

[E2ESuite("billing")]
public sealed class BillingTests
{
    [E2ETest("a member upgrades to Pro")]
    public async Task Upgrades(App app, Agent agent, Screen screen)
    {
        await app.OpenAsync("/settings/billing");
        await agent.ActAsync("upgrade the workspace to the Pro plan");
        await agent.AssertAsync("the invoice preview shows a prorated amount");
        await Expect.That(screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}
```

An `act` that a later `assert` or locator `Expect` verifies is recorded. The next run replays those actions with no model calls until the screen no longer matches. Tests that never call the agent need no model.

Run a test assembly:

```bash
e2e run tests/Billing.csproj
e2e run --grep upgrade
e2e run --no-cache
```

`e2e.config.json` selects the model. There is no default model and no shared API key.

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

## In-process host

A program can host the runner itself. `DocumentEngine` is an in-memory page, useful for samples and for tests of the runner. A real browser uses `WebEngine`.

```csharp
var result = await Runner.RunAsync(suite, new RunOptions
{
    Engine = new WebEngine(),
    Model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "gpt-4.1-mini" }),
    BaseUrl = "http://127.0.0.1:4173",
});
```

## Sample

The sample runs the billing upgrade twice. The second run replays the tap and does not ask the model to act. It uses the document engine unless you pass `--web`.

```bash
dotnet run --project samples/E2E.Sample
dotnet run --project samples/E2E.Sample -- --web
```

`--web` serves a small billing page and drives it with Playwright. It needs Chromium installed.

## Tests

```bash
dotnet test E2E.slnx -c Release
```

Unit tests use `DocumentEngine` and a scripted model. They do not need an API key or a browser. The Playwright test returns without failing when Chromium is not installed.

## What is in this port

| JavaScript | .NET |
| --- | --- |
| `e2e` test, expect, agent, cache, runner, and `@e2e-dev/web` | `E2E` (`WebEngine`) |
| `e2e` CLI | `E2E.Cli` (`e2e`) |

`@e2e-dev/mobile`, `@e2e-dev/github`, `@e2e-dev/kernel`, and `@e2e-dev/eas` are not ported. Details are in [COMPATIBILITY.md](COMPATIBILITY.md).
