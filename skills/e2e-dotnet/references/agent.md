# Agent steps

`Agent` is a fixture member like `Screen` and the main way a test drives the
app. Each call is one bounded invocation: a fresh redacted observation, a
deadline, a model-call budget, and no shared transcript. A test with no agent
step makes no model calls and needs no model in the config.

## Configure a model

Set `provider` and `model` under `agents.default` (topic `setup` lists the
providers and their logins):

```json
{
  "targets": [{ "platform": "web", "app": { "url": "http://127.0.0.1:3000" } }],
  "agents": { "default": { "provider": "copilot", "model": "claude-sonnet-5.5" } }
}
```

- An agents entry takes `provider`, `model`, `judge`, `baseUrl`, `apiKeyEnv`,
  `system`, `context`, `maxSteps`, `maxModelCalls`, `judgmentTimeout`, and
  `providerOptions`. None inherits from `default`.
- `model` drives `ActAsync`. Judgments use `judge` when set, else `model`.
- No model and no replay that finishes the step is `MODEL_UNAVAILABLE`.
  Auth failures surface on the first model call as `MODEL_PROVIDER_FAILED`.
- `context` is what the app calls things, sent to every model call, judges
  included. `system` is how the acting agent works; judges never see it.
- In code, `ModelProviders` and `Subscriptions` build the same clients, and a
  fixture can override `CreateModel()` and `CreateJudge()`.

### Choose an agent

Tests use `agents.default`. A call picks another with its `Agent` option:
`new ActOptions { Agent = "buyer" }`. An unknown name is `INVALID_ARGUMENT`.
There is no `--agent` flag and no pin for a whole test; name the agent on
each call.

## ActAsync: one goal

```csharp
await Agent.ActAsync("add a todo named \"Buy milk\" and mark it done");

var member = Credentials.User("member");
await Agent.ActAsync("sign in with the given credentials", new ActOptions
{
    Params = new Dictionary<string, object?>
    {
        ["username"] = member.Username,
        ["password"] = member.Password, // a Secret: the model sees its name, the runner fills the field
    },
});
```

`ActAsync(instruction, options?)` runs a multi-action flow to a verdict. A
pass returns an `ActResult` with `Summary`, `ModelCalls`, `Actions`, and
`Cache` (the replay cache's part). A failed or blocked step throws an
`AgentException` whose `Code` says why: `ACTION_FAILED` (product failure);
`STEP_BUDGET_EXHAUSTED`, `STEP_TIMEOUT` (out of room);
`AUTH_CREDENTIAL_UNAVAILABLE`, `AUTH_CREDENTIAL_INVALID`,
`SECRET_UNAVAILABLE`, `ENVIRONMENT_UNAVAILABLE`, `SEED_DATA_MISSING`,
`TEST_SETUP_FAILED`, `AUTOMATION_UNSUPPORTED`, `POLICY_DENIED` (blocked from
outside, `Blocked` is true); `MODEL_OUTPUT_INVALID` (unusable answer). Full
list: topic `debugging`.

`ActOptions`:

- `Params`: values the instruction names. The runner fills a `Secret`, and a
  `Values.Unique(...)` value keeps the cache working across runs.
- `Timeout`: 30 s by default (`StepTimeout` on the fixture).
- `MaxSteps` (default 25 actions), `MaxModelCalls` (default 25): may only
  lower the agent's limits, else `INVALID_ARGUMENT`.
- `Agent`: the named agent that runs the step.

The tools the model can call: `observe`, `tap`, `double_tap`, `fill`,
`fill_secret` (a `Secret` from `Params`, by name), `press`, `select`,
`check`, `uncheck`, `clear`, `scroll`, `scroll_to`, `navigate`, `back`, and
`done`. `scroll` and `scroll_to` come with an engine that can scroll, and
`back` with one that has history. There is no `hover`, `drag`, `upload`,
`long_press`, `right_click`, `screenshot`, or point tool: a step that needs
one is `AUTOMATION_UNSUPPORTED`. Do that part with `Screen` actions instead.

## AssertAsync, WaitForAsync, ExtractAsync: one question

```csharp
await Agent.AssertAsync("the dashboard shows a trial badge"); // one look, one judgment

await Agent.WaitForAsync("the export finished and a download link appeared", new WaitForOptions
{
    Interval = TimeSpan.FromMilliseconds(500),
    Timeout = TimeSpan.FromMinutes(2),
});

var data = await Agent.ExtractAsync<TodoSummary>("every todo title and how many remain");
Assert.That(data.Titles, Does.Contain("Buy milk"));

public sealed record TodoSummary(IReadOnlyList<string> Titles, int Remaining);
```

- `AssertAsync` does not poll. False is `ASSERTION_FAILED` with the model's
  explanation. Too little on screen to decide is `ASSERTION_INCONCLUSIVE`,
  also a failure, so reach the right screen first and ask about what is
  visible.
- Judgments see the statement and the current screen only, never prior
  steps. Malformed output gets one repair round, then
  `MODEL_OUTPUT_INVALID`.
- A value shown in more than one place (a total in the summary and on the
  pay button) must agree everywhere, or the judgment fails. Name the one you
  mean (`"the order summary total is $42.00"`) when only it matters.
- `WaitForAsync` judges at once, then every `Interval` (default 3 s) only
  when the screen changed, and is `STEP_TIMEOUT` after `Timeout` (default the
  agent's `judgmentTimeout`, 30 s).
- `ExtractAsync<T>`: the type is the schema. The model sees the JSON schema
  of `T`, and the answer must deserialize into `T`. Data the screen does not
  show is `ASSERTION_INCONCLUSIVE`; to accept absence, make the member
  nullable and ask for it (`"the phone, or null when none is shown"`).
- There is no `vision`: judgments read the text snapshot, never pixels.

## Write instructions the model can execute

- One goal per `ActAsync`. The test sets the order of goals; the model finds
  the path.
- Use the words on screen: `"open the Billing tab"`, not `"upgrade"`.
- Values go in `Params`, never pasted into the instruction.
- Do not describe mechanics the runner handles: waiting, scrolling, retries.
- Pin every act's outcome right after it; that check lets the cache record:

```csharp
await Agent.ActAsync("create a workspace named \"Atlas\" on the Pro plan");
await Expect.That(Screen.GetByRole("status")).ToHaveTextAsync("Created \"Atlas\" on the Pro plan");
```

Off-screen state (a database row) can land after `ActAsync` returns; use
`Expect.Poll` on the read instead of a sleep.

## What the model sees

A redacted text snapshot of the screen (roles, names, text, states), and
your `context`. Never raw HTML, cookies, headers, environment values, or a
`Secret`'s value; password fields are masked. Each model turn gets the whole
screen. Nothing the model returns runs as code or selectors: the runner
validates every tool call first.

## Budgets and cost

| Call | Model calls | Default timeout |
| --- | ---: | --- |
| `ActAsync` | up to `maxModelCalls` (25) | 30 s |
| `AssertAsync` | 2 | 30 s |
| `ExtractAsync` | 2 | 30 s |
| `WaitForAsync` | up to `maxModelCalls` (25) | 30 s |

Slow model calls: raise `Timeout` on the call or the fixture's
`StepTimeout` for acts, `judgmentTimeout` for judgments, and `actionTimeout`
for slow UI. `STEP_TIMEOUT` and `STEP_BUDGET_EXHAUSTED` fail the test;
smaller goals help.

## The replay cache

A passing `ActAsync` saves its actions once a later check verifies the
outcome. The next run replays them with no model call, and the live agent
continues when the app no longer matches. `AssertAsync`, `WaitForAsync`, and
`ExtractAsync` always run live.

- On by default (`read-write`), `read-only` in CI, off with
  `"cache": { "mode": "off" }`. Entries live in `.e2e/cache/`; deleting the
  directory only slows the next run.
- An entry is written only after a later locator expectation,
  `AssertAsync`, or `WaitForAsync` passes, so an unchecked act never
  replays. Locator reads, `locator.WaitForAsync`, and `ExtractAsync` verify
  nothing.
- A replay re-finds each control by role, name, test id, and path, and
  needs the recorded start route unless the recording opens with a
  navigation. It passes alone only when the recorded end route is back,
  every control that appeared (with its text, value, and checked or
  selected state) is there, every one that went away is gone, at least one
  of those changed during the replay, and no new alert showed; otherwise
  the agent takes over mid-step. Text that reads differently on every run,
  a date, a time, an id, is not checked. Entries are keyed per agent and
  per agent `context`. `ActResult.Cache.Reason` says why a replay missed:
  `no-entry`, `invalid-entry`, `wrong-context`, `target-not-found`,
  `target-ambiguous`, `end-mismatch`.
- A step recording no actions, or changing nothing on screen or in the
  route, creates no entry. The screen it passed on is read once it holds
  still, so a late render still counts. One whose `Values.Unique` value
  equals another param's value is not recorded either
  (`ActResult.Cache.NotRecorded`: `param-collision`).
- Only the first attempt replays. A `[Retry]` attempt runs live and still
  records.
- Commit `.e2e/cache/` to share replays with CI and teammates.
- `cache.strict` fails a recording that no longer replays with
  `REPLAY_STALE`, also when the recording sits under an old key (topic
  `running`). Re-record with a run without it, then commit.

## Make the agent yours

1. **The goal.** A failed step usually named something the screen does not
   show; reword it with on-screen labels.
2. **`context`.** Vocabulary every step needs (plan names, what a
   "workspace" is), once on `agents.<name>.context` (at most 16 KiB).
3. **`system`.** How carefully the agent verifies and what it never does.
   A careful QA persona and a fast smoke agent are two `system` prompts on
   one model.
4. **The model and its options.** `providerOptions` for reasoning effort
   (wire names, such as `reasoning_effort`), or another model for one
   agent.

Not ported: project `tools`, a custom `executor`, decision models,
`maxObservationBytes`, `maxInputTokens`, `--debug`, and `--ai-trace`.

## In CI

Run the suite, agent steps included, on every pull request, with the
provider's API key from the CI secrets. A committed `.e2e/cache/` lets CI
replay verified acts with no model call (topic `running`).
