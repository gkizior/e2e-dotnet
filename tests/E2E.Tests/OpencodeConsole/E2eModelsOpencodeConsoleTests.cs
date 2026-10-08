// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.OpencodeConsole;

public sealed class E2eModelsOpencodeConsoleTests
{
    [Fact]
    public async Task Lists_the_Zen_and_Go_models_the_workspace_serves_Go_ids_prefixed_leaving_out_disabled_free_and_custom_provider_models()
    {
        var api = OpencodeConsoleWorkspace.Serve();
        var store = new MemoryCredentialStore(new() { ["opencode-console"] = OpencodeConsoleWorkspace.Login });
        var provider = new OpenCodeConsoleProvider(consoleUrl: OpencodeConsoleWorkspace.ConsoleUrl);
        using var http = Subscriptions.Client(provider, store, inner: api);

        var models = await provider.ListModelsAsync(http, CancellationToken.None);

        Assert.Equal("org_1", api.Requests.First().Header("x-org-id"));
        Assert.Equal(
            [
                ("claude-sonnet-5", "Claude Sonnet 5", "Zen, $2 in, $10 out per 1M, vision"),
                ("gemini-3.1-pro", null, "Zen, $2 in, $12 out per 1M"),
                ("gpt-5-nano", null, "Zen, $0.05 in, $0.4 out per 1M"),
                ("glm-5.3", null, "Zen, $1.4 in, $4.4 out per 1M"),
                ("go/deepseek-v4.1-flash", "DeepSeek V4.1 Flash", "Go, vision"),
            ],
            models.Select(model => (model.Id, model.Name, model.Detail)));
    }
}
