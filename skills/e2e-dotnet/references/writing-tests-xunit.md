# Writing tests with xUnit v3

The `E2E.XUnit.V3` package holds `E2E.XUnit.V3.E2ETest`, an xUnit v3 base
class. xUnit v2 is not supported. Topic `writing-tests` covers what both
frameworks share: locators, reads, expectations, agent steps, and the
`Browser` member.

## Project

```bash
dotnet new install xunit.v3.templates
dotnet new xunit3 -n MyApp.E2E
dotnet add MyApp.E2E package E2E
dotnet add MyApp.E2E package E2E.XUnit.V3
```

The e2e-dotnet sample and tests run on the VSTest runner: their project
references `xunit.v3.mtp-off`, `xunit.runner.visualstudio`, and
`Microsoft.NET.Test.Sdk`. The `--filter` commands below are for that runner.

Put `e2e.config.json` in the project folder. Topic `setup` covers the config
and the browser install.

The session stops on xUnit's cancellation token, so calls on `App`,
`Screen`, `Agent`, `Browser`, and `Expect.That` do not need a token; the
sample turns off the analyzer warning `xUnit1051` that asks for one.
`Expect.Poll` matchers do not see the session: pass
`Xunit.TestContext.Current.CancellationToken` to `ToSatisfyAsync` or
`ToBeAsync` so a cancelled run stops them. Write `Xunit.TestContext` in
full, because `using E2E;` brings in `E2E.TestContext` too.

## The base class

Derive each test class from `E2ETest`:

```csharp
using E2E;
using E2E.XUnit.V3;

public sealed class BillingTests : E2ETest
{
}
```

- `E2ETest` implements `IAsyncLifetime`. `InitializeAsync` starts one engine
  session per test, so every test gets a fresh browser context.
  `DisposeAsync` commits the replay cache from the xUnit result: a pass or
  a skip records verified acts, a failure evicts the unverified ones, and a
  cancelled test leaves the cache alone.
- To run code before or after each test, override `InitializeAsync` or
  `DisposeAsync` and call the base method: first in `InitializeAsync`, last
  in `DisposeAsync`, inside a `finally` so the session is always closed. A
  check there after a failure verifies nothing, so it never lets an act be
  recorded. A constructor runs before the session exists, so do not use
  `App` or `Screen` there.
- The members are protected properties of the base class: `App`, `Agent`,
  `Screen`, `Browser`, `Secrets`, and `Context`. `Expect` and `Credentials`
  are static classes in the `E2E` namespace.
- Settings come from `e2e.config.json`. Override a virtual member to change
  one value for a class: `CreateEngine()`, `CreateModel()`, `CreateJudge()`,
  `CreateAgents()`, `BaseUrl`, `ActionTimeout`, `CacheMode`, and more.

## Attributes

- `[Fact]` marks a test. `[Theory]` rows work too; each row keeps its own
  replay, keyed by the class, the method, and every argument.
- xUnit has no retry, so every test is a first attempt and can replay.
- `[Trait("TestCategory", "RealModel")]` marks a test that calls a real
  model, so CI can skip it.
- `Assert.Skip(reason)` or `Assert.SkipWhen(condition, reason)` skips a test
  from inside its body.

## Soft checks and polls

- `Expect.Soft(locator)` keeps each failure on the session. The test body
  runs on, and the test fails when it is disposed, with every failure in
  one message.
- `Expect.Poll(read)` retries a value with `ToBeAsync` or
  `ToSatisfyAsync(predicate)`: `await Expect.Poll(() => ReadCountAsync()).ToSatisfyAsync(count => count > 3);`.

## A complete example

```csharp
using E2E;
using E2E.XUnit.V3;

public sealed class DaritenTests : E2ETest
{
    [Fact]
    public async Task The_dashboard_links_to_the_transaction_register()
    {
        await App.OpenAsync("/");
        await Expect.That(Screen.GetByRole("heading", "Dashboard")).ToBeVisibleAsync();

        await Screen.GetByRole("link", "Transactions").ClickAsync();
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync();
    }

    [Fact]
    [Trait("TestCategory", "RealModel")]
    public async Task An_agent_filters_the_register_by_category()
    {
        await App.OpenAsync("/transactions");
        await Agent.ActAsync("filter the register to show only the Groceries category");
        await Agent.AssertAsync("every transaction listed is in the Groceries category, or the register says no transactions match");
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync();
    }
}
```

`samples/E2E.XUnit.V3.Sample` in the e2e-dotnet repo holds a longer version
of this class, run against a live demo.

## Run

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~DaritenTests"            # one class
dotnet test --filter "TestCategory!=RealModel"                    # skip model tests
dotnet test --filter "TestCategory=RealModel"                     # only model tests
```

The trait name is the filter name: `[Trait("TestCategory", "RealModel")]`
is what `TestCategory!=RealModel` matches.
