// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

public sealed class CoreTests
{
    [Fact]
    public async Task Role_and_name_match_one_button()
    {
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/settings/billing");
            await ctx.Screen.GetByRole("button", "Upgrade to Pro").TapAsync();
            await Expect.That(ctx.Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
        });

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Two_buttons_with_the_same_name_are_strict()
    {
        var world = new DocumentWorld().Map("/dup", page =>
        {
            page.Button("Save");
            page.Button("Save");
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/dup");
            await ctx.Screen.GetByRole("button", "Save").TapAsync();
        }, world);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("STRICT_MODE", result.Tests[0].ErrorCode);
    }

    [Fact]
    public async Task First_picks_one_of_many()
    {
        var world = new DocumentWorld().Map("/dup", page =>
        {
            page.Button("Save");
            page.Button("Save");
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/dup");
            await ctx.Screen.GetByRole("button", "Save").First().TapAsync();
        }, world);

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Hidden_status_is_not_visible_until_it_appears()
    {
        DocumentElement? status = null;
        var world = new DocumentWorld().Map("/wait", page =>
        {
            status = page.Status("Ready", hidden: true);
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/wait");
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                status!.Hidden = false;
            });
            await Expect.That(ctx.Screen.GetByRole("status", "Ready")).ToBeVisibleAsync();
        }, world, assertionTimeout: TimeSpan.FromSeconds(2));

        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Expect_times_out_when_the_node_never_appears()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await Expect.That(ctx.Screen.GetByRole("status", "Missing")).ToBeVisibleAsync();
            },
            assertionTimeout: TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("ASSERTION_FAILED", result.Tests[0].ErrorCode);
    }

    [Fact]
    public async Task Agent_act_assert_and_expect_upgrade_the_plan()
    {
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/settings/billing");
            await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
            await ctx.Agent.AssertAsync("the invoice preview shows a prorated amount");
            await Expect.That(ctx.Screen.GetByRole("status")).ToContainTextAsync("Pro");
        }, model: Script());

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.ModelCalls > 0);
    }

    [Fact]
    public async Task Second_run_replays_the_act_without_the_model()
    {
        var directory = TempCache();
        var actCalls = 0;
        ScriptedModel Model()
        {
            return new ScriptedModel(request =>
            {
                var text = string.Join('\n', request.Messages.Select(message => message.Content));
                if (text.Contains("Statement:", StringComparison.Ordinal))
                {
                    return text.Contains("Prorated", StringComparison.Ordinal)
                        ? ModelResponses.Done("passed", "The invoice is prorated.")
                        : ModelResponses.Done("failed", "No prorated amount.", "ASSERTION_FAILED");
                }

                actCalls++;
                if (text.Contains("tapped", StringComparison.Ordinal))
                {
                    return ModelResponses.Done("passed", "Upgraded to Pro.");
                }

                return ModelResponses.Tap("button", "Upgrade to Pro");
            });
        }

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/settings/billing");
            await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
            await ctx.Agent.AssertAsync("the invoice preview shows a prorated amount");
        }

        var first = await RunAsync(Body, model: Model(), cacheDirectory: directory);
        Assert.Equal(0, first.ExitCode);
        Assert.True(actCalls > 0);
        actCalls = 0;

        var second = await RunAsync(Body, model: Model(), cacheDirectory: directory);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(0, actCalls);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task A_missing_control_misses_the_cache_and_runs_live()
    {
        var directory = TempCache();
        var button = "Upgrade to Pro";
        DocumentWorld Current()
        {
            return new DocumentWorld().Map("/settings/billing", page =>
            {
                page.Heading("Billing");
                var status = page.Status("Pro", hidden: true);
                page.Button(button, () => status.Hidden = false);
            });
        }

        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "done");
            }

            if (text.Contains("Switch plan", StringComparison.Ordinal))
            {
                return ModelResponses.Tap("button", "Switch plan");
            }

            return ModelResponses.Tap("button", "Upgrade to Pro");
        });

        await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
                await Expect.That(ctx.Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
            },
            Current(),
            model,
            directory);

        button = "Switch plan";
        var second = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
                await Expect.That(ctx.Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
            },
            Current(),
            model,
            directory);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal(1, second.Missed);
        Assert.Equal(0, second.Replayed);
    }

    [Fact]
    public async Task Secret_value_never_reaches_the_model()
    {
        ScriptedModel? captured = null;
        var world = new DocumentWorld().Map("/login", page =>
        {
            page.Textbox("Password", secure: true);
            page.Paragraph("hint s3cret-value");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/login");
                await ctx.Agent.ActAsync("sign in", new ActOptions
                {
                    Params = new Dictionary<string, object?>
                    {
                        ["password"] = Secret.Create("password", "s3cret-value", "member password"),
                    },
                });
            },
            world,
            model: captured = new ScriptedModel(_ => ModelResponses.Done("passed", "signed in")));

        Assert.Equal(0, result.ExitCode);
        var prompt = string.Join('\n', captured!.Requests.SelectMany(request => request.Messages.Select(message => message.Content)));
        Assert.DoesNotContain("s3cret-value", prompt);
        Assert.Contains("<secret:password>", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unique_values_replay_with_the_new_value()
    {
        var directory = TempCache();
        var email = "ada+1@example.test";
        var world = new DocumentWorld().Map("/signup", page => page.Textbox("Email"));
        ScriptedModel Model() => new(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("filled", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "filled");
            }

            return ModelResponses.Fill("textbox", "Email", email);
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/signup");
            await ctx.Agent.ActAsync("sign up with {email}", new ActOptions
            {
                Params = new Dictionary<string, object?> { ["email"] = Values.Unique(email) },
            });
            await Expect.That(ctx.Screen.GetByLabel("Email")).ToHaveValueAsync(email);
        }

        var first = await RunAsync(Body, world, Model(), directory);
        Assert.Equal(0, first.ExitCode);
        email = "ada+2@example.test";
        var second = await RunAsync(Body, world, Model(), directory);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task Before_each_opens_the_page_and_skip_does_not_fail()
    {
        var suite = new Suite("billing");
        suite.BeforeEach(ctx => ctx.App.OpenAsync("/settings/billing"));
        suite.Test("skipped inside", ctx =>
        {
            ctx.Skip(true, "not ready");
            return Task.CompletedTask;
        });
        var result = await Runner.RunAsync(suite, Options(BillingWorld.Create()));
        Assert.Equal(TestStatus.Skipped, result.Tests[0].Status);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task Only_is_rejected_in_ci()
    {
        var previous = Environment.GetEnvironmentVariable("CI");
        Environment.SetEnvironmentVariable("CI", "true");
        try
        {
            var suite = new Suite();
            suite.Test("focused", _ => Task.CompletedTask, new TestOptions { Only = true });
            var error = await Assert.ThrowsAsync<TestException>(() => Runner.RunAsync(suite, Options(BillingWorld.Create())));
            Assert.Equal("ONLY_IN_CI", error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CI", previous);
        }
    }

    [Fact]
    public void Discovery_reads_attributes()
    {
        DiscoveredFixture.BeforeAllCount = 0;
        var suite = SuiteDiscovery.Discover(typeof(DiscoveredFixture).Assembly);
        var tests = suite.Tests.Where(test => test.Title.StartsWith("discovered >", StringComparison.Ordinal)).ToList();
        Assert.Contains(tests, test => test.Title == "discovered > opens");
        Assert.Contains(tests, test => test.Options.Skip == "not ready");
        Assert.Contains(tests, test => test.Tags.Contains("billing"));
    }

    [Fact]
    public async Task OpenAi_compatible_model_parses_tool_calls()
    {
        var handler = new StubHandler("""
            {
              "choices": [{
                "message": {
                  "content": null,
                  "tool_calls": [{
                    "id": "call_1",
                    "type": "function",
                    "function": { "name": "done", "arguments": "{\"status\":\"passed\",\"summary\":\"ok\"}" }
                  }]
                }
              }],
              "usage": { "prompt_tokens": 4, "completion_tokens": 2 }
            }
            """);
        using var http = new HttpClient(handler);
        var model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "gpt-test", ApiKey = "test" }, http);
        var response = await model.CompleteAsync(new ModelRequest
        {
            System = "system",
            Messages = [new ModelMessage { Role = "user", Content = "hi" }],
            Tools = AgentToolList(),
        }, CancellationToken.None);

        Assert.Equal("done", response.ToolCalls[0].Name);
        Assert.Equal(4, response.Usage!.InputTokens);
        Assert.Contains("/chat/completions", handler.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_report_lists_the_test()
    {
        var markdown = Report.ToMarkdown(new RunResult
        {
            Tests = [new TestResult { Title = "billing > upgrades", Status = TestStatus.Passed }],
            Replayed = 1,
        });
        Assert.Contains("billing > upgrades", markdown, StringComparison.Ordinal);
        Assert.Contains("1 replayed", markdown, StringComparison.Ordinal);
    }

    private static IReadOnlyList<ModelTool> AgentToolList()
    {
        return
        [
            new ModelTool
            {
                Name = "done",
                Description = "finish",
                Parameters = System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object" }),
            },
        ];
    }

    private static ScriptedModel Script()
    {
        return new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("Statement:", StringComparison.Ordinal))
            {
                return text.Contains("Prorated", StringComparison.Ordinal)
                    ? ModelResponses.Done("passed", "prorated")
                    : ModelResponses.Done("failed", "missing", "ASSERTION_FAILED");
            }

            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "upgraded");
            }

            return ModelResponses.Tap("button", "Upgrade to Pro");
        });
    }

    private static string TempCache()
    {
        return Path.Combine(Path.GetTempPath(), "e2e-tests", Guid.NewGuid().ToString("n"));
    }

    private static Task<RunResult> RunAsync(
        Func<TestContext, Task> body,
        DocumentWorld? world = null,
        IAgentModel? model = null,
        string? cacheDirectory = null,
        TimeSpan? assertionTimeout = null)
    {
        var suite = new Suite("billing");
        suite.Test("case", body);
        return Runner.RunAsync(suite, Options(world ?? BillingWorld.Create(), model, cacheDirectory, assertionTimeout));
    }

    private static RunOptions Options(
        DocumentWorld world,
        IAgentModel? model = null,
        string? cacheDirectory = null,
        TimeSpan? assertionTimeout = null)
    {
        return new RunOptions
        {
            Engine = new DocumentEngine(world),
            Model = model,
            BaseUrl = "https://billing.test",
            CacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "e2e-empty", Guid.NewGuid().ToString("n")),
            CacheEnabled = cacheDirectory is not null,
            ReportPath = null,
            AssertionTimeout = assertionTimeout ?? TimeSpan.FromSeconds(2),
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;

        public StubHandler(string body) => _body = body;

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }
}

[E2ESuite("discovered", Tags = new[] { "billing" })]
public sealed class DiscoveredFixture
{
    public static int BeforeAllCount { get; set; }

    [E2EBeforeAll]
    public static void Once() => BeforeAllCount++;

    [E2ETest("opens")]
    public void Opens()
    {
    }

    [E2ETest("later", Skip = "not ready")]
    public void Later()
    {
    }
}
