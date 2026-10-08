---
name: e2e-dotnet
description: Write, run, and debug agentic end-to-end tests with the .NET port of e2e (NuGet packages E2E, E2E.NUnit, E2E.XUnit.V3, E2E.Cli). Use when adding an E2E test, setting up e2e.config.json or a model provider, or fixing a failing agent step, locator expectation, or replay-cache miss.
---

# e2e for .NET

An `E2ETest` opens the app in Chromium. `Agent.ActAsync` drives one goal with
a model; `Agent.AssertAsync`, `Agent.WaitForAsync`, and `Agent.ExtractAsync`
judge the screen. `Screen`, `App`, `Browser`, and `Expect` make exact
interactions and checks without a model. A verified agent step is recorded
and replayed on the next run with no model call; judgments still run live.
Model sign-in commands are in
[setup](references/setup.md#subscriptions-and-api-keys).

```json
{
  "targets": [{ "platform": "web", "app": { "url": "http://127.0.0.1:3000" } }],
  "agents": { "default": { "provider": "copilot", "model": "claude-sonnet-5.5" } }
}
```

```csharp
using E2E;
using E2E.NUnit;

public sealed class BillingTests : E2ETest
{
    [Test]
    public async Task Member_upgrades_to_Pro()
    {
        await App.OpenAsync("/settings/billing");
        await Agent.ActAsync("upgrade the workspace to the Pro plan");
        await Expect.That(Screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}
```

## Topics

Read the topic for the job before writing code. The files sit next to this
one; the installed tool prints the same text with `e2e guide <topic>`
(`e2e guide` alone prints this page), and `e2e mcp` serves it as
`e2e://guide/<topic>`. Differences from the TypeScript
[tester-army/e2e](https://github.com/tester-army/e2e) are listed in
`COMPATIBILITY.md` in the e2e-dotnet repo.

| Topic | File | Read it when |
| --- | --- | --- |
| `setup` | [references/setup.md](references/setup.md) | Adding e2e to a project, writing `e2e.config.json`, picking a model provider, browser options |
| `writing-tests` | [references/writing-tests.md](references/writing-tests.md) | Writing or fixing tests: fixture members, locators, actions, matchers, sign-in, the `Browser` member |
| `writing-tests-nunit` | [references/writing-tests-nunit.md](references/writing-tests-nunit.md) | NUnit: the `E2E.NUnit` base class, `[Test]`, `[Category("RealModel")]`, `dotnet test --filter`, a complete test |
| `writing-tests-xunit` | [references/writing-tests-xunit.md](references/writing-tests-xunit.md) | xUnit v3: the `E2E.XUnit.V3` base class, `[Fact]`, `[Trait("TestCategory", "RealModel")]`, `dotnet test --filter`, a complete test |
| `agent` | [references/agent.md](references/agent.md) | Adding `Agent` steps, picking a model, budgets, the replay cache |
| `running` | [references/running.md](references/running.md) | `dotnet test` filters, the `e2e` tool, exit codes, CI |
| `explore` | [references/explore.md](references/explore.md) | Exploring an app toward a goal without a test file (not ported) |
| `debugging` | [references/debugging.md](references/debugging.md) | A run failed: error codes and their fixes, a visible browser, replay misses |
| `mcp` | [references/mcp.md](references/mcp.md) | Driving the live app from a coding agent over MCP: `e2e mcp`, its tools, and the explore-then-write loop |
| `bug-bash` | [references/bug-bash.md](references/bug-bash.md) | Asked to bug bash or hunt for bugs: proving each bug with a repro test |

## Workflow

1. Look at what exists: `e2e.config.json`, a test project that references
   `E2E.NUnit` or `E2E.XUnit.V3`. Nothing there: follow `setup`.
2. Learn the screens before writing a test: routes, labels, roles, button
   text. Semantic locators need the accessible names the app renders, so read
   the components or watch a run with `E2E_HEADLESS=0`.
3. Write an `E2ETest` class. Drive the flow with `Agent.ActAsync`, one goal
   per call, and pin each outcome right after with `Expect.That` or
   `Agent.AssertAsync`. Exact values go through `Screen`.
4. Run one class: `dotnet test --filter "FullyQualifiedName~<Class>"`. Agent
   steps need a model in the config and that provider's authentication.
   Tests without agent steps need no model.
5. Read the exception's `Code` and message (topic `debugging`). Fix the
   locator, the expectation, or the app. Never add a sleep.

## Rules

- Text matches are exact by default. A locator that matches two nodes
  fails with `STRICT_MODE`; narrow it (topic `writing-tests`).
- Actions wait for readiness and `Expect` retries. Reads such as
  `TextContentAsync` look once; use a matcher when a value has to settle.
- Secrets never appear in test code. Declare them under `secrets` in the
  config and read them with `Secrets.Get(name)`; accounts come from
  `Credentials.User(name)`. Hand the `Secret` only to `FillAsync` or
  `ActOptions.Params`.
- Agent instructions: one goal per act, the wording on screen, real values
  in `Params`. Judge meaning, not phrasing.
- Check each act's outcome. Only a verified act is recorded and replayed
  (topic `agent`).
- Shape the agent for this app: `context` for the vocabulary the screens
  use, `system` for how it works, named agents under `agents`.
- `.e2e/cache/` is output; commit it to share replays, never edit it.
