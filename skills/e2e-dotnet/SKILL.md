---
name: e2e-dotnet
description: Write and run agentic end-to-end tests with the .NET port of e2e. Use when adding an E2E test, an agent act, a locator expectation, or a replay-cache failure in this SDK.
---

# e2e for .NET

The public flow matches [tester-army/e2e](https://github.com/tester-army/e2e). Differences are in `COMPATIBILITY.md`.

## Write a test

```csharp
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

One goal per `ActAsync`. Follow it with `AssertAsync` or `Expect.That` so the replay cache can keep the step. Put fresh emails and timestamps in `Values.Unique`. Put passwords in `Secret.Create` so the model never sees the value.

## Run

```bash
dotnet test
dotnet run --project samples/E2E.Sample
e2e run path/to/Tests.csproj --grep billing
```

A second passing run should report `cache replayed` and zero model calls for that `act`. `assert` still calls the model.

## Cache misses

`no-entry` means nothing is recorded yet. `target-not-found` means the control's role and name changed. `wrong-context` means the path changed. Fix the screen or the instruction; the next verified pass records a new entry.
