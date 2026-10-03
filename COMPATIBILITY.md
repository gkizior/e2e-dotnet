# Compatibility

Upstream parity target: [tester-army/e2e](https://github.com/tester-army/e2e) commit `f7c075666672d128f79ef2ec347fda67ea9e42ce` (`fix(expect): waitFor attached/detached, absent-frame absence, toHaveProperty primitives, toHaveURL ignoreCase`), recorded from the default branch on 2026-10-02.

Names are C# versions of the JavaScript API: `agent.act` is `ActAsync`, `screen.getByRole` is `GetByRole`, `expect(locator).toContainText` is `Expect.That(locator).ToContainTextAsync`, and `unique()` is `Values.Unique`.

## Package map

| JavaScript | .NET |
| --- | --- |
| `e2e` and `@e2e-dev/web` | `E2E` (`WebEngine` is Playwright, in the same package) |
| NUnit `[Test]` | `E2E.NUnit.E2ETest`. The fixture commits the replay cache from the NUnit result |
| `e2e.config.ts` | `e2e.config.json`, the same keys as JSON. `E2ETest` finds and applies it; fixture properties override it |
| Vercel AI SDK model | `OpenAiCompatibleModel` (chat completions and tool calls) |
| — | `DocumentEngine`, an in-memory page for hosts that do not want a browser |

## Ported

- NUnit `[Test]`, `[SetUp]`, `[TearDown]`, `[OneTimeSetUp]`, and `[OneTimeTearDown]`
- `Assert.Ignore`, `[Retry]`, `[Timeout]`, and `[Category]`
- Screen queries: role, text, label, test id, placeholder, display value, `filter` (`hasText` and `has`), `first`, `last`, `nth`. A locator offers every query kind, scoped to its matches' descendants. Every query takes a `TextMatch`: a string or a .NET `Regex`, tested against the whitespace-normalized text. `RoleOptions` carries `name`, `checked`, `disabled`, `selected`, `expanded`, `pressed`, and `level`, and `img` is read as `image`. A role query never matches a hidden node; the other kinds keep hidden nodes unless `Visible` is set, as upstream. `getByLabel` matches any node whose accessible name matches, as the upstream reference resolver does. Options are C# objects (`RoleOptions`, `TextMatchOptions`) instead of object literals, and `filter({ hasText, has })` is the two overloads `Filter(TextMatch)` and `Filter(Locator)`. Regular expressions use .NET syntax, not ECMAScript
- Actions: tap, click (an alias of tap), doubleTap, fill (a string or a `Secret`), pressSequentially, press, check, uncheck, clear, selectOption, focus, scrollIntoView. They wait up to the action timeout for exactly one enabled, visible match. `selectOption` takes a string that matches an option value or label. `ActionOptions.Timeout` overrides the wait per action (`PressSequentiallyOptions` adds `Delay`). The web engine's own Playwright wait still uses the configured action timeout
- Locator reads: `textContent`, `inputValue`, `getAttribute`, `isVisible`, `isHidden`, `isEnabled`, `isDisabled`, `isChecked`, `boundingBox`, `count`, `all`, `allTextContents`. They read the current screen once and do not verify an earlier `act`. Single-node reads fail with `NOT_FOUND` when nothing matches instead of waiting
- `locator.waitFor` with `attached`, `detached`, `visible` (default), and `hidden`, as `WaitForAsync(new LocatorWaitForOptions { State, Timeout })`. The timeout defaults to the action timeout and a timeout fails with `TIMEOUT`. `LocatorWaitForOptions` is named apart from `agent.waitFor`'s `WaitForOptions`
- Locator expectations: visible, hidden, attached, enabled, disabled, checked, selected, expanded, focused, text, contained text, value, attribute, accessible name, and count. They poll until the assertion timeout, or the matcher's own `timeout`. `Not` inverts a matcher, which then passes after 1000 ms of continuous truth (or the whole budget when it is shorter). Text matchers take the same `TextMatch` as queries, `ignoreCase`, and a list form. The boolean flags are `visible`, `attached`, `enabled`, and `isChecked` (`checked` is a C# keyword)
- `expect.soft` for every locator expectation, as `Expect.Soft(locator)`, including `.Not` and the per-matcher `timeout`. An `ASSERTION_FAILED` is kept and the body runs on; any other error still throws. `E2ETest` records each one on the NUnit result, as inside `Assert.EnterMultipleScope`, so the test fails when the body ends and lists every failure. Other hosts set `E2ESessionOptions.OnSoftFailure` or call `E2ESession.CloseSoftFailures`
- `expect.poll` as `Expect.Poll(read, options)`, with `Timeout`, `Interval`, and `Message`. A read that throws is retried. The matchers are `ToBeAsync`, `ToSatisfyAsync`, and `.Not`; `E2E.NUnit` adds `ToMatchAsync` for any NUnit constraint, such as `Is.GreaterThan(3)`. The default timeout is 5 seconds, not the configured assertion timeout, because the poll does not see the running test, and it does not stop at the test deadline unless a cancellation token is passed
- `agent.act`, `agent.assert`, `agent.waitFor`, `agent.extract`. `ActOptions` has `Params`, `Timeout`, `MaxSteps`, and `MaxModelCalls`; `AssertOptions` has `Timeout`; `WaitForOptions` has `Timeout`, `Interval`, and `MaxModelCalls`. `ExtractAsync<T>` takes no options (see [Agent options not yet ported](#agent-options-not-yet-ported))
- Act tools `observe`, `scroll`, `scroll_to`, and `back`. They are offered when the engine declares `EngineCapabilities.Scroll` or `EngineCapabilities.History`. `scroll_to` with a target scrolls it into view. With text, it pages the viewport, or the target list, until a node reading the text is listed, then scrolls it into view. It stops when the screen stops moving
- Replay cache for a verified `act`: role, name, test id, and path. Modes are `self-finalized`, `agent-concluded`, and `missed`
- `Values.Unique` and `Secret`. `Values.Unique` rejects an empty or whitespace value, and a value that contains the cache slot marker (U+0001, the port's form of `{{param:`). A secret value has at least 6 code points: `Secret.Create` throws `INVALID_ARGUMENT`, a config secret `INVALID_CONFIG`, and `Credentials.User` `INVALID_CONFIG` for a short `E2E_USER_*_PASSWORD`. Secret values are redacted from prompts, including the `assert`, `waitFor`, and `extract` statement. Redaction matches any case, JSON escapes, HTML character references, percent encoding, and collapsed inner whitespace. A known marker is never rewritten. A failed secret fill in `WebEngine` reports the Playwright message with the value and its fragments of 8 or more characters redacted. Not ported: decoding base64 runs, and values cut short at a length limit
- `app.open`, `app.back`, `app.restart`, `app.clearState`, and `app.baseUrl` (`App.BaseUrl`), and the context `platform`
- The `browser` fixture subset: `reload`, `back`, `forward`, `url`, `title`, `waitForURL`, `evaluate`, `cookies`, `setCookies`, `setViewport`, `keyboard.press`, `keyboard.type`, and `mouse.move`, `wheel`, `down`, `up`
- Agent budgets: `ActOptions.MaxSteps`, `ActOptions.MaxModelCalls`, and `WaitForOptions.MaxModelCalls`. A per-call budget can only lower the configured one (`E2ESessionOptions.MaxSteps` and `MaxModelCalls`, 25 each by default). A higher or non-positive value throws `INVALID_ARGUMENT`
- An `act` past its action budget ends `STEP_BUDGET_EXHAUSTED`, blocked. The model is told and may still conclude: a passing verdict, or a failure without a code, becomes the budget error. Replayed actions draw on the same budget. `navigate`, `back`, `scroll`, and `scroll_to` take a slot; `observe` does not
- `ActResult.ModelCalls` (0 for a full replay) and `ActResult.Actions` (replayed and live actions, counting failed attempts)
- `AgentException.Blocked` and `AgentException.Explanation` (the same text as `Message`)
- OpenAI-compatible tool calling

Replay runs `back` and a viewport scroll as recorded, re-finds a scrolled list before each repeat, and pages again for a `scroll_to` text. Consecutive identical scrolls are recorded as one action with a repeat count. `observe` is not recorded. Upstream scrolls the viewport when a list that filled the screen cannot be re-found. Replay does not use node geometry, so a lost list stops the replay.

A passing locator expectation or `agent.assert` after `act` writes the recording. `agent.assert`, `waitFor`, and `extract` always run live. As upstream, verification stops when the test fails, so a check in a derived `[TearDown]` after a failure records nothing. When a replay ends in `end-mismatch` and the agent repairs it with more actions, the entry is evicted instead of rewritten; the next clean run records the flow again.

## Replay cache

- The key holds the cache schema, the replay policy version, the test, the engine and its major.minor version, the instruction, the params, and the repeat index.
- Only the first attempt replays. A `[Retry]` attempt runs live and still records, and its acts report `missed` with reason `retry`. Each `[Repeat]` iteration is a first attempt.
- A recording that opens with `navigate` replays from any route. Any other recording needs the start route.
- At the end of an attempt that passed, failed, or was skipped, verified acts are written and the unverified acts that recorded or replayed are evicted. An act that missed the cache and then failed leaves its key alone. A model outage or a cancelled test writes and evicts nothing.
- An entry that a verified replay finished, or that already holds the same flow, is not rewritten.
- A replay waits up to `ReplayTimeout` (15 s; a .NET-only fixture property, not a config key) for each recorded target and for the recorded end state. Upstream waits 15 s for the end route and sizes the anchor wait from the recording.
- `cache.mode` `read-only` replays but never writes or evicts, including the `end-mismatch` eviction. `off` neither replays nor records. Under `cache.strict`, a stale recording fails the act with `REPLAY_STALE` and stays in place.

## Defaults

These match upstream.

| Setting | Default |
| --- | --- |
| Test timeout | 120 s |
| Action timeout | 30 s |
| Assertion timeout | 5 s |
| `maxModelCalls` | 25 |
| `maxSteps` (actions per `act`) | 25 |
| `waitFor` interval | 3 s. The judge runs only when the screen changed |
| Replay wait | 15 s |

`E2EDefaults` holds these values; `E2EConfig`, `E2ESessionOptions`, and `E2ETest` start from it.

## Error codes that differ

| | Upstream | .NET |
| --- | --- | --- |
| A locator action, read, or expectation matched more than one node | `LOCATOR_AMBIGUOUS` | `STRICT_MODE` |
| A locator action or read matched no node | `LOCATOR_NOT_FOUND` | `NOT_FOUND` |

## Not ported

- The `e2e` command-line tool, the custom runner, `[E2ETest]` discovery, `--grep`, `test.only`, and the JSON report
- `@e2e-dev/mobile`, `@e2e-dev/github`, `@e2e-dev/kernel`, `@e2e-dev/eas`
- MCP server, `e2e init`, `e2e login`, OAuth subscriptions (ChatGPT, Copilot, Grok)
- Vision, screenshots, traces, and video
- Parallel workers, serial suites, project tools, and custom executors
- The full locator action set (`dragTo`, `swipe`, `hover`, `setInputFiles`, `secondaryTap`, `longPress`, pointer points). `secondaryTap` and `longPress` have no counterpart in the document engine
- Per-action options other than `timeout` (and `delay` for `pressSequentially`): click `modifiers`, tap `position`, and the `{ label, value, index }` form of `selectOption`
- Route patterns that ignore a record id. This port compares the URL path exactly and ignores the query and fragment
- Diff-only observations. Each model turn receives a full text snapshot
- Telemetry
- Browser fixture members `goto`, `locator`, `frameLocator`, `route`, `unroute`, `waitForResponse`, `onDialog`, `waitForDownload`, and the `expect(browser)` matchers (`toHaveURL`, `toHaveTitle`, `toHaveClass`)
- Value expectations (`expect(value).toBe`, `toEqual`, `toMatchObject`, `toHaveProperty`, `toMatchSchema`, and the rest) and their `expect.soft` form. Use NUnit `Assert.That` with constraints, and `Assert.EnterMultipleScope` (or `Assert.Multiple`) for soft value checks. `expect.poll` takes NUnit constraints through `ToMatchAsync` in their place
- Asymmetric matchers (`expect.any`, `anything`, `objectContaining`, `arrayContaining`, `stringContaining`, `stringMatching`). Use NUnit constraints such as `Is.InstanceOf`, `Is.Not.Null`, `Has.Property`, `Is.SupersetOf`, `Does.Contain`, and `Does.Match`
- The upstream reporter, GitHub pull request comment, and trace viewer

### Agent options not yet ported

Tracked in [#13](https://github.com/hardkoded/e2e-dotnet/issues/13).

- `ExtractOptions`: a Standard Schema `schema`, a per-call `timeout`, and validation with one repair round before `MODEL_OUTPUT_INVALID`. `ExtractAsync<T>` deserializes into `T`
- `judge` (a separate judgment model), `system`, and `context`
- Named agents (`agents.<name>` and the per-call `agent` option)
- `providerOptions`, `judgmentTimeout`, `maxObservationBytes`, and `maxInputTokens`
- `vision` and `screenshot` on judgments, and `AgentError.screenshot`
- Act param limits: 64 KiB and 32 levels deep. A cycle is already rejected with `INVALID_ARGUMENT` when the cache key serializes the params
- Range checks on the configured budgets (1 through 100)
- A model-call budget exhausted by `act` ends `STEP_NO_CONCLUSION`, not `STEP_BUDGET_EXHAUSTED`

## Config

`E2ETest` loads the nearest `e2e.config.json` above the test assembly directory, then above the working directory, once per run. Supported keys: `targets` (one entry with `name`, `platform`, and `app.url`), `timeout`, `launchTimeout`, `actionTimeout`, `assertionTimeout`, `cleanupTimeout`, `retries`, `agents`, `cache` (`mode`, `dir`, `strict`), and `secrets`. Defaults, the CI cache demotion to `read-only`, `E2E_SECRET_<NAME>` overrides, the 6 code point secret minimum, and `INVALID_CONFIG` for unknown keys follow upstream. Keys of the old .NET shape (`app`, `engine`, `agent`, `timeouts`) fail with a hint to the upstream key.

Differences:

- JSON has no engine handles or model instances. The fixture's `CreateEngine` chooses the engine, `platform` must be `web`, and `targets` holds one entry. `agents.<name>.model` is an OpenAI-compatible model id, with the .NET-only `baseUrl` and `apiKeyEnv` beside it. Only `agents.default` is used
- A secret set to `null` reads only `E2E_SECRET_<NAME>`. Provider functions do not exist in JSON. A test reads secrets with `Secrets.Get(name)`
- `retries` is validated and resolved (1 in CI, 0 elsewhere) but NUnit retries still come from `[Retry]`
- `cache.strict` treats any replay that finds a recording but does not finish it as stale, and leaves that recording in place
- A launch or cleanup timeout fails with `ENVIRONMENT_UNAVAILABLE`
- Not supported, and rejected with `INVALID_CONFIG`: `projectId`, `tests`, `failOnSkippedFailure`, `workers`, `artifacts`, `output`, `trace`, `video`, `reporters`, `credentials`, `cache.store`, the non-URL `app` keys, and agent keys other than `model` and `maxModelCalls` (`maxSteps` is the fixture property `E2ETest.MaxSteps`). Credentials still come from `E2E_USER_<NAME>_USERNAME` and `_PASSWORD`

## Web engine

`WebEngine` launches Chromium through Playwright and builds a semantic tree in the page: explicit roles, the upstream implicit role table (landmarks, lists, tables with rows and cells, dialogs, options, `img alt=""` as presentation, `select multiple` as listbox, and the rest), accessible name, text, test id, heading level, and the disabled, checked, expanded, selected, pressed, focused, and hidden states. As upstream, the name reads `aria-labelledby` before `aria-label` and associated labels. Disabled covers `:disabled` (including a disabled fieldset), `aria-disabled="true"` on the element, and `aria-disabled` inherited from an ancestor for the roles it applies to. Checked is the native state for checkbox and radio inputs and `aria-checked="true"` elsewhere; `mixed` reads as not checked. A node under a hidden ancestor is hidden, `role="img"` is reported as `image`, and an open `details` is expanded. A closed select lists up to 60 options under it. Each observed element keeps a stable ref for the life of the document, and actions run on the element that ref names. Firefox and WebKit launch options are not exposed yet; the package reference can drive them later.

The walk goes through open shadow roots and closed ones that page script attached (an init script records them, as upstream does). Each iframe is a boundary node, and the engine reads that frame's document through Playwright and puts it under the node, so same-origin and cross-origin frames are both listed and acted on. One observation lists at most 3000 nodes across all frames. When a document has more, nodes that intersect the viewport come first and the rest of the budget goes to the others in document order; the observation reports `Truncated`, and the snapshot tells the model to scroll. `OpenAsync` waits for the `load` event.

`WebEngineOptions` carries the upstream `web()` options: `Viewport` (default 1280 by 720, `null` for no emulation), `TestIdAttribute`, `Headers`, `BasicAuth`, `UserAgent`, and `Connect` (a CDP endpoint resolver). Differences:

- `Headers` ride requests to the base URL's host. Upstream scopes them to the app's site, which can include sibling subdomains
- `Connect` has no `reconnectEndpoint`, so there is no persistent remote context, and no provider (`browser: BrowserProvider`) or `screencast` option
- `BasicAuth` takes a `Secret` password, but the value is not added to the session's redaction list

The tree is a subset of upstream's: names follow the port's simpler accname rules, nodes are listed by role or test id only (not by name, direct text, or as an empty painted `box`).

Password fields and `autocomplete=current-password` fields are marked secure. Their values, and their `value` attribute, are omitted from the snapshot. A text, value, or attribute expectation on a secure field fails with `POLICY_DENIED`, as upstream.

Selected is `aria-selected="true"` or a selected `<option>`, expanded is `aria-expanded="true"` or an open `details`, and focused is the active element, followed into shadow roots. Attributes (for `getAttribute` and `toHaveAttribute`) and the client rect (for `boundingBox`) are read for every kept node; they are not sent to the model. A rect in an iframe is relative to that frame's viewport.

Text, label, placeholder, test id, and display value queries keep hidden matches, so `toBeVisible` fails on a hidden match and `toBeHidden` passes on no match or one hidden match. Role queries skip hidden nodes, and `toBeAttached` and `waitFor` `attached` or `detached` also match hidden nodes for role queries. A single-node matcher that still sees more than one match at its deadline fails with `STRICT_MODE`.

Playwright failures never escape as `PlaywrightException`. `WebEngine` throws `EngineException` with an upstream engine code from `EngineErrorCodes`: `NOT_ACTIONABLE` when an action timed out before its input was dispatched or the target does not take that input, `ACTION_MAY_HAVE_COMMITTED` when it timed out after dispatch, `NODE_STALE` when the element or its document is gone, `OPERATION_TIMEOUT` for a navigation or key press timeout, and `ENGINE_FAILURE` otherwise. `EngineException.Retryable` is true only for `NODE_STALE` and `FRAME_NOT_FOUND`; any other retryable claim becomes `ENGINE_FAILURE`, as upstream. A sensitive fill's value is redacted from the message. The document engine still uses `NOT_FOUND` for a ref missing from its last observation.

A scroll moves three quarters of the viewport or the scrolled element with `scrollBy`. Upstream sends a wheel gesture. `back` is the browser history.

## App and browser fixtures

`App.RestartAsync` closes the tab and opens a blank one in the same browser context, so cookies and storage survive. `App.ClearStateAsync` replaces the context with a clean one and opens a blank page. Both keep a viewport set with `Browser.SetViewportAsync`. Call `App.OpenAsync` afterwards.

Differences from upstream:

- `app.open`, `back`, `forward`, and `reload` wait for `load` within the action timeout. Upstream waits within the test timeout.
- `Browser.WaitForURLAsync` takes a string, resolved against the base URL and compared exactly, or a `Regex`. It polls until the assertion timeout or the given timeout.
- `Browser.EvaluateAsync<T>` takes the script as a string (an expression, or a function source that is called with the optional argument) and deserializes the JSON result to `T`. A throwing script fails with `EVALUATE_FAILED`.
- Fixture calls are not recorded as harness steps.

`DocumentEngine` supports `app.back` (it rebuilds the previous route), `app.restart`, and `app.clearState` (both leave a blank page and no history). It has no viewport: `DocumentPage.OnScroll` and `DocumentElement.OnScroll` let a page load more rows when it is scrolled. It has no browser, so every `Browser` member fails with `UNSUPPORTED_CAPABILITY`. A custom engine opts in by implementing `IBrowserSession`, and by overriding the default `BackAsync`, `RestartAsync`, and `ClearStateAsync` on `IEngineSession`, which otherwise fail with `UNSUPPORTED_CAPABILITY`.
