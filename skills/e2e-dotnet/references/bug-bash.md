# Running a bug bash

Upstream's bug bash is many `e2e explore` runs at once, one charter each,
then a verification pass that turns every claimed bug into a repro test that
fails for the reason reported. `e2e explore` is not ported (topic
`explore`), so the fan-out part is not available in this port.

## What still applies

The verification half works with plain tests. For each suspected bug:

1. Write one test that reproduces it: open the screen with `App.OpenAsync`,
   reach the state with `Screen` actions or `Agent.ActAsync`, and pin the
   expected behavior with `Expect.That` or `Agent.AssertAsync` (topic
   `writing-tests`).
2. Run it with `dotnet test --filter "FullyQualifiedName~<Test>"`. It must
   fail, and fail for the reason reported, not for a locator or setup
   problem (topic `debugging`).
3. Run it again. A bug that fails only some of the time is a flake until
   the cause is known.

## Rules

- Report confirmed bugs only, each with its failing test.
- Give each test its own data (`Values.Unique(...)`) and account, so tests
  do not report each other's edits as bugs.
- Never weaken an expectation to make a repro pass or fail.
