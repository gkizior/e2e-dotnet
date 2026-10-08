# Writing tests with NUnit

The `E2E.NUnit` package holds `E2E.NUnit.E2ETest`, an NUnit base class.
Topic `writing-tests` covers what both frameworks share: locators, reads,
expectations, agent steps, and the `Browser` member.

## Project

```bash
dotnet new nunit -n MyApp.E2E
dotnet add MyApp.E2E package E2E
dotnet add MyApp.E2E package E2E.NUnit
```

Put `e2e.config.json` in the project folder. Topic `setup` covers the config
and the browser install.

## The base class

Derive each test class from `E2ETest`:

```csharp
using E2E;
using E2E.NUnit;

public sealed class BillingTests : E2ETest
{
}
```

- Its `[SetUp]` starts one engine session per test, so every test gets a
  fresh browser context. Its `[TearDown]` commits the replay cache from the
  NUnit result: a pass or a skip records verified acts, a failure evicts
  the unverified ones, and a cancelled test leaves the cache alone.
- Your own `[SetUp]` runs after the base class's, so the session is ready
  there. Your `[TearDown]` runs before the base class's. A check there after
  a failure verifies nothing, so it never lets an act be recorded.
- The members are protected properties of the base class: `App`, `Agent`,
  `Screen`, `Browser`, `Secrets`, and `Context`. `Expect` and `Credentials`
  are static classes in the `E2E` namespace.
- Settings come from `e2e.config.json`. Override a virtual member to change
  one value for a class: `CreateEngine()`, `CreateModel()`, `CreateJudge()`,
  `CreateAgents()`, `BaseUrl`, `ActionTimeout`, `CacheMode`, and more.

## Attributes

- `[Test]` marks a test; `[TestCase]` rows work too.
- `[Retry(n)]`: the first attempt can replay the cache; later attempts run
  live and still record. Each `[Repeat]` iteration is a first attempt.
- `[Category("RealModel")]` marks a test that calls a real model, so CI can
  skip it.
- `Assert.Ignore(reason)` or `Assume.That(...)` skips a test from inside its body.

## Soft checks and polls

- `Expect.Soft(locator)` records each failure on the NUnit result, as inside
  `Assert.EnterMultipleScope`. The test body runs on, and the test fails
  when it ends, with every failure listed.
- `Expect.Poll(read)` retries a value. `E2E.NUnit` adds `ToMatchAsync` with
  any NUnit constraint: `await Expect.Poll(() => ReadCountAsync()).ToMatchAsync(Is.GreaterThan(3));`.
  `ToBeAsync` and `ToSatisfyAsync(predicate)` work too.

## A complete example

```csharp
using E2E;
using E2E.NUnit;

public sealed class DaritenTests : E2ETest
{
    [Test]
    public async Task The_dashboard_links_to_the_transaction_register()
    {
        await App.OpenAsync("/");
        await Expect.That(Screen.GetByRole("heading", "Dashboard")).ToBeVisibleAsync();

        await Screen.GetByRole("link", "Transactions").ClickAsync();
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync();
    }

    [Test]
    [Category("RealModel")]
    public async Task An_agent_filters_the_register_by_category()
    {
        await App.OpenAsync("/transactions");
        await Agent.ActAsync("filter the register to show only the Groceries category");
        await Agent.AssertAsync("every transaction listed is in the Groceries category, or the register says no transactions match");
        await Expect.That(Screen.GetByRole("heading", "Transactions")).ToBeVisibleAsync();
    }
}
```

`samples/E2E.Sample` in the e2e-dotnet repo holds a longer version of this
class, run against a live demo.

## Run

```bash
dotnet test
dotnet test --filter "FullyQualifiedName~DaritenTests"            # one class
dotnet test --filter "Name=The_dashboard_links_to_the_transaction_register"
dotnet test --filter "TestCategory!=RealModel"                    # skip model tests
dotnet test --filter "TestCategory=RealModel"                     # only model tests
```
