---
_layout: landing
title: Home
---

# E2E for .NET

Agentic end-to-end tests for .NET. Describe a goal in plain language, let an agent drive the app in Chromium, then check the result with locators. A verified agent step is recorded and replays on the next run with no model call.

```bash
dotnet add package E2E
dotnet add package E2E.NUnit   # or E2E.XUnit.V3 for xUnit v3
```

- [Guide](../README.md): install, write a test, config, model providers, and subscriptions.
- [API](api/index.md): every public type in `E2E`, `E2E.NUnit`, and `E2E.XUnit.V3`.
- [Compatibility](../COMPATIBILITY.md): how this port differs from the TypeScript [e2e](https://github.com/tester-army/e2e).
