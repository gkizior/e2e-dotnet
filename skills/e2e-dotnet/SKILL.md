---
name: e2e-dotnet
description: Write, run, and debug agentic end-to-end tests with the .NET port of e2e (NuGet packages E2E, E2E.NUnit, E2E.XUnit.V3, E2E.Cli). Use when adding an E2E test, setting up e2e.config.json or a model provider, or fixing a failing agent step, locator expectation, or replay-cache miss.
---

# e2e for .NET

An `E2ETest` opens the app in Chromium. `Agent` runs natural-language steps with a model, and `Screen` and `Expect` check the result without one. A verified agent step is recorded and replayed on the next run with no model call.

## Set up a test project

```bash
dotnet new nunit -n MyApp.E2E
dotnet add MyApp.E2E package E2E
dotnet add MyApp.E2E package E2E.NUnit
```

For xUnit v3, create the project with `dotnet new install xunit.v3.templates` and `dotnet new xunit3`, then add the `E2E.XUnit.V3` package. Derive from `E2E.XUnit.V3.E2ETest` and write `[Fact]` tests. The members below are the same. `Expect.Soft` failures fail the test when it is disposed, and `Expect.Poll` uses `ToSatisfyAsync(predicate)`. xUnit v2 is not supported.

Targets `net10.0`. The first browser launch in a test run installs Chromium, so the first run needs network access. On a CI image that already has the browser, or with no network, set `E2E_SKIP_BROWSER_INSTALL=1` and install it yourself with `pwsh bin/Debug/net10.0/playwright.ps1 install chromium`.

## Config

Put `e2e.config.json` in the test project folder. `E2ETest` finds the nearest one above the test assembly, then above the working directory. An unknown key fails with `INVALID_CONFIG`.

```json
{
  "targets": [{ "platform": "web", "app": { "url": "https://staging.example.com" } }],
  "actionTimeout": 15000,
  "assertionTimeout": 10000,
  "agents": {
    "default": {
      "provider": "copilot",
      "model": "claude-sonnet-5.5",
      "context": "Facts about the app the model should know, such as which data it must not change."
    }
  },
  "cache": { "dir": ".e2e/cache" },
  "secrets": { "admin-password": null }
}
```

Pick the model:

| Goal | `provider` | Auth |
| --- | --- | --- |
| Claude on a subscription | `copilot` | `e2e login github-copilot` (reuses `gh auth token`) |
| OpenAI on a subscription | `chatgpt` | `e2e login openai` |
| Claude on an API key | `anthropic` | `ANTHROPIC_API_KEY`, a workspace key (`sk-ant-api03-...`) |
| OpenAI on an API key | `openai` (the default) | `OPENAI_API_KEY` |
| Other | `openai-responses`, `azure`, `google`, `bedrock`, `xai`, `openrouter`, `gateway`, `openai-compatible`, `grok`, `opencode-console` | See the README |

Rules that cause most auth failures:

- Get model ids from `e2e models <login>`. Copilot uses a dot (`claude-sonnet-5.5`). The Anthropic API uses a dash (`claude-sonnet-5-5`).
- `apiKeyEnv` overrides the provider's variable. Remove it when you change `provider`, or the client sends no key and gets a 401.
- A Claude Pro or Max plan cannot serve the agent. Use Copilot or an API key.
- Subscriptions are for local runs. Use API keys in CI.
- Install the login tool with `dotnet tool install --global E2E.Cli`. In a clone of the e2e-dotnet repo, use `dotnet run --project src/E2E.Cli -- <command>`.

A secret reads `E2E_SECRET_<NAME>` first (`E2E_SECRET_ADMIN_PASSWORD`), then the config value. `null` means the variable is required.

## Write a test

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
        await Agent.AssertAsync("the invoice preview shows a prorated amount");
        await Expect.That(Screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}
```

Members of `E2ETest`:

- `App`: `OpenAsync(path)`, `BackAsync`, `RestartAsync`, `ClearStateAsync`. A path resolves against the target URL. `OpenAsync` and the agent's navigate step admit only `http:`, `https:`, and the exact `about:blank`; any other scheme (`file:`, `data:`, `javascript:`, `view-source:file:`) is `POLICY_DENIED`.
- `Agent`: `ActAsync(instruction)` performs a goal. `AssertAsync(statement)` judges the screen once. `WaitForAsync(statement)` judges until true or timeout. `ExtractAsync<T>(instruction)` reads typed data from the screen.
- `Screen`: `GetByRole(role, name)`, `GetByText`, `GetByLabel`, `GetByPlaceholder`, `GetByTestId`, `GetByDisplayValue`. Text matches are **exact by default**; pass `exact: false` for a substring. A locator has `ClickAsync`, `FillAsync`, `PressAsync`, `SelectOptionAsync`, `CheckAsync`, `First()`, `Last()`, `Nth(i)`, and `Filter(...)`.
- `Expect.That(locator)`: `ToBeVisibleAsync`, `ToBeHiddenAsync`, `ToContainTextAsync`, `ToHaveTextAsync`, `ToHaveValueAsync`, `ToHaveCountAsync`, `ToBeEnabledAsync`, `ToBeCheckedAsync`, `ToHaveAttributeAsync`, and more. `Expect.Soft` records a failure and continues. `Expect.Poll(read)` retries any value.
- `Browser`: URL, title, cookies, viewport, and raw keyboard and mouse.
  - `RouteAsync(pattern, handler)`, `UnrouteAsync(pattern)`: intercept requests, newest route first. `route.Request` has `Url`, `Method`, `Headers`, `PostData`. The handler calls exactly one of `FulfillAsync(new RouteFulfillResponse { Status, Headers, ContentType, Json | Body | Path })`, `ContinueAsync(new RouteContinueOverrides { Url, Method, Headers, PostData })` (straight to the network), `FallbackAsync()` (the route registered before it), or `AbortAsync()`. None or two fails the next step with `ACTION_FAILED`, a bad option with `INVALID_ARGUMENT`. `Path` is relative to the project root.
  - `WaitForResponseAsync(pattern, timeout?)`: resolves once the headers arrive, with a `WebResponse` (`Url`, `Status`, `Headers`, `TextAsync()`, `JsonAsync<T>()`). `TextAsync` and `JsonAsync` wait for the body (up to the action timeout) and fail with `ACTION_FAILED` when it could not be read. Start it before the step that sends the request, and await it after.
  - `CookiesAsync()`, `SetCookiesAsync([...])`: a target is an http(s) URL, relative to the base URL, or a domain. `SetCookiesAsync` refuses a cookie URL that is not http(s), `about:blank` included, with `POLICY_DENIED`.
  - A `RouteAsync`, `UnrouteAsync`, or `WaitForResponseAsync` pattern is a glob string or a `Regex` matched against the full URL (`*` stays within one path segment, `**` crosses `/`, `?` is one character, `\` escapes the next one).
- `Secrets.Get("admin-password")`: a `Secret` from config.

Rules for agent steps:

- One goal per `ActAsync`. Write the outcome, not the clicks: "add a $5 coffee expense", not "click Add, type 5".
- Follow every `ActAsync` with `AssertAsync`, `WaitForAsync`, or `Expect.That`. Only a verified act is recorded for replay.
- Prefer `Expect.That` with a locator when the check is exact. It needs no model and does not flake.
- A value shown in more than one place (a total in the summary and on the pay button) must agree everywhere, or the judgment fails. Name the one you mean (`"the order summary total is $42.00"`) when only it matters.
- Pass data in `Params`, not in the instruction text. Use `Values.Unique(...)` for fresh emails or names, and a `Secret` for passwords. The model never sees a secret's value.

```csharp
await Agent.ActAsync("sign in as the admin", new ActOptions
{
    Params = new Dictionary<string, object?>
    {
        ["email"] = "admin@example.com",
        ["password"] = Secrets.Get("admin-password"),
    },
});
```

Mark tests that call a real model with `[Category("RealModel")]`, so CI can skip them with `--filter "TestCategory!=RealModel"`. A test with no agent call needs no model and no config entry for one.

To pin the language and time zone, so dates and numbers format the same on every machine, override `CreateEngine()` and pass `WebEngineOptions`:

```csharp
protected override IEngine CreateEngine() => new WebEngine(new WebEngineOptions
{
    Locale = "de-DE",
    TimezoneId = "Europe/Berlin",
});
```

- `Locale` is the language every attempt runs in, a BCP 47 tag: `navigator.language`, `Intl`, and `Accept-Language`. An `accept-language` entry in `Headers` beside it is `INVALID_CONFIG`; set `Locale` only.
- `TimezoneId` is the IANA time zone every attempt runs in. Spell it with its exact case (`Europe/Berlin`, not `europe/berlin`), or it is `INVALID_CONFIG`.

To test without a browser or model, override `CreateEngine()` to return a `DocumentEngine`, and `CreateModel()` to return a scripted `IAgentModel`.

## Run

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~BillingTests"
```

The first passing run records each verified act in `cache.dir`. The next run replays it with no model call. `AssertAsync` and `WaitForAsync` still call the model. `cache.mode` is `read-write` locally and `read-only` when `CI` is set. Commit the cache directory if CI should replay it.

## Debug a failure

An `E2EException` has a `Code`. Read it first.

| Code or message | Cause | Fix |
| --- | --- | --- |
| `MODEL_PROVIDER_FAILED` with 401, "x-api-key header is required", or "You didn't provide an API key" | No key reached the provider | Set the provider's variable. Remove a stale `apiKeyEnv` |
| `MODEL_PROVIDER_FAILED` with 400 "not scoped to a workspace" | An Anthropic user key (`sk-ant-usr-`) | Use a workspace key, or `provider: copilot` |
| `MODEL_PROVIDER_FAILED` with "The subscription login failed (NOT_LOGGED_IN)" or `LOGIN_REQUIRED` | No login is stored, or it was rejected | `e2e login <provider>` |
| `MODEL_PROVIDER_FAILED` with a 4xx that names the model | The plan or key does not serve that model id | Pick an id from `e2e models <login>` |
| `MODEL_UNAVAILABLE` | No model is set and no replay finished the step | Set `agents.default.model` |
| `SECRET_UNAVAILABLE` | `Secrets.Get` named a secret the config does not declare | Add it under `secrets` |
| `INVALID_CONFIG` | Unknown key or bad value in `e2e.config.json` | Fix the key the message names |
| `ASSERTION_FAILED` | The judge or locator saw a different screen | Check the app, then the statement. Do not loosen the statement to pass |
| `ASSERTION_INCONCLUSIVE` | The judge could not decide | Make the statement concrete and visible on screen |
| `STEP_BUDGET_EXHAUSTED`, `STEP_TIMEOUT` | The goal was too large or unclear | Split it into smaller `ActAsync` calls |
| `LOCATOR_NOT_FOUND`, `STRICT_MODE` | No match, or more than one match | Use the exact accessible name, or `First()`, `Nth(i)`, `Filter(...)` |
| `APP_NOT_OPEN` | A `Screen` call before `App.OpenAsync()` | Open the app first |
| `ENVIRONMENT_UNAVAILABLE` with "Chromium is not installed" or "could not install Chromium" | The browser install was skipped or failed | Allow network access for the first run, or install Chromium and set `E2E_SKIP_BROWSER_INSTALL=1` |
| `APP_UNREACHABLE` | The agent found the app down or not loading | Start the app, or fix `targets[].app.url` |
| `POLICY_DENIED` | A URL whose scheme is not `http:` or `https:` (`file:`, `view-source:`, `data:`), from `OpenAsync`, the agent's navigate step, or `SetCookiesAsync` | http(s) or `about:blank` only (no `about:blank` cookie) |
| `REPLAY_STALE` | `cache.strict` is on and a recording no longer matches | Re-run once without `cache.strict` to re-record |

A replay miss is not a failure. The step runs live and records again. `ActResult.Cache.Reason` says why it missed: `no-entry` (nothing recorded yet), `invalid-entry` (an old or broken file), `target-not-found` (the control's role or name changed), `target-ambiguous`, `wrong-context` (the page or path changed), or `end-mismatch` (the replay ended on a different screen).

Differences from the TypeScript [tester-army/e2e](https://github.com/tester-army/e2e) are listed in `COMPATIBILITY.md` in the e2e-dotnet repo.
