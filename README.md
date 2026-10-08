# E2E for .NET

Community [.NET](https://dotnet.microsoft.com) port of [e2e](https://github.com/tester-army/e2e), the agentic end-to-end testing framework. Describe a goal in natural language, let an agent drive the app, then check the result with locators.

This is **not** an official TesterArmy product and is not endorsed by TesterArmy. It reimplements the public testing flow. It does not copy the TypeScript sources. Behavior that is intentionally different is listed in [COMPATIBILITY.md](COMPATIBILITY.md).

License: Apache License 2.0. Copyright 2026 TesterArmy.

Docs and API reference: https://hardkoded.github.io/e2e-dotnet/

## Install

```bash
dotnet add package E2E
dotnet add package E2E.NUnit   # or E2E.XUnit.V3 for xUnit v3
```

The library targets `net10.0` and includes `WebEngine`, which drives Chromium through [Microsoft.Playwright](https://playwright.dev/dotnet/). Firefox and WebKit are not exposed yet. The first `WebEngine` launch in a process installs Chromium, so the first run needs network access. A headless run installs only the headless shell, the build it launches. When that build is already installed, the step does nothing. The install keeps other browsers in the Playwright cache; set `PLAYWRIGHT_SKIP_BROWSER_GC=0` to let it remove them. On a machine that already has the browser, or has no network, set `E2E_SKIP_BROWSER_INSTALL=1` to skip it, and install it yourself from the build output:

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

`E2ETest` starts a `WebEngine` session for each `[Test]`. Override `CreateEngine` with a `DocumentEngine` when the test should not open a browser, or with a `WebEngine` whose `Connect` attaches to a remote Chromium over CDP (see [Recovering a CDP transport](#recovering-a-cdp-transport)). An `act` that a later `assert`, `waitFor`, or locator `Expect` verifies is recorded. The next run replays those actions with no model calls until the screen no longer matches. When the test ends, verified acts are written and unverified acts that recorded or replayed are evicted, whether it passed, failed, or was skipped. `[Retry]` runs the later attempts live, and they still record. Tests that never call the agent need no model.

### Recovering a CDP transport

`WebConnectOptions.ReconnectEndpoint` opts into recovery of the same remote browser after its CDP transport disconnects. This mode uses the browser's persistent default context, because Chrome deletes ordinary Playwright contexts when their transport detaches.

```csharp
new WebEngine(new WebEngineOptions
{
    Connect = new WebConnectOptions
    {
        CdpEndpoint = ct => hosted.ProvisionAsync(ct),
        ReconnectEndpoint = ct => hosted.EndpointAsync(ct),
    },
});
```

In this mode, `CdpEndpoint` provisions a fresh, dedicated browser at every attempt start, including retries. The engine rejects a browser reused by a previous attempt of the same `WebEngine`. The remote must have only its default context, and the host owns the browser: disposing the session closes the connection, not the browser. The attempt's first page is the browser's own first tab, navigated to `about:blank`.

After a disconnect, the next operation calls `ReconnectEndpoint` once, bounded by the action timeout and its cancellation token. This resolver must return the existing browser's endpoint. The engine verifies the default context id and the original page's target id; a URL match is not enough to pick a replacement tab. Recovery also requires the page's frames to keep the closed shadow root hook, which a document that navigated while disconnected lost. A different browser, a missing page, or a resolver that runs out of time fails the attempt, and every later operation fails the same way. Recovery never repeats an operation that was already dispatched. Route handlers and init scripts, configured and added, are reattached, and each document still runs every init script once. After a reconnect, `PerformAsync`, the agent's key press, and the viewport swipe fail with `NODE_STALE` until the screen is observed again; the `Browser` keyboard and mouse keep working.

Persistent recovery does not support `Headers`, `BasicAuth`, `UserAgent`, `Locale`, or `TimezoneId` (`INVALID_CONFIG`), or `App.ClearStateAsync` (`UNSUPPORTED_CAPABILITY`). `App.RestartAsync` remains available. Omit `ReconnectEndpoint` to keep a new, isolated context per attempt.

### xUnit v3

`E2E.XUnit.V3.E2ETest` has the same members and reads the same config. Write `[Fact]` or `[Theory]` tests. It differs from the NUnit fixture in a few ways:

- `Expect.Soft` failures fail the test when it is disposed, in one `ASSERTION_FAILED`.
- xUnit has no retry, so every test is a first attempt and can replay.
- The session already stops on `Xunit.TestContext.Current.CancellationToken`, so a call does not need it. You can turn off the analyzer rule `xUnit1051` for E2E calls.
- `Expect.Poll` takes a predicate through `ToSatisfyAsync`. There is no `ToMatchAsync`.
- `E2E` and `Xunit` both have a `TestContext`. With both `using` lines, write `Xunit.TestContext.Current` for the xUnit one.

xUnit v2 is not supported. It cannot read a test's result during cleanup, and the replay cache needs that result.

## Config

`E2ETest` reads the nearest `e2e.config.json` above the test assembly directory, then above the working directory. The shape follows upstream `e2e.config.ts`. Every key is optional, and an unknown key fails with `INVALID_CONFIG`.

```json
{
  "targets": [{ "platform": "web", "app": { "url": "http://127.0.0.1:4173" } }],
  "timeout": 120000,
  "launchTimeout": 60000,
  "actionTimeout": 30000,
  "assertionTimeout": 5000,
  "cleanupTimeout": 30000,
  "agents": {
    "default": {
      "model": "gpt-4.1-mini",
      "baseUrl": "https://api.openai.com/v1",
      "apiKeyEnv": "OPENAI_API_KEY"
    }
  },
  "cache": { "mode": "read-write", "dir": ".e2e/cache", "strict": false },
  "secrets": { "stripe-key": null }
}
```

There is no default model and no shared API key. `baseUrl` can point at any OpenAI-compatible server, including a local one. Tests without agent steps ignore it.

`provider` picks who serves `model` and `judge`. Unset, it is `openai` (chat completions). `baseUrl` and `apiKeyEnv` default to the provider's own:

| `provider` | Key | Notes |
| --- | --- | --- |
| `openai` | `OPENAI_API_KEY` | Chat completions |
| `openai-responses` | `OPENAI_API_KEY` | The Responses API, as the AI SDK's `openai(id)` |
| `azure` | `AZURE_API_KEY` | Responses API at `https://$AZURE_RESOURCE_NAME.openai.azure.com/openai/v1`, or `baseUrl`. `model` is the deployment |
| `anthropic` | `ANTHROPIC_API_KEY` | Messages API, with prompt-cache breakpoints |
| `google` | `GOOGLE_GENERATIVE_AI_API_KEY` | Gemini API |
| `bedrock` | AWS credentials, or `AWS_BEARER_TOKEN_BEDROCK` | Converse API in `AWS_REGION` |
| `xai` | `XAI_API_KEY` | SpaceXAI API |
| `openrouter` | `OPENROUTER_API_KEY` | Model ids such as `openai/gpt-6-luna-fast` |
| `gateway` | `AI_GATEWAY_API_KEY`, else `VERCEL_OIDC_TOKEN` | Vercel AI Gateway |
| `openai-compatible` | `LLM_API_KEY`, optional | Any `/v1/chat/completions` server; `baseUrl` is required |
| `chatgpt`, `copilot`, `grok`, `opencode-console` | The stored login | Subscriptions, below |

An `apiKeyEnv` you set wins over the provider's default, so remove it when you switch providers. Otherwise the client reads the old variable, sends no key, and the provider answers 401.

`anthropic` needs a key scoped to a workspace (`sk-ant-api03-...`). A user key (`sk-ant-usr-...`) fails with a 400 that asks for an `anthropic-workspace-id` header, and the config cannot send one.

In code, `ModelProviders` builds the same clients (`ModelProviders.Anthropic("claude-sonnet-5")`), and `AnthropicModel`, `GoogleModel`, `BedrockModel`, `OpenAiResponsesModel`, and `OpenAiCompatibleModel` take full options.

### Subscriptions

A ChatGPT Plus/Pro, GitHub Copilot, OpenCode Console (Zen and Go), or SuperGrok/X Premium+ plan can serve the agent instead of an API key. Sign in once with the `e2e` tool:

```bash
dotnet tool install --global E2E.Cli
e2e login openai            # or github-copilot, opencode-console, spacexai
e2e models openai           # the model ids the plan serves
```

Then set `"provider": "chatgpt"` (or `copilot`, `opencode-console`, `grok`) with one of those ids, or use `Subscriptions.ChatGpt("gpt-6-luna")` in code. `e2e login openai --device` works without a browser, and `e2e login github-copilot` reuses `gh auth token` (or takes `--client-id` of your own OAuth App, and `--enterprise-url` for GitHub Enterprise). The login is stored in `~/.config/e2e/oauth.json`, the file upstream's `npx e2e login` writes, so a login made with either tool serves both. `E2E_OAUTH_CREDENTIALS` holding that JSON stands in for the file, and `OPENCODE_API_KEY` replaces an OpenCode Console login. Use API keys in CI.

A Claude Pro or Max plan cannot serve the agent. Anthropic allows that login only in its own apps. To run Claude on a subscription, use GitHub Copilot. Copilot writes model ids with a dot (`claude-sonnet-5.5`), where the Anthropic API uses a dash (`claude-sonnet-5-5`), so take the id from `e2e models github-copilot`:

```json
"agents": { "default": { "provider": "copilot", "model": "claude-sonnet-5.5" } }
```

From a clone of this repo, run the tool with `dotnet run --project src/E2E.Cli -- login github-copilot`.

An agent entry also takes `judge` (the model id for `assert`, `waitFor`, and `extract`), `system`, `context`, `maxSteps`, `maxModelCalls`, `judgmentTimeout`, and `providerOptions`. `agents` can name more agents than `default`; a call picks one with its `Agent` option, such as `new ActOptions { Agent = "careful" }`.

`cache.mode` is `off`, `read-only`, or `read-write`. Unset, it is `read-write` locally and `read-only` when `CI` is set. `cache.strict` fails a recording that no longer matches with `REPLAY_STALE` instead of running the step live. `cache.dir` resolves against the config file's directory.

Each secret reads `E2E_SECRET_<NAME>` first, then the config value. `null` means the variable is required. A test gets one with `Secrets.Get("stripe-key")`. Secret values passed to an act never enter cache entries: a name or test id that shows one is stored as `<secret:name>`.

A fixture overrides any value with the matching property, such as `BaseUrl`, `CacheMode`, or `ActionTimeout`, or replaces the whole config by overriding `Config`.

## Coding agents (MCP)

`e2e mcp` serves the project to a coding agent such as Claude Code or Cursor over MCP (stdio). Register it once:

```bash
claude mcp add e2e -- e2e mcp
```

The server has four fixed tools (`open_session`, `tools`, `call`, `close_session`) and serves the skill as the resources `e2e://guide` and `e2e://guide/<topic>`. Live sessions are not ported yet: `open_session` answers `UNSUPPORTED_CAPABILITY`. The flags are `--config`, `--target`, `--headed`, and `--max-sessions` (1 through 16, default 4). See [COMPATIBILITY.md](COMPATIBILITY.md#mcp-server).

## Sample

The sample is a real end-to-end test of [Dariten](https://dariten.vercel.app), a public personal finance demo. It drives Chromium through `WebEngine`, which installs the browser on its first run. The demo data is shared and anyone can edit it, so the tests only read the app and never check specific balances.

- `The_dashboard_links_to_the_transaction_register` uses locators only. It needs no model.
- `An_agent_filters_the_register_by_category` asks the agent to filter the register, then judges the result. It runs `claude-sonnet-5.5` on a GitHub Copilot plan and is in the `RealModel` category, which CI skips. A second passing run replays the filter without asking the model to act.

Sign in once with the GitHub CLI already logged in, then run the sample:

```bash
dotnet run --project src/E2E.Cli -- login github-copilot
dotnet test --project samples/E2E.Sample
```

To use an API key instead, change `agents.default` in `samples/E2E.Sample/e2e.config.json`, for example to `"provider": "openai", "model": "gpt-4.1-mini"` with `OPENAI_API_KEY` set.

[`samples/E2E.XUnit.V3.Sample`](https://github.com/hardkoded/e2e-dotnet/tree/main/samples/E2E.XUnit.V3.Sample) has the same two tests for xUnit v3. Run it with `dotnet test --project samples/E2E.XUnit.V3.Sample`.

Runs are headless. To watch a run in a browser window, set `E2E_HEADLESS=0`, for example `E2E_HEADLESS=0 dotnet test --project samples/E2E.Sample`.

[`samples/TodoMvc`](https://github.com/hardkoded/e2e-dotnet/tree/main/samples/TodoMvc) ports Playwright's TodoMVC example to agent steps, using the NuGet packages. Its README compares the two.

## Tests

```bash
dotnet test E2E.slnx -c Release
```

Most unit tests use `DocumentEngine` and a scripted model. No test needs an API key. The Playwright tests install Chromium on the first launch, so the first run needs network access.

## What is in this port

| JavaScript | .NET |
| --- | --- |
| `e2e` test, expect, agent, cache, and `@e2e-dev/web` | `E2E` (`WebEngine`) |
| NUnit | `E2E.NUnit` (`E2ETest`) |
| xUnit v3 | `E2E.XUnit.V3` (`E2ETest`) |
| `e2e login`, `e2e logout`, `e2e models`, `e2e guide`, `e2e mcp` | `E2E.Cli` (the `e2e` .NET tool) |

`@e2e-dev/mobile`, `@e2e-dev/github`, `@e2e-dev/kernel`, and `@e2e-dev/eas` are not ported. Details are in [COMPATIBILITY.md](COMPATIBILITY.md).
