# API

- `E2E`: the core. `Agent`, `Screen`, `Expect`, the engines, model clients, config, and the replay cache.
- `E2E.NUnit`: the NUnit runner. Subclass `E2ETest` to get a session per test.
- `E2E.Xunit`: the xUnit v3 runner. Subclass its `E2ETest` to get a session per test.
