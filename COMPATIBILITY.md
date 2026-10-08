# Compatibility

Upstream parity target: [tester-army/e2e](https://github.com/tester-army/e2e) commit `94ddbfe46faa5cf7b9159fd0a604aa4317056051` (`fix(config): refuse secrets or credentials sharing an override env var (#721)`). This file was audited against it on 2026-10-03.

Names are C# versions of the JavaScript API: `agent.act` is `ActAsync`, `screen.getByRole` is `GetByRole`, `expect(locator).toContainText` is `Expect.That(locator).ToContainTextAsync`, and `unique()` is `Values.Unique`.

## Package map

| JavaScript | .NET |
| --- | --- |
| `e2e` and `@e2e-dev/web` | `E2E` (`WebEngine` is Playwright, in the same package) |
| NUnit `[Test]` | `E2E.NUnit.E2ETest`. The fixture commits the replay cache from the NUnit result |
| xUnit v3 `[Fact]` | `E2E.XUnit.V3.E2ETest`. The fixture commits the replay cache from the xUnit result |
| `e2e.config.ts` | `e2e.config.json`, the same keys as JSON. `E2ETest` finds and applies it; fixture properties override it |
| Vercel AI SDK model | `OpenAiCompatibleModel`, `OpenAiResponsesModel`, `AnthropicModel`, `GoogleModel`, and `BedrockModel`, built from `provider` in JSON or `ModelProviders` in code. See [Models](#models) |
| `e2e/oauth/chatgpt`, `copilot`, `grok`, `opencode-console` | `E2E.OAuth.Subscriptions` |
| `e2e login`, `e2e logout`, `e2e models`, `e2e guide`, `e2e mcp` | `E2E.Cli`, a .NET tool whose command is `e2e`. `e2e guide [topic]` prints the bundled skill, as upstream; an unknown topic exits 2. `e2e mcp` is partly ported: see [MCP server](#mcp-server) |
| — | `DocumentEngine`, an in-memory page for hosts that do not want a browser |

## Ported

- NUnit `[Test]`, `[SetUp]`, `[TearDown]`, `[OneTimeSetUp]`, and `[OneTimeTearDown]`
- `Assert.Ignore`, `[Retry]`, `[Timeout]`, and `[Category]`
- Screen queries: role, text, label, test id, placeholder, display value, `filter` (`hasText` and `has`), `first`, `last`, `nth`. A locator offers every query kind, scoped to its matches' descendants. Every query takes a `TextMatch`: a string or a .NET `Regex`, tested against the whitespace-normalized text. A string matches the whole text, case-sensitively, unless `exact` is false, which makes it a case-insensitive substring, as upstream. `RoleOptions` carries `name`, `checked`, `disabled`, `selected`, `expanded`, `pressed`, and `level`, and `img` is read as `image`. A role query never matches a hidden node; the other kinds keep hidden nodes unless `Visible` is set, as upstream. `getByLabel` matches any node whose accessible name matches, as the upstream reference resolver does. Options are C# objects (`RoleOptions`, `TextMatchOptions`) instead of object literals, and `filter({ hasText, has })` is the two overloads `Filter(TextMatch)` and `Filter(Locator)`. Regular expressions use .NET syntax, not ECMAScript. `GetByTestId` also takes `Exact`, which upstream's `getByTestId` does not
- Actions: tap, click (an alias of tap), doubleTap, fill (a string or a `Secret`), pressSequentially, press, check, uncheck, clear, selectOption, focus, scrollIntoView. They wait up to the action timeout for exactly one enabled, visible match. `selectOption` takes a string that matches an option value or label. `ActionOptions.Timeout` overrides the wait per action (`PressSequentiallyOptions` adds `Delay`). The web engine's own Playwright wait still uses the configured action timeout
- Locator reads: `textContent`, `inputValue`, `getAttribute`, `isVisible`, `isHidden`, `isEnabled`, `isDisabled`, `isChecked`, `boundingBox`, `count`, `all`, `allTextContents`. They read the current screen once and do not verify an earlier `act`. Single-node reads fail with `NOT_FOUND` when nothing matches instead of waiting
- `locator.waitFor` with `attached`, `detached`, `visible` (default), and `hidden`, as `WaitForAsync(new LocatorWaitForOptions { State, Timeout })`. The timeout defaults to the action timeout. `LocatorWaitForOptions` is named apart from `agent.waitFor`'s `WaitForOptions`. Differences: a timeout fails with `TIMEOUT` (upstream `LOCATOR_NOT_FOUND`, `locator did not become <state>`), and a passing `waitFor` does not verify an earlier `act` (upstream it does)
- Locator expectations: visible, hidden, attached, enabled, disabled, checked, selected, expanded, focused, text, contained text, value, attribute, accessible name, and count. They poll until the assertion timeout, or the matcher's own `timeout`. `Not` inverts a matcher, which then passes after 1000 ms of continuous truth (or the whole budget when it is shorter). Text matchers take the same `TextMatch` as queries, `ignoreCase`, and a list form. The boolean flags are `visible`, `attached`, `enabled`, and `isChecked` (`checked` is a C# keyword)
- `expect.soft` for every locator expectation, as `Expect.Soft(locator)`, including `.Not` and the per-matcher `timeout`. An `ASSERTION_FAILED` is kept and the body runs on; any other error still throws. `E2ETest` records each one on the NUnit result, as inside `Assert.EnterMultipleScope`, so the test fails when the body ends and lists every failure. `E2E.XUnit.V3.E2ETest` keeps them on the session and fails the test when it is disposed. Other hosts set `E2ESessionOptions.OnSoftFailure` or call `E2ESession.CloseSoftFailures`
- `expect.poll` as `Expect.Poll(read, options)`, with `Timeout`, `Interval`, and `Message`. A read that throws is retried. The matchers are `ToBeAsync`, `ToSatisfyAsync`, and `.Not`; `E2E.NUnit` adds `ToMatchAsync` for any NUnit constraint, such as `Is.GreaterThan(3)`. The default timeout is 5 seconds, not the configured assertion timeout, because the poll does not see the running test, and it does not stop at the test deadline unless a cancellation token is passed
- `agent.act`, `agent.assert`, `agent.waitFor`, `agent.extract`. `ActOptions` has `Params`, `Timeout`, `MaxSteps`, `MaxModelCalls`, and `Agent`; `AssertOptions` has `Timeout` and `Agent`; `WaitForOptions` has `Timeout`, `Interval`, `MaxModelCalls`, and `Agent`; `ExtractOptions` has `Timeout` and `Agent`. See [Agents](#agents)
- Act tools `observe`, `scroll`, `scroll_to`, and `back`. They are offered when the engine declares `EngineCapabilities.Scroll` or `EngineCapabilities.History`. `scroll_to` with a target scrolls it into view. With text, it pages the viewport, or the target list, until a node reading the text is listed, then scrolls it into view. It stops when the screen stops moving
- Replay cache for a verified `act`: role, name, test id, and path. Modes are `self-finalized`, `agent-concluded`, and `missed`
- `Values.Unique` and `Secret`. `Values.Unique` rejects an empty or whitespace value, and a value that contains the cache slot marker (U+0001, the port's form of `{{param:`). A secret value has at least 6 code points: `Secret.Create` throws `INVALID_ARGUMENT`, a config secret `INVALID_CONFIG`, and `Credentials.User` `INVALID_CONFIG` for a short `E2E_USER_*_PASSWORD`. The values of the secrets an `act` received in `Params` are redacted from every later prompt of the attempt, including the `assert`, `waitFor`, and `extract` statement and the agent's `system` and `context`. A secret filled through a locator, or read with `Secrets.Get` and never passed to `act`, is not added to that list. Redaction matches any case, JSON escapes, HTML character references, percent encoding, and collapsed inner whitespace. A known marker is never rewritten. The same secrets are redacted from the node names and test ids an `act` records as targets and end anchors in its replay cache entry, and a target, on replay or from the model, is matched against the screen in that redacted form. A failed secret fill in `WebEngine` reports the Playwright message with the value and its fragments of 8 or more characters redacted. Not ported: decoding base64 runs, and values cut short at a length limit
- `app.open`, `app.back`, `app.restart`, `app.clearState`, and `app.baseUrl` (`App.BaseUrl`), and the context `platform`
- The `browser` fixture subset: `reload`, `back`, `forward`, `url`, `title`, `waitForURL`, `evaluate`, `waitForResponse`, `route`, `unroute`, `cookies`, `setCookies`, `setViewport`, `addInitScript`, `keyboard.press`, `keyboard.type`, and `mouse.move`, `wheel`, `down`, `up`
- Agent budgets: `ActOptions.MaxSteps`, `ActOptions.MaxModelCalls`, and `WaitForOptions.MaxModelCalls`. A per-call budget can only lower the agent's configured one. A higher or non-positive value throws `INVALID_ARGUMENT`
- An `act` past its action budget ends `STEP_BUDGET_EXHAUSTED`, blocked. The model is told and may still conclude: a passing verdict, or a failure without a code, becomes the budget error. Replayed actions draw on the same budget. `navigate`, `back`, `scroll`, and `scroll_to` take a slot; `observe` does not. An `act` that needs one model call more than its budget also ends `STEP_BUDGET_EXHAUSTED`, blocked (`agent.act exhausted its model-call budget of N`)
- `ActResult.ModelCalls` (0 for a full replay) and `ActResult.Actions` (replayed and live actions, counting failed attempts)
- `AgentException.Blocked` and `AgentException.Explanation` (the same text as `Message`)
- OpenAI-compatible tool calling

## Replay cache

- A passing locator expectation, `agent.assert`, or `agent.waitFor` after `act` verifies it, and a verified act is written. Locator reads, `locator.waitFor`, and `agent.extract` verify nothing. `agent.assert`, `waitFor`, and `extract` always run live. As upstream, verification stops when the test fails, so a check in a derived `[TearDown]` after a failure records nothing.
- Replay runs `back` and a viewport scroll as recorded, re-finds a scrolled list before each repeat, and pages again for a `scroll_to` text. Consecutive identical scrolls are recorded as one action with a repeat count. `observe` is not recorded. Upstream scrolls the viewport when a list that filled the screen cannot be re-found. Replay does not use node geometry, so a lost list stops the replay.
- When a replay ends in `end-mismatch` and the agent repairs it with more actions, the entry is evicted instead of rewritten; the next clean run records the flow again.
- The key holds the cache schema, the replay policy version, the test, the engine, the instruction, the params, the agent's name, a SHA-256 digest of the agent's `context` with the known secrets redacted, and the repeat index. Another agent, or a changed context, records again. The repeat index counts per agent and context. Replay policy version 2 re-keys every entry, so the first run after the upgrade records them again.
- A recording checks the step's delta, as upstream: up to eight nodes that appeared and up to eight that went away, alerts and status messages first, each with its value (never a secure field's), secrets redacted from names and values, and its `checked`, `expanded`, `pressed`, and `selected` states. A node counts as gone only when no node on the end screen still matches its anchor. A replay passes on its own only when the end route matches, every appeared node is present, every gone node is absent, at least one of those changes happened during the replay (counted from the first screen the replay saw on the route it ended on), and no alert shows that was not there before and that the recording never saw. A value the replay typed counts as a change only when the step changed nothing else. An alert's dates, times, durations, and ids are read as placeholders. A step that changed no node and stayed on its route records nothing, and an entry read for an act that then passed with nothing to record is evicted.
- As upstream, each side lists alerts and status messages first, then leaves, then containers. It skips text that reads differently on every run (ids, durations, dates, clock times, bare numbers, and a count that is the data rather than the step's result) while a stable anchor remains. An anchor is a visible node with a name, a text, a test id, or a placeholder, and records its role, name, text (when it differs from the name), test id, placeholder, input purpose, value, and states. A node the other fields identify is matched without its test id, so a test id that changed between renders is forgiven; a node with only a test id is matched by it. The screen a step passed on is read once it holds still (two reads 100 ms apart agree, for at most 1 s). A node whose name or text holds a `Values.Unique` value is not an anchor.
- `ActResult.Cache` carries `ReplayedActions`, `TotalActions`, and `NotRecorded` (`param-collision`), as upstream's step cache detail does. A store whose read throws, or that returns an entry of another schema or with no actions, is a miss with `invalid-entry`.
- Only the first attempt replays. A `[Retry]` attempt runs live and still records, and its acts report `missed` with reason `retry`. Each `[Repeat]` iteration is a first attempt.
- A recording that opens with `navigate` replays from any route. Any other recording needs the start route.
- At the end of an attempt that passed, failed, or was skipped, verified acts are written and the unverified acts that recorded or replayed are evicted. An act that missed the cache and then failed leaves its key alone. A model outage or a cancelled test writes and evicts nothing.
- An entry that a verified replay finished, or that already holds the same flow, is not rewritten.
- A replay waits up to `ReplayTimeout` (15 s; a .NET-only fixture property, not a config key) for each recorded target and for the recorded end state. Upstream waits 15 s for the end route and sizes the anchor wait from the recording.
- `cache.mode` `read-only` replays but never writes or evicts, including the `end-mismatch` eviction. `off` neither replays nor records. Under `cache.strict`, a stale recording fails the act with `REPLAY_STALE` and stays in place.

## Defaults

| Setting | Default | Upstream |
| --- | --- | --- |
| Test timeout (`timeout`) | 120 s | Same |
| `launchTimeout` | 60 s | Same |
| `actionTimeout` | 30 s | Same |
| `assertionTimeout` | 5 s | Same |
| `cleanupTimeout` | 30 s | Same |
| `maxModelCalls` | 25 | Same |
| `maxSteps` (actions per `act`) | 25 | Same |
| `judgmentTimeout` (`assert`, `waitFor`, `extract`) | 30 s | Same |
| `waitFor` interval | 3 s. After the first judgment, the judge runs again only on a changed screen | Same |
| `act` timeout | 30 s (`StepTimeout`, .NET-only) | The test timeout |
| Replay wait | 15 s (`ReplayTimeout`, .NET-only) | 15 s for the end route; the anchor wait is sized from the recording |
| `expect.poll` timeout | 5 s | The assertion timeout |

`E2EDefaults` holds these values, except the `expect.poll` timeout (`PollOptions`); `E2EConfig`, `E2ESessionOptions`, and `E2ETest` start from it.

## Error codes that differ

| | Upstream | .NET |
| --- | --- | --- |
| A locator action, read, or expectation matched more than one node | `LOCATOR_AMBIGUOUS` | `STRICT_MODE` |
| A locator action or read matched no node | `LOCATOR_NOT_FOUND` | `NOT_FOUND` |
| `locator.waitFor` timed out | `LOCATOR_NOT_FOUND` | `TIMEOUT` |

## Not ported

- The `e2e` command-line tool apart from `login`, `logout`, `models`, `guide`, and `mcp`: the custom runner, `[E2ETest]` discovery, `--grep`, `test.only`, and the JSON report
- `@e2e-dev/mobile`, `@e2e-dev/github`, `@e2e-dev/kernel`, `@e2e-dev/eas`
- `e2e init`
- Vision, screenshots, traces, and video
- Parallel workers and serial suites
- The full locator action set (`dragTo`, `swipe`, `hover`, `setInputFiles`, `secondaryTap`, `longPress`, pointer points). `secondaryTap` and `longPress` have no counterpart in the document engine
- The screen-level `screen.tapAt`, `screen.swipe`, and `screen.scrollUntilVisible`
- Per-action options other than `timeout` (and `delay` for `pressSequentially`): click `modifiers`, tap `position`, and the `{ label, value, index }` form of `selectOption`
- Route patterns that ignore a record id. This port compares the URL path exactly and ignores the query and fragment
- Diff-only observations. Each model turn receives a full text snapshot
- Telemetry
- Browser fixture members `goto`, `locator`, `frameLocator`, `onDialog`, `waitForDownload`, and the `expect(browser)` matchers (`toHaveURL`, `toHaveTitle`, `toHaveClass`)
- Value expectations (`expect(value).toBe`, `toEqual`, `toMatchObject`, `toHaveProperty`, `toMatchSchema`, and the rest) and their `expect.soft` form. Use NUnit `Assert.That` with constraints, and `Assert.EnterMultipleScope` (or `Assert.Multiple`) for soft value checks. `expect.poll` takes NUnit constraints through `ToMatchAsync` in their place
- Asymmetric matchers (`expect.any`, `anything`, `objectContaining`, `arrayContaining`, `stringContaining`, `stringMatching`). Use NUnit constraints such as `Is.InstanceOf`, `Is.Not.Null`, `Has.Property`, `Is.SupersetOf`, `Does.Contain`, and `Does.Match`
- The upstream reporter, GitHub pull request comment, and trace viewer

## MCP server

Partly ported. `e2e mcp` serves over stdio with the server name `e2e`, the tools, resources, and logging capabilities, and upstream's four fixed tools with their names, descriptions, and closed argument schemas. Stdout carries only the protocol; every other write goes to stderr, and the MCP SDK logs nothing. The flags are `--config`, `--target`, `--headed`, a hidden `--headless`, and `--max-sessions` (1 through 16, default 4). A bad command line exits with 2, a disconnect or a signal with 0, and an unexpected error with 1. The resources `e2e://guide` and `e2e://guide/<topic>` serve the bundled skill; an unknown topic fails with `UNKNOWN_TOPIC`.

Differences:

- The fixed tools check their arguments with a strict check of the JSON Schema subset tool schemas use, since the .NET MCP SDK does not validate them. The messages are zod's, so an unknown argument reads `Invalid arguments for tool <name>: Unrecognized key: "<key>"`, as upstream.
- The server instructions name `e2e.config.json`, `E2ETest` classes, and `dotnet test` in place of `e2e.config.ts`, `tests/*.e2e.ts`, and `npx e2e run`.
- The guide has upstream's eight topics, written for .NET, plus `writing-tests-nunit` and `writing-tests-xunit`, one per test framework. `explore` and `bug-bash` say that `e2e explore` is not ported.
- The 2026-07-28 MCP revision deprecates logging. The server still declares it and sends its log lines, as upstream does.

Not ported yet:

- Live sessions. `open_session` answers `UNSUPPORTED_CAPABILITY`. `tools` and `call` answer `NO_SESSION`, and `close_session` answers `No session is open.`, as upstream does with no session open. `--config`, `--target`, `--headed`, and `--max-sessions` are accepted and have no effect yet.
- The session catalog behind `call`: `observe`, the grammar verbs, `type_secret`, `locate`, `screenshot` and the point tools, `start_recording` and `stop_recording`, and project tools.
- The idle and lifetime limits of a session, redaction of what user code prints, telemetry, and `e2e init` registering the server.
- The Playwright driver in the tool package. The tool still ships without it.

## Agents

`E2ESessionOptions` describes the default agent with `Model`, `Judge`, `AgentSystem`, `AgentContext`, `MaxSteps`, `MaxModelCalls`, `JudgmentTimeout`, and `ProviderOptions`, and names the others in `Agents` (an `AgentOptions` per name). `E2ETest` fills both from `agents.<name>` in `e2e.config.json`. A call picks an agent with its `Agent` option; an empty or unknown name throws `INVALID_ARGUMENT` (`unknown agent "x"; configured: default, ...`). Each agent starts from the defaults and inherits nothing from another. Settings are checked when the session starts and fail with `INVALID_CONFIG`.

- `judge` judges `assert`, `waitFor`, and `extract`, and defaults to the model. `act` always uses the model
- `system` is appended to the built-in act rules. Judges never see it
- `context` (at most 16384 UTF-8 bytes) is told to every model call: after the act rules as `Project context:`, and to judges inside `<project-context>`
- `maxSteps` and `maxModelCalls` are 1 through 100, 25 by default
- `judgmentTimeout` (30 s) bounds `assert`, `waitFor`, and `extract` unless the call sets `Timeout`. A per-call timeout must be positive; a `waitFor` interval is 100 ms through 60 s
- `providerOptions` is a dictionary of JSON objects by provider. It rides every `ModelRequest`, and `OpenAiCompatibleModel` adds the fields under its `Provider` key (`openai` by default) to the chat-completions body as given, so write the wire names (`reasoning_effort`, not `reasoningEffort`). It cannot replace `model`, `messages`, or `tools`. Every other client does the same with its own key (`anthropic`, `google`, `bedrock`, `azure`, `xai`, `openrouter`, `gateway`) and refuses its own core fields. As upstream, requests to OpenAI (and Azure over Responses) carry `store: false` and a prompt cache key per system prompt, and Anthropic requests carry cache breakpoints on the system prompt and the newest message; provider options win over both
- `assert` and `extract` make one model call and one repair round. `waitFor` judges at once, then again only after `Interval` and on a changed screen, and every call, repairs included, counts against `MaxModelCalls`. It ends `STEP_TIMEOUT` (`waitFor timed out; last judgment: ...`) or `STEP_BUDGET_EXHAUSTED` (`waitFor exhausted its model-call budget; last judgment: ...`)
- As upstream, the judge prompt says a value shown in more than one place holds only when every place agrees, unless the statement names the place or is about some item among several. The wording follows upstream, with "instruction" as "statement" and the `"fails"` verdict as "false", because the port's judge answers through `done`
- `ExtractAsync<T>`: the type argument is the schema, in place of a Standard Schema. The judge gets the JSON schema of `T` (`JsonSchemaExporter`), and the answer must deserialize into `T` with required members and nullable annotations respected. A failure gets one repair round with the validation error, then `MODEL_OUTPUT_INVALID` (`extracted data failed schema validation: ...`). A judge that says the data is not shown ends `ASSERTION_INCONCLUSIVE` (`nothing to extract: ...`)
- `act` also offers a `double_tap` tool, which upstream does not have. Without it, the agent cannot open a control that reacts only to a double-click, such as a TodoMVC label that opens its editor. It runs the locator `doubleTap`, and the replay cache records it as `doubleTap`. Upstream and older versions of this port cannot replay that entry, so they run the step live
- `act` params are checked as upstream: JSON-safe values, at most 32 levels deep, no cycle, and at most 64 KiB once a `Secret` is projected to its name and purpose and a `Values.Unique` to its value. The instruction is at most 8192 UTF-8 bytes. Each limit throws `INVALID_ARGUMENT` with the upstream message before any model call. A value reached twice through different paths is not a cycle

Not ported:

- `maxObservationBytes` and `maxInputTokens`. The port sends each observation whole, so it has nothing to cut; the keys are rejected in `e2e.config.json`
- `vision` on judgments, `screenshot` on `assert`, and `AgentError.screenshot`. The port has no pixels (see Vision under [Not ported](#not-ported))
- `executor` (a custom brain) and project `tools`. The agent loop is not pluggable
- Pinning an agent for a whole test. Name it on each call instead

## Config

`E2ETest` loads the nearest `e2e.config.json` above the test assembly directory, then above the working directory, once per run. Supported keys: `targets` (one entry with `name`, `platform`, and `app.url`), `timeout`, `launchTimeout`, `actionTimeout`, `assertionTimeout`, `cleanupTimeout`, `retries`, `agents`, `cache` (`mode`, `dir`, `strict`), and `secrets`. Defaults, the CI cache demotion to `read-only`, `E2E_SECRET_<NAME>` overrides, the 6 code point secret minimum, and `INVALID_CONFIG` for unknown keys follow upstream. Keys of the old .NET shape (`app`, `engine`, `agent`, `timeouts`) fail with a hint to the upstream key.

Differences:

- JSON has no engine handles or model instances. The fixture's `CreateEngine` chooses the engine, `platform` must be `web`, and `targets` holds one entry. `agents.<name>.model` and `judge` are model ids for the .NET-only `provider` (see [Models](#models)), with the .NET-only `baseUrl` and `apiKeyEnv` beside them. A subscription provider refuses `baseUrl` and `apiKeyEnv`. `E2ETest` passes `agents.default` as the session's own agent settings and every other entry through `CreateAgents`
- A secret set to `null` reads only `E2E_SECRET_<NAME>`. Provider functions do not exist in JSON. A test reads secrets with `Secrets.Get(name)`
- `retries` is validated and resolved (1 in CI, 0 elsewhere) but NUnit retries still come from `[Retry]`
- `cache.strict` treats any replay that finds a recording but does not finish it as stale, and leaves that recording in place
- A launch or cleanup timeout fails with `ENVIRONMENT_UNAVAILABLE`
- Not supported, and rejected with `INVALID_CONFIG`: `projectId`, `tests`, `failOnSkippedFailure`, `workers`, `artifacts`, `output`, `trace`, `video`, `reporters`, `credentials`, `cache.store`, the non-URL `app` keys, and the agent keys `tools`, `executor`, `maxObservationBytes`, and `maxInputTokens`. Credentials still come from `E2E_USER_<NAME>_USERNAME` and `_PASSWORD`

## Web engine

`WebEngine` launches Chromium through Playwright and builds a semantic tree in the page: explicit roles, the upstream implicit role table (landmarks, lists, tables with rows and cells, dialogs, options, `img alt=""` as presentation, `select multiple` as listbox, and the rest), accessible name, text, test id, heading level, and the disabled, checked, expanded, selected, pressed, focused, and hidden states. As upstream, the name reads `aria-labelledby` before `aria-label` and associated labels. Disabled covers `:disabled` (including a disabled fieldset), `aria-disabled="true"` on the element, and `aria-disabled` inherited from an ancestor for the roles it applies to. Checked is the native state for checkbox and radio inputs and `aria-checked="true"` elsewhere; `mixed` reads as not checked. A node under a hidden ancestor is hidden, `role="img"` is reported as `image`, and an open `details` is expanded. A closed select lists up to 60 options under it. Each observed element keeps a stable ref for the life of the document, and actions run on the element that ref names; there is no fallback to a text or role lookup in the page. Firefox and WebKit launch options are not exposed yet; the package reference can drive them later. .NET-only: the first launch in a process runs Playwright's Chromium install, and `E2E_SKIP_BROWSER_INSTALL=1` turns it off. .NET-only: `dotnet test` has no flag like upstream's `--headed`, so when `WebEngineOptions.Headless` is unset, `E2E_HEADLESS=0` or `false` shows the window. As upstream, a run is headless by default. As upstream, a headless run installs with `--only-shell` and a headed run installs the full build, and the install runs with `PLAYWRIGHT_SKIP_BROWSER_GC=1` unless the user set it. Upstream checks the cache before it spawns the install; the port leaves that to Playwright's install, which does nothing when the build is there. Playwright's .NET `Program.Main` starts the driver with the process environment, so the port sets `PLAYWRIGHT_SKIP_BROWSER_GC` on the process, not on the install alone.

The walk goes through open shadow roots and closed ones that page script attached (an init script records them, as upstream does). Each iframe is a boundary node, and the engine reads that frame's document through Playwright and puts it under the node, so same-origin and cross-origin frames are both listed and acted on. One observation lists at most 3000 nodes across all frames. When a document has more, nodes that intersect the viewport come first and the rest of the budget goes to the others in document order; the observation reports `Truncated`, and the snapshot tells the model to scroll. `OpenAsync` waits for the `load` event.

`WebEngineOptions` carries the upstream `web()` options: `Viewport` (default 1280 by 720, `null` for no emulation), `TestIdAttribute`, `Headers`, `BasicAuth`, `UserAgent`, `Locale`, `TimezoneId`, `InitScripts`, and `Connect` (a CDP endpoint resolver, with an optional `ReconnectEndpoint`). Differences:

- `Headers` ride requests to the base URL's host. Upstream scopes them to the app's site, which can include sibling subdomains
- There is no provider (`browser: BrowserProvider`) or `screencast` option, so a persistent remote context comes only from `Connect.ReconnectEndpoint`
- With `ReconnectEndpoint`, as upstream, each attempt rides the default context of a fresh browser from `CdpEndpoint`, refuses a browser an earlier attempt of the same `WebEngine` rode, and reconnects once to the same browser and page after a transport drop. `Headers`, `BasicAuth`, `UserAgent`, `Locale`, and `TimezoneId` are `INVALID_CONFIG`, and `app.clearState()` is `UNSUPPORTED_CAPABILITY`. After a reconnect, `PerformAsync`, the agent's key press, and the viewport swipe fail with `NODE_STALE` until the next observation; the browser fixture's keyboard and mouse are direct input and keep working. As upstream, the reconnect is spent from the budget of the operation that found the transport dropped, both for the CDP dial and for the operation itself
- `BasicAuth` takes a `Secret` password, but the value is not added to the session's redaction list
- An `InitScripts` entry is a `WebInitScript`: source (a string converts to one), `WebInitScript.FromPath`, or `WebInitScript.FromFunction`. A C# lambda cannot run in the page, so a function is its JavaScript source. A relative path resolves against the project root (`EngineStartOptions.ProjectRoot`, which `E2ESession` passes from `E2ESessionOptions.ProjectRoot`). Each attempt reads the files when its session starts; upstream reads them once per process

The tree is a subset of upstream's: names follow the port's simpler accname rules, and nodes are listed by role, test id, or direct text (not by name alone, or as an empty painted `box`). So `getByText` answers with the innermost element that reads the text, such as a `<strong>` inside a paragraph, as Playwright does. Text inside a `<label>` of a control, or in an element an `aria-labelledby` names, is not listed: the labelled element carries it as its name, so `getByText` answers with that element (Playwright answers with the label and retargets a fill to the control). Upstream's `getByText` runs Playwright's text engine; the port matches the tree, so text split across child elements with no text of its own between them (`<div><b>a</b><i>b</i></div>`) is not one match.

Password fields and `autocomplete=current-password` fields are marked secure. Their values, and their `value` attribute, are omitted from the snapshot. A text, value, or attribute expectation on a secure field fails with `POLICY_DENIED`, as upstream.

Selected is `aria-selected="true"` or a selected `<option>`, expanded is `aria-expanded="true"` or an open `details`, and focused is the active element, followed into shadow roots. Attributes (for `getAttribute` and `toHaveAttribute`) and the client rect (for `boundingBox`) are read for every kept node; they are not sent to the model. A rect in an iframe is relative to that frame's viewport.

Text, label, placeholder, test id, and display value queries keep hidden matches, so `toBeVisible` fails on a hidden match and `toBeHidden` passes on no match or one hidden match. Role queries skip hidden nodes, and `toBeAttached` and `waitFor` `attached` or `detached` also match hidden nodes for role queries. A single-node matcher that still sees more than one match at its deadline fails with `STRICT_MODE`.

Playwright failures never escape as `PlaywrightException`. `WebEngine` throws `EngineException` with an upstream engine code from `EngineErrorCodes`: `NOT_ACTIONABLE` when an action timed out before its input was dispatched or the target does not take that input, `ACTION_MAY_HAVE_COMMITTED` when it timed out after dispatch, `NODE_STALE` when the element or its document is gone, `OPERATION_TIMEOUT` for a navigation or key press timeout, and `ENGINE_FAILURE` otherwise. `EngineException.Retryable` is true only for `NODE_STALE` and `FRAME_NOT_FOUND`; any other retryable claim becomes `ENGINE_FAILURE`, as upstream. A sensitive fill's value is redacted from the message. As upstream, `check` and `uncheck` click once and read the state back on the same element: a control gone by then (replaced, or navigated away) took the click, so the action passes, and a click that leaves the state unchanged, or an `uncheck` on a checked radio, is `NOT_ACTIONABLE`. The document engine still uses `NOT_FOUND` for a ref missing from its last observation.

A page stuck in a script fails an operation at the action timeout, not the test timeout. As upstream, each page operation (a navigation, back, forward, reload, observe, an action, a scroll, `browser.evaluate`, `browser.title`, `browser.setViewport`, a key press, typing, and `browser.mouse`) has one deadline, the action timeout, that every page call in it shares. Playwright's own timeout is set 250 ms short of what is left, or half of it when less than 500 ms is left, so Playwright's reason (what blocked an action, or that its input was dispatched) arrives first. A call Playwright never answers, such as an evaluate or input on a page stuck in a script, is abandoned at the deadline. A read cut off that way fails with `OPERATION_TIMEOUT`. An action or input cut off that way is `ACTION_MAY_HAVE_COMMITTED`, because it may have reached the page.

A scroll moves three quarters of the viewport or the scrolled element with `scrollBy`. Upstream sends a wheel gesture. `back` is the browser history.

## App and browser fixtures

`App.RestartAsync` closes the tab and opens a blank one in the same browser context, so cookies and storage survive. `App.ClearStateAsync` replaces the context with a clean one and opens a blank page. Both keep a viewport set with `Browser.SetViewportAsync`. Call `App.OpenAsync` afterwards.

Differences from upstream:

- `app.open`, `back`, `forward`, and `reload` wait for `load` within the action timeout. Upstream waits within the test timeout.
- `Browser.WaitForURLAsync` takes a string, resolved against the base URL and compared exactly, or a `Regex`. It polls until the assertion timeout or the given timeout.
- `Browser.WaitForResponseAsync` takes upstream's glob string or a .NET `Regex` (upstream an ECMAScript `RegExp`), and the timeout as a `TimeSpan?`. As upstream, the timeout bounds the match only; `WebResponse.TextAsync` and `JsonAsync<T>` wait for the body up to the action timeout, which is fixed for the session (upstream reads the budget when the body is read). A failed match is `OPERATION_TIMEOUT` with the port's message form (`waitForResponse: <Playwright message>`).
- `Browser.EvaluateAsync<T>` takes the script as a string (an expression, or a function source that is called with the optional argument) and deserializes the JSON result to `T`. A throwing script fails with `EVALUATE_FAILED`.
- `Browser.RouteAsync` takes the same glob string or `Regex` as `WaitForResponseAsync`, and a handler that returns a `Task`. `WebRoute` has `Request`, `FulfillAsync` (`RouteFulfillResponse`: `Status`, `Headers`, `ContentType`, `Json`, `Body`, `Path`), `ContinueAsync` (`RouteContinueOverrides`: `Url`, `Method`, `Headers`, `PostData`), `FallbackAsync`, and `AbortAsync`, with upstream's checks, codes, and messages. `UnrouteAsync` removes the routes added with an equal glob, or a `Regex` with the same source and options. `Path` resolves against the project root (`E2ESessionOptions.ProjectRoot`, which `E2ETest` sets to the config's directory, else the working directory). Differences: options are typed, so an unknown key, a non-string header value, and an `abort` error code do not compile instead of failing with `INVALID_ARGUMENT`; `Json` is serialized with `System.Text.Json` web defaults, and `null` means no JSON body; a handler error raised after the test's last browser call is dropped (upstream waits for running handlers and fails the attempt with it).
- `Browser.AddInitScriptAsync` takes source or a `WebInitScript`, and a JSON argument only with `WebInitScript.FromFunction`, the function's JavaScript source.
- Fixture calls are not recorded as harness steps.
- Navigation URLs resolve with `System.Uri`, not WHATWG. As upstream, only `http:`, `https:`, and the exact `about:blank` are admitted. But a tab or newline inside a URL is escaped, not stripped, so `view-\tsource:file:` opens as an http path under the base URL (or fails with `APP_URL_REQUIRED` without one) instead of `POLICY_DENIED`. A percent-encoded unreserved character in the path is decoded (`%66ile:` becomes `file:`). Neither opens a scheme other than http(s).

`DocumentEngine` supports `app.back` (it rebuilds the previous route), `app.restart`, and `app.clearState` (both leave a blank page and no history). It has no viewport: `DocumentPage.OnScroll` and `DocumentElement.OnScroll` let a page load more rows when it is scrolled. It has no browser, so every `Browser` member fails with `UNSUPPORTED_CAPABILITY`. A custom engine opts in by implementing `IBrowserSession`, and by overriding the default `BackAsync`, `RestartAsync`, and `ClearStateAsync` on `IEngineSession`, which otherwise fail with `UNSUPPORTED_CAPABILITY`.

## Models

Upstream takes any AI SDK model instance. JSON cannot hold one, so `agents.<name>.provider` names the client, and code builds one with `ModelProviders`, `Subscriptions`, or a client's options. Each client speaks its vendor's wire protocol directly:

| Upstream | `provider` | .NET |
| --- | --- | --- |
| `@ai-sdk/openai` chat, `@ai-sdk/openai-compatible`, Ollama | `openai`, `openai-compatible` | `OpenAiCompatibleModel` |
| `@ai-sdk/openai` Responses, `@ai-sdk/azure` | `openai-responses`, `azure` | `OpenAiResponsesModel` (`ModelProviders.AzureOpenAi`) |
| `@ai-sdk/anthropic` | `anthropic` | `AnthropicModel` |
| `@ai-sdk/google` | `google` | `GoogleModel` |
| `@ai-sdk/amazon-bedrock` | `bedrock` | `BedrockModel`, signed with SigV4 or a Bedrock API key |
| `@ai-sdk/xai` | `xai` | `OpenAiCompatibleModel` at `api.x.ai` |
| `@openrouter/ai-sdk-provider` | `openrouter` | `OpenAiCompatibleModel` at `openrouter.ai` |
| `gateway()` from `ai` | `gateway` | `OpenAiCompatibleModel` at `ai-gateway.vercel.sh` |
| `chatgpt()` | `chatgpt` | `Subscriptions.ChatGpt` |
| `copilot()` | `copilot` | `Subscriptions.Copilot` (`CopilotModel`) |
| `grok()` | `grok` | `Subscriptions.Grok` |
| `opencodeConsole()` | `opencode-console` | `Subscriptions.OpenCodeConsole` (`OpenCodeConsoleModel`) |

Keys come from the environment variable the AI SDK reads (`OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `GOOGLE_GENERATIVE_AI_API_KEY`, `AZURE_API_KEY`, `XAI_API_KEY`, `OPENROUTER_API_KEY`, `AI_GATEWAY_API_KEY`, `AWS_*`), or the one `apiKeyEnv` names.

Subscriptions follow upstream: the same four logins (`openai` PKCE on port 1455 or `--device`, `github-copilot` from `gh` or an OAuth App's device flow with enterprise hosts, `opencode-console` and `spacexai` device flows), the same credentials file (`$XDG_CONFIG_HOME/e2e/oauth.json`, mode 0600, locked and replaced atomically, unknown entries kept), `E2E_OAUTH_CREDENTIALS`, refresh two minutes ahead of expiry with one refresh shared per file, a retry after a 401, Copilot's chat-or-Responses choice from the plan's listing, OpenCode Console's per-model protocol from the workspace config, and `OPENCODE_API_KEY`. A missing or rejected login fails the step with `MODEL_PROVIDER_FAILED`, blocked, naming the `OAuthException` code (`NOT_LOGGED_IN`, `LOGIN_REQUIRED`).

Differences:

- Requests are not streamed. The Codex backend only streams, so its event stream is folded back into the final response, as upstream folds it
- The Vercel AI Gateway and OpenRouter are reached over their OpenAI-compatible chat API, so a gateway model gets no Anthropic cache breakpoints or OpenAI cache key. The gateway reads `VERCEL_OIDC_TOKEN` from the environment but does not run `vercel env pull` itself
- Gemini tool schemas go as `parametersJsonSchema`, unconverted. Gemini 3 thought signatures are kept per model instance and sent back with their function calls
- `azure` uses the Responses API at `/openai/v1` with `api-version=v1`, as the AI SDK's `azure()` does. Bedrock reads static credentials from the environment only; there is no AWS profile, SSO, or instance-role chain
- Every model request sends upstream's identity headers, replacing the same headers set on the client, but names this port so its traffic is not counted as upstream's: `User-Agent: e2e-dotnet/<version> (<platform>; <arch>)` (the NuGet version; the platform and arch by Node's names), `HTTP-Referer: https://github.com/hardkoded/e2e-dotnet`, and `X-Title: e2e-dotnet`. Model requests append `runtime/dotnet/<version>` after it, where upstream has the AI SDK's user agent. Login requests send the same user agent, as upstream's do, and still name the `e2e` originator and referrer that upstream registered
- `e2e login` with no provider shows a numbered list instead of upstream's picker. `e2e models` prints the same columns
- Not ported: images in model requests (there is no vision), and `e2e init` writing the model config
