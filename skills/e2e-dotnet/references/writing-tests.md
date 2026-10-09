# Writing tests

## Pick your framework

A test class derives from an `E2ETest` base class. Its setup, attributes,
model-test marker, and `dotnet test --filter` syntax depend on the
framework; read the guide for yours first:

| Framework | Package | Topic |
| --- | --- | --- |
| NUnit | `E2E.NUnit` | `writing-tests-nunit` ([writing-tests-nunit.md](writing-tests-nunit.md)) |
| xUnit v3 | `E2E.XUnit.V3` | `writing-tests-xunit` ([writing-tests-xunit.md](writing-tests-xunit.md)) |

This topic covers what both share. A test body reads the same in either:

```csharp
await App.OpenAsync("/todos");

// An exact interaction: an empty submit adds nothing.
await Screen.GetByRole("button", "Add").ClickAsync();
await Expect.That(Screen.GetByRole("listitem")).ToHaveCountAsync(0);

await Agent.ActAsync("add a todo with the given title", new ActOptions
{
    Params = new Dictionary<string, object?> { ["title"] = "Write the release notes" },
});
await Expect.That(Screen.GetByRole("listitem")).ToHaveCountAsync(1);
```

The agent does the flow; `Expect` pins what must be true after each goal,
and that check lets the replay cache rerun the step later. `Screen` actions
are for exact interactions and values. Every test starts from a fresh
browser context with no page open, so it calls `App.OpenAsync` first.
There is no `describe`, `test.setup`, `serial` group, or tag of e2e's own:
grouping, setup, and categories are the framework's.

## Fixtures

Members of `E2ETest` in both frameworks:

- `App`: `OpenAsync(path)`, `BackAsync`, `RestartAsync`, `ClearStateAsync`,
  and `BaseUrl`. A path resolves against the target URL. `OpenAsync` and the
  agent's navigate step admit only `http:`, `https:`, and the exact
  `about:blank`; any other scheme is `POLICY_DENIED`. `RestartAsync` keeps
  cookies and storage, `ClearStateAsync` drops them, and both reopen the
  target URL. `ClearStateAsync` is `UNSUPPORTED_CAPABILITY` with
  `Connect.ReconnectEndpoint`.
- `Agent`: `ActAsync`, `AssertAsync`, `WaitForAsync`, `ExtractAsync<T>`
  (topic `agent`).
- `Screen`: the locators below.
- `Expect`: `Expect.That(locator)`, `Expect.Soft(locator)`, `Expect.Poll(read)`.
- `Browser`: URL, title, cookies, viewport, raw keyboard and mouse, and
  responses (below).
- `Secrets.Get(name)`: a `Secret` from the config. `Credentials.User(name)`:
  an account from `E2E_USER_<NAME>_USERNAME` and `_PASSWORD`.

## Locators

- `GetByRole(role, name)`, `GetByText`, `GetByLabel`, `GetByPlaceholder`,
  `GetByTestId`, and `GetByDisplayValue`. Prefer a role with the accessible
  name the app renders.
- Text matches are **exact by default**. Pass `exact: false` for a
  case-insensitive substring, or a .NET `Regex`.
- `GetByText` and `GetByLabel` return the innermost match, so a container
  echoing its child does not count twice: `GetByText("$42.00")` finds the
  `<strong>` in `<p>Order total: <strong>$42.00</strong></p>`. Text inside a
  control's `<label>` answers with the control.
- Roles follow HTML-AAM, and `img` aliases `image`. An inline `<svg>` is an
  `image`. A `<th>` without `scope` beside a data cell with text or children
  is a `rowheader`; one in a row of header cells, or with `scope="col"`, is a
  `columnheader`.
- A role's name is the accessible name, as Playwright computes it. Only
  content roles (button, link, cell, heading, tab, option, and the like) are
  named by their text, so `GetByRole("group", "More")` misses a `<details>`.
  A table is named by its `<caption>`, a fieldset by its `<legend>`, a figure
  by its `<figcaption>`, and an svg by its `<title>` child, which also names
  a link or button around it. A node with no name reports `Name` null.
- `RoleOptions` adds `Checked`, `Disabled`, `Selected`, `Expanded`,
  `Pressed`, and `Level`.
- Narrow with `Filter(text)` or `Filter(locator)`, and pick with `First()`,
  `Last()`, or `Nth(i)` when the order is the point. A locator also offers
  every query, scoped to its matches' descendants.
- A locator resolves when it is used. A match on two nodes fails with
  `STRICT_MODE`; no match fails with `NOT_FOUND`.
- `Visible = true` (on `TextMatchOptions` or `RoleOptions`) drops nodes the
  page hides (a closed drawer) before the exactly-one rule. Visibility is
  what renders, as `ToBeVisibleAsync` reads it: `display: none`, a
  `visibility` other than `visible`, and content under
  `content-visibility: hidden` or a closed `details` are hidden. A child that
  sets `visibility: visible` under a hidden parent is visible, and a
  `display: contents` element is visible when something under it is. An
  `aria-hidden` node that paints is visible, as upstream, but role queries skip
  it. Unlike upstream, a box with no size reads as visible.
- `GetByDisplayValue` never matches a checkbox or radio. An accessible name
  includes an embedded control's value (`<button>Flash the screen
  <input value="3"> times</button>` is "Flash the screen 3 times") and leaves
  out `aria-hidden` parts, such as a required-field `*`.

### Actions

`ClickAsync` (and `TapAsync`), `DoubleTapAsync` (and `DblClickAsync`), `FillAsync` (a string or a
`Secret`), `PressSequentiallyAsync`, `PressAsync`, `CheckAsync`,
`UncheckAsync`, `ClearAsync`, `SelectOptionAsync` (a value or label),
`FocusAsync`, and `ScrollIntoViewAsync`. Each waits up to the action timeout
for exactly one enabled, visible match. There is no hover, drag, file
upload, long press, or right click.

### Reads

`TextContentAsync`, `InputValueAsync`, `GetAttributeAsync`,
`IsVisibleAsync`, `IsHiddenAsync`, `IsEnabledAsync`, `IsDisabledAsync`,
`IsCheckedAsync`, `BoundingBoxAsync`, `CountAsync`, `AllAsync`, and
`AllTextContentsAsync`. A read looks at the screen once and does not wait
for a value to change; use a matcher when a value has to settle.
`WaitForAsync` waits for a state (`attached`, `detached`, `visible`,
`hidden`). `BoundingBoxAsync` is `null` for a node with no layout box
(`display: none` on it or an ancestor, or any `display: contents`
element). `InputValueAsync` on a checkbox or
radio reads its value attribute, `on` when it has none, whatever its checked
state; `IsCheckedAsync` reads the state.

Text reads (`ToHaveTextAsync`, `ToContainTextAsync`, `TextContentAsync`, and
`AllTextContentsAsync`) read the node's rendered text, whitespace
collapsed. On the web that is what `innerText` reads, not the label:
`<output aria-label="Remaining">1 remaining</output>` reads `1 remaining`,
and an icon-only button reads `""`. A hidden node reads its DOM text, so a
text query still finds a `visibility: hidden` copy. Check a label with
`ToHaveAccessibleNameAsync`.

## Expect

`Expect.That(locator)` polls until the assertion timeout:
`ToBeVisibleAsync`, `ToBeHiddenAsync`, `ToBeAttachedAsync`,
`ToBeEnabledAsync`, `ToBeDisabledAsync`, `ToBeCheckedAsync`,
`ToContainTextAsync`, `ToHaveTextAsync`, `ToHaveValueAsync`,
`ToHaveCountAsync`, `ToHaveAttributeAsync`, and more. `.Not` inverts a
matcher.

- `Expect.That(Screen).ToHaveScreenshotAsync("home.png")` compares the whole
  screen, and `Expect.That(locator).ToHaveScreenshotAsync()` one element's box,
  against `<test file>-snapshots/<name>-<target>-<os>.png` (web only). Commit the
  files. The first run writes the file and fails: look at it, commit it, rerun. In
  CI (`CI` set) nothing is written into the project; the file is attached under
  `.e2e/results/` at `snapshots/<path>`. `E2E_UPDATE_SNAPSHOTS=1` writes missing
  and different files and passes, after an intended UI change. Options
  (`ScreenshotOptions`): `Threshold`, `MaxDiffPixels`, `MaxDiffPixelRatio`, `Mask`
  (locators painted over, for clocks and other changing content), `MaskColor`,
  `Timeout`. A failure writes `-expected`, `-actual`, and `-diff` images under
  `.e2e/results/`. Files are per target and OS (`-web-linux.png`): screenshots
  written on a Mac never serve Linux CI, so write CI's from a CI job with the
  update variable.
- `Expect.Soft(locator)` records a failure and lets the test go on; the
  test fails at its end with every failure listed.
- `Expect.Poll(read)` retries any value: `ToBeAsync`, `ToSatisfyAsync`, and,
  in `E2E.NUnit`, `ToMatchAsync` with any NUnit constraint.
- For plain values, use the framework's asserts (`Assert.That`).

## Sign-in

There is no `test.setup` or saved `session`. Sign in inside each test, or in
the framework's per-test setup (NUnit `[SetUp]`, xUnit v3 `InitializeAsync`),
with `Screen` actions or `Agent.ActAsync`; both fill a `Secret`:

```csharp
var admin = Credentials.User("admin");
await App.OpenAsync("/login");
await Screen.GetByLabel("Email").FillAsync(admin.Username);
await Screen.GetByLabel("Password").FillAsync(admin.Password);
await Screen.GetByRole("button", "Sign in").ClickAsync();
```

A password field's value never reaches the model, and a text, value, or
attribute expectation on it fails with `POLICY_DENIED`: assert the outcome,
not the value.

## The browser fixture

`Browser` works with `WebEngine` only; the document engine answers
`UNSUPPORTED_CAPABILITY`.

- `ReloadAsync`, `BackAsync`, `ForwardAsync`, `UrlAsync`, `TitleAsync`,
  `WaitForURLAsync`, `EvaluateAsync<T>`, `SetViewportAsync`, and the raw
  keyboard and mouse.
- `WaitForResponseAsync(pattern, timeout?)` resolves once the headers
  arrive, with a `WebResponse` (`Url`, `Status`, `Headers`, `TextAsync()`,
  `JsonAsync<T>()`). `TextAsync` and `JsonAsync` wait for the body (up to
  the action timeout) and fail with `ACTION_FAILED` when it could not be
  read. Start it before the step that sends the request, and await it after.
- `RouteAsync(pattern, handler)`, `UnrouteAsync(pattern)`: intercept
  requests, newest route first. `route.Request` has `Url`, `Method`,
  `Headers`, `PostData`. The handler calls exactly one of
  `FulfillAsync(new RouteFulfillResponse { Status, Headers, ContentType, Json | Body | Path })`,
  `ContinueAsync(new RouteContinueOverrides { Url, Method, Headers, PostData })`
  (straight to the network), `FallbackAsync()` (the route registered before
  it), or `AbortAsync()`. None or two fails the next step with
  `ACTION_FAILED`, a bad option with `INVALID_ARGUMENT`. `Path` is relative
  to the project root.
- A `RouteAsync`, `UnrouteAsync`, or `WaitForResponseAsync` pattern is a
  glob string or a `Regex` matched against the full URL (`*` stays within
  one path segment, `**` crosses `/`, `?` is one character, `\` escapes the
  next one).
- `CookiesAsync()`, `SetCookiesAsync([...])`: a target is an http(s) URL,
  relative to the base URL, or a domain. `SetCookiesAsync` refuses a cookie
  URL that is not http(s), `about:blank` included, with `POLICY_DENIED`.
- `AddInitScriptAsync(source | WebInitScript)`,
  `AddInitScriptAsync(WebInitScript.FromFunction(fn), arg)`: runs before the
  page's own scripts; call it before `App.OpenAsync`. `arg` is JSON.

### Run a script before the page

An init script runs in every document before the page's own scripts, in
every tab and frame: mock a browser API, seed `Math.random`, or set a flag
the app reads at boot. A script is JavaScript source (a string), a file
(`WebInitScript.FromPath`, relative to the project root), or a function's
JavaScript source (`WebInitScript.FromFunction`). A function cannot close
over test variables; pass a JSON `arg` instead. For every test, set
`InitScripts` on the engine:

```csharp
protected override IEngine CreateEngine() => new WebEngine(new WebEngineOptions
{
    InitScripts = [WebInitScript.FromPath("tests/init.js")],
});
```

For one test, before the page opens:

```csharp
await Browser.AddInitScriptAsync(WebInitScript.FromFunction("(value) => { Math.random = () => value; }"), 0.5);
await App.OpenAsync("/raffle");
```

A script added after the page opened runs from the next navigation or
reload. A configured file that cannot be read fails the test with
`INVALID_CONFIG`. A bad script or an argument passed with anything but a
function fails with `INVALID_ARGUMENT`, and a `Secret` in the argument with
`POLICY_DENIED`.

## Habits

- One goal per `ActAsync`, followed by a check of its outcome.
- Prefer `Expect.That` with a locator when the check is exact. It needs no
  model and does not flake.
- Pass data in `Params`. Use `Values.Unique(...)` for fresh emails or names,
  and a `Secret` for passwords.
- Never add a sleep: wait for the condition with a matcher.
