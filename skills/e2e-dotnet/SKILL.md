---
name: e2e-dotnet
description: Write and run agentic end-to-end tests with the .NET port of e2e. Use when adding an E2E test, an agent act, a locator expectation, or a replay-cache failure in this SDK.
---

# e2e for .NET

The public flow matches [tester-army/e2e](https://github.com/tester-army/e2e). Differences are in `COMPATIBILITY.md`.

## Write a test

Subclass `E2E.NUnit.E2ETest` and use `[Test]`. `App`, `Agent`, and `Screen` are available after setup. The replay cache commits from the NUnit result. Override `CreateModel` and `CreateEngine` for a scripted model or `DocumentEngine`.

```csharp
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

One goal per `ActAsync`. Follow it with `AssertAsync` or `Expect.That` so the replay cache can keep the step. Put fresh emails and timestamps in `Values.Unique`. Put passwords in `Secret.Create` so the model never sees the value.

## Run

```bash
dotnet test
dotnet test --project samples/E2E.Sample
```

A second passing run of the same test replays the `act` and does not call the model for it. `assert` still calls the model.

## Cache misses

`no-entry` means nothing is recorded yet. `target-not-found` means the control's role and name changed. `wrong-context` means the path changed. Fix the screen or the instruction; the next verified pass records a new entry.
