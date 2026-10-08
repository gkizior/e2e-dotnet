# Exploring without a test

Upstream's `e2e explore` runs the agent against the app with a goal instead
of a test file: it plans one charter at a time, reports findings, and ends
with a verdict. It is not ported. The .NET port has no `explore` command and
no `report_finding` tool.

## What to do instead

- To learn the screens before writing a test, read the app's markup for
  roles and accessible names, or watch a run in a visible browser
  (`E2E_HEADLESS=0`).
- To try a flow with the agent, write a short test that calls
  `Agent.ActAsync` with the goal, then `Agent.AssertAsync` or
  `Expect.That` on the outcome you expect (topic `agent`). A failed step
  carries the model's explanation of what it saw.
- Once the `e2e mcp` sessions are ported, a coding agent can drive the live
  app directly (topic `mcp`).

## When it does not fit

A bug hunt over the whole app (topic `bug-bash`) depends on `e2e explore`,
so it is not available either.
