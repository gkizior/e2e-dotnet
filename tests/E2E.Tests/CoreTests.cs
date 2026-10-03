// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
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

        Assert.Null(result.Error);
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

        var error = Assert.IsType<TestException>(result.Error);
        Assert.Equal("STRICT_MODE", error.Code);
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

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Text_query_matches_the_innermost_node_only()
    {
        var world = new DocumentWorld().Map("/total", page =>
        {
            page.Paragraph("Total: 42").Children.Add(new DocumentElement { Text = "Total: 42" });
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/total");
            await Expect.That(ctx.Screen.GetByText("Total: 42")).ToHaveCountAsync(1);
            Assert.Equal("Total: 42", await ctx.Screen.GetByText("Total: 42").TextContentAsync());
        }, world);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Filter_has_text_ignores_case()
    {
        var world = new DocumentWorld().Map("/buttons", page =>
        {
            page.Button("Save draft");
            page.Button("Cancel");
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/buttons");
            await ctx.Screen.GetByRole("button").Filter("SAVE").TapAsync();
        }, world);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Tap_waits_for_a_button_that_appears_later()
    {
        DocumentElement? save = null;
        var tapped = false;
        var world = new DocumentWorld().Map("/later", page =>
        {
            save = page.Button("Save", () => tapped = true);
            save.Hidden = true;
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/later");
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                save!.Hidden = false;
            });
            await ctx.Screen.GetByRole("button", "Save").TapAsync();
        }, world, actionTimeout: TimeSpan.FromSeconds(2));

        Assert.Null(result.Error);
        Assert.True(tapped);
    }

    [Fact]
    public async Task Tap_waits_for_a_disabled_button_to_become_enabled()
    {
        DocumentElement? save = null;
        var tapped = false;
        var world = new DocumentWorld().Map("/later", page =>
        {
            save = page.Button("Save", () => tapped = true);
            save.Disabled = true;
        });
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/later");
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                save!.Disabled = false;
            });
            await ctx.Screen.GetByRole("button", "Save").TapAsync();
        }, world, actionTimeout: TimeSpan.FromSeconds(2));

        Assert.Null(result.Error);
        Assert.True(tapped);
    }

    [Fact]
    public async Task Tap_fails_after_the_action_timeout_when_nothing_matches()
    {
        var waited = TimeSpan.Zero;
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/settings/billing");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await ctx.Screen.GetByRole("button", "Missing").TapAsync();
            }
            finally
            {
                waited = watch.Elapsed;
            }
        });

        var error = Assert.IsType<TestException>(result.Error);
        Assert.Equal("NOT_FOUND", error.Code);
        Assert.True(waited >= TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public async Task Tap_fails_after_the_action_timeout_when_the_button_stays_disabled()
    {
        var world = new DocumentWorld().Map("/disabled", page => page.Button("Save").Disabled = true);
        var waited = TimeSpan.Zero;
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/disabled");
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await ctx.Screen.GetByRole("button", "Save").TapAsync();
            }
            finally
            {
                waited = watch.Elapsed;
            }
        }, world);

        var error = Assert.IsType<TestException>(result.Error);
        Assert.Equal("NOT_ACTIONABLE", error.Code);
        Assert.True(waited >= TimeSpan.FromMilliseconds(300));
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

        Assert.Null(result.Error);
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

        var error = Assert.IsType<TestException>(result.Error);
        Assert.Equal("ASSERTION_FAILED", error.Code);
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

        Assert.Null(result.Error);
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
        Assert.Null(first.Error);
        Assert.True(actCalls > 0);
        actCalls = 0;

        var second = await RunAsync(Body, model: Model(), cacheDirectory: directory);
        Assert.Null(second.Error);
        Assert.Equal(0, actCalls);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task Replay_waits_for_an_end_state_that_shows_up_late()
    {
        var directory = TempCache();
        var delay = TimeSpan.Zero;
        var world = new DocumentWorld().Map("/settings/billing", page =>
        {
            page.Heading("Billing");
            var status = page.Status("Pro", hidden: true);
            page.Button("Upgrade to Pro", () =>
            {
                if (delay == TimeSpan.Zero)
                {
                    status.Hidden = false;
                    return;
                }

                _ = Task.Delay(delay).ContinueWith(_ => status.Hidden = false, TaskScheduler.Default);
            });
        });

        var modelCalls = 0;
        var model = new ScriptedModel(request =>
        {
            modelCalls++;
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Upgraded to Pro.")
                : ModelResponses.Tap("button", "Upgrade to Pro");
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/settings/billing");
            await ctx.Agent.ActAsync("upgrade");
            await Expect.That(ctx.Screen.GetByRole("status", "Pro")).ToBeVisibleAsync();
        }

        var first = await RunAsync(Body, world, model, directory);
        Assert.Null(first.Error);
        modelCalls = 0;

        delay = TimeSpan.FromMilliseconds(50);
        var second = await RunAsync(Body, world, model, directory);
        Assert.Null(second.Error);
        Assert.Equal(0, modelCalls);
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

        Assert.Null(second.Error);
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

        Assert.Null(result.Error);
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
        Assert.Null(first.Error);
        email = "ada+2@example.test";
        var second = await RunAsync(Body, world, Model(), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("STEP_TIMEOUT")]
    [InlineData("MADE_UP")]
    public async Task A_failed_act_without_a_model_code_is_an_action_failure(string? code)
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
            },
            model: new ScriptedModel(_ => ModelResponses.Done("failed", "No upgrade button.", code)));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("ACTION_FAILED", error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ASSERTION_FAILED")]
    [InlineData("STEP_TIMEOUT")]
    public async Task A_blocked_act_needs_a_blockable_code(string? code)
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("failed: ", StringComparison.Ordinal)
                ? ModelResponses.Done("blocked", "No network.", "ENVIRONMENT_UNAVAILABLE")
                : ModelResponses.Done("blocked", "Cannot upgrade.", code);
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
            },
            model: model);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("ENVIRONMENT_UNAVAILABLE", error.Code);
        Assert.Equal(2, model.CallCount);
    }

    [Fact]
    public async Task Act_without_a_verdict_has_no_conclusion()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { MaxModelCalls = 2 });
            },
            model: new ScriptedModel(_ => new ModelResponse { Content = "thinking" }));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_NO_CONCLUSION", error.Code);
    }

    [Theory]
    [InlineData("ASSERTION_INCONCLUSIVE", "ASSERTION_INCONCLUSIVE")]
    [InlineData("AUTOMATION_UNSUPPORTED", "ASSERTION_FAILED")]
    [InlineData(null, "ASSERTION_FAILED")]
    public async Task Assert_fails_only_with_judge_codes(string? code, string expected)
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.AssertAsync("the plan is Pro");
            },
            model: new ScriptedModel(_ => ModelResponses.Done("failed", "Not Pro.", code)));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public async Task WaitFor_keeps_waiting_after_a_blocked_judgment()
    {
        DocumentElement? status = null;
        var world = new DocumentWorld().Map("/wait", page =>
        {
            status = page.Status("Ready", hidden: true);
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("Ready", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "Ready.");
            }

            return ModelResponses.Done("blocked", "Not ready yet.", "AUTOMATION_UNSUPPORTED");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/wait");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(80);
                    status!.Hidden = false;
                });
                await ctx.Agent.WaitForAsync("the status is shown");
            },
            world,
            model);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Identical_acts_in_one_test_replay_their_own_recordings()
    {
        var directory = TempCache();
        var world = new DocumentWorld().Map("/wizard", page =>
        {
            var done = page.Status("Done", hidden: true);
            DocumentElement? next = null;
            DocumentElement? finish = null;
            next = page.Button("Next", () =>
            {
                next!.Hidden = true;
                finish!.Hidden = false;
            });
            finish = page.Button("Finish", () => done.Hidden = false);
            finish.Hidden = true;
        });
        var actCalls = 0;
        var model = new ScriptedModel(request =>
        {
            actCalls++;
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "advanced");
            }

            return text.Contains("button \"Next\"", StringComparison.Ordinal)
                ? ModelResponses.Tap("button", "Next")
                : ModelResponses.Tap("button", "Finish");
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/wizard");
            await ctx.Agent.ActAsync("go to the next step");
            await ctx.Agent.ActAsync("go to the next step");
            await Expect.That(ctx.Screen.GetByRole("status", "Done")).ToBeVisibleAsync();
        }

        var first = await RunAsync(Body, world, model, directory);
        Assert.Null(first.Error);
        actCalls = 0;

        var second = await RunAsync(Body, world, model, directory);
        Assert.Null(second.Error);
        Assert.Equal(0, actCalls);
        Assert.Equal(2, second.Replayed);
        Assert.Equal(0, second.Missed);
    }

    [Fact]
    public void Nested_params_key_by_their_content()
    {
        string Key(object? value) => CacheKeys.Create("document", "1.0.0", "test", "add items", new Dictionary<string, object?> { ["items"] = value });

        Assert.NotEqual(Key(new[] { "apple" }), Key(new[] { "pear" }));
        Assert.Equal(Key(new[] { Values.Unique("a@example.test") }), Key(new[] { Values.Unique("b@example.test") }));
        Assert.Equal(
            Key(new { password = Secret.Create("password", "one") }),
            Key(new { password = Secret.Create("password", "two") }));
        Assert.Equal("{\"password\":\"<secret:password>\"}", CacheKeys.Canonical(new { password = Secret.Create("password", "one") }));

        var cycle = new List<object>();
        cycle.Add(cycle);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => Key(cycle)).Code);
    }

    [Fact]
    public async Task Nested_params_reach_the_model_as_json()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("passed", "added"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("add {items} to the cart", new ActOptions
                {
                    Params = new Dictionary<string, object?>
                    {
                        ["items"] = new object[] { "apple", Values.Unique("pear-1"), Secret.Create("coupon", "s3cret") },
                    },
                });
            },
            model: model);

        Assert.Null(result.Error);
        var prompt = string.Join('\n', model.Requests.SelectMany(request => request.Messages.Select(message => message.Content)));
        Assert.Contains("Goal: add [\"apple\",\"pear-1\",\"<secret:coupon>\"] to the cart", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Object[]", prompt, StringComparison.Ordinal);
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

    private readonly record struct Attempt(Exception? Error, int ModelCalls, int Replayed, int Missed);

    private static async Task<Attempt> RunAsync(
        Func<TestContext, Task> body,
        DocumentWorld? world = null,
        IAgentModel? model = null,
        string? cacheDirectory = null,
        TimeSpan? assertionTimeout = null,
        TimeSpan? actionTimeout = null)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world ?? BillingWorld.Create()),
            Model = model,
            BaseUrl = "https://billing.test",
            Cache = cacheDirectory is null ? null : new FileStepCache(cacheDirectory),
            CacheEnabled = cacheDirectory is not null,
            TestTitle = "billing > case",
            AssertionTimeout = assertionTimeout ?? TimeSpan.FromSeconds(2),
            ActionTimeout = actionTimeout ?? TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        Exception? error = null;
        try
        {
            await body(session.Context);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return new Attempt(error, session.ModelCalls, session.Replayed, session.Missed);
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
