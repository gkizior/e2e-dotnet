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
    public async Task A_key_press_on_the_focused_control_replays()
    {
        var directory = TempCache();
        static DocumentWorld World() => new DocumentWorld().Map("/todos", page =>
        {
            var status = page.Status("Empty");
            page.Roots.Add(new DocumentElement { Role = "textbox", Name = "New todo", OnFill = value => status.Name = status.Text = "Typed " + value });
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("pressed Enter", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "Added.");
            }

            return text.Contains("filled", StringComparison.Ordinal)
                ? ModelResponses.Call("press", new { key = "Enter" })
                : ModelResponses.Fill("textbox", "New todo", "Buy milk");
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/todos");
            await ctx.Agent.ActAsync("add the todo Buy milk");
            await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("Typed Buy milk");
        }

        var first = await RunAsync(Body, World(), model, directory);
        Assert.Null(first.Error);

        var second = await RunAsync(Body, World(), new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
        Assert.Equal(0, second.ModelCalls);
    }

    [Fact]
    public async Task A_key_press_with_only_a_test_id_goes_to_that_control()
    {
        var world = new DocumentWorld().Map("/form", page =>
        {
            var status = page.Status("Not saved");
            page.Roots.Add(new DocumentElement { Role = "button", Name = "Save", TestId = "save", OnTap = () => status.Name = status.Text = "Saved" });
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("pressed Enter", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Saved.")
                : ModelResponses.Call("press", new { key = "Enter", testId = "save" });
        });

        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/form");
            await ctx.Agent.ActAsync("save the form");
            await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("Saved");
        }, world, model);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task A_repeated_control_is_not_recorded_as_an_end_state_anchor()
    {
        var directory = TempCache();
        static DocumentWorld World() => new DocumentWorld().Map("/todos", page =>
        {
            page.Button("Add two", () =>
            {
                page.Roots.Add(new DocumentElement { Role = "button", Name = "Delete" });
                page.Roots.Add(new DocumentElement { Role = "button", Name = "Delete" });
                page.Roots.Add(new DocumentElement { Role = "status", Name = "Added two", Text = "Added two" });
            });
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Added.")
                : ModelResponses.Tap("button", "Add two");
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/todos");
            await ctx.Agent.ActAsync("add two todos");
            await Expect.That(ctx.Screen.GetByRole("button", "Delete")).ToHaveCountAsync(2);
        }

        var first = await RunAsync(Body, World(), model, directory);
        Assert.Null(first.Error);

        var second = await RunAsync(Body, World(), new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task Act_double_taps_a_control_and_replays_it()
    {
        var directory = TempCache();
        static DocumentWorld World() => new DocumentWorld().Map("/todos", page =>
        {
            var status = page.Status("Not editing");
            var taps = 0;
            page.Button("Buy milk", () =>
            {
                taps++;
                status.Name = status.Text = taps == 2 ? "Editing Buy milk" : "Not editing";
            });
        });
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("double-tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "Editing.")
                : ModelResponses.Call("double_tap", new { role = "button", name = "Buy milk" });
        });

        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/todos");
            await ctx.Agent.ActAsync("start editing Buy milk");
            await Expect.That(ctx.Screen.GetByRole("status")).ToHaveTextAsync("Editing Buy milk");
        }

        var first = await RunAsync(Body, World(), model, directory);
        Assert.Null(first.Error);
        Assert.Contains(model.Requests[0].Tools, tool => tool.Name == "double_tap");

        var second = await RunAsync(Body, World(), new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
        Assert.Equal(0, second.ModelCalls);
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
    public async Task Secret_value_in_a_statement_never_reaches_the_model()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("Instruction:", StringComparison.Ordinal)
                ? ModelResponses.Call("extract", new { data = "ok" })
                : ModelResponses.Done("passed", "ok");
        });
        var world = new DocumentWorld().Map("/login", page =>
        {
            page.Textbox("Password", secure: true);
            page.Paragraph("hint S3CRET-VALUE");
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
                await ctx.Agent.AssertAsync("the hint says S3cret-Value");
                await ctx.Agent.WaitForAsync("the hint says s3cret%2Dvalue");
                await ctx.Agent.ExtractAsync<string>("read s3cret-value from the hint");
            },
            world,
            model: model);

        Assert.Null(result.Error);
        var prompt = string.Join('\n', model.Requests.SelectMany(request => request.Messages.Select(message => message.Content)));
        Assert.DoesNotContain("s3cret", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Statement: the hint says <secret:password>", prompt, StringComparison.Ordinal);
        Assert.Contains("Instruction: read <secret:password> from the hint", prompt, StringComparison.Ordinal);
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
    public async Task Act_out_of_model_calls_is_blocked_on_its_budget()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { MaxModelCalls = 2 });
            },
            model: new ScriptedModel(_ => new ModelResponse { Content = "thinking" }));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.True(error.Blocked);
        Assert.Equal("agent.act exhausted its model-call budget of 2", error.Message);
        Assert.Equal(2, result.ModelCalls);
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
    public void The_key_is_stable_for_identical_parts()
    {
        var key = Key();

        Assert.Matches("^[a-f0-9]{64}$", key);
        Assert.Equal(key, Key());
    }

    public static TheoryData<string> KeyParts => new() { "instruction", "params", "call index", "test", "engine", "agent", "agent context", "agent context, to none" };

    [Theory]
    [MemberData(nameof(KeyParts))]
    public void The_key_changes_when_a_part_changes(string part)
    {
        var changed = part switch
        {
            "instruction" => Key(instruction: "close billing"),
            "params" => Key(parameters: new Dictionary<string, object?> { ["fast"] = true }),
            "call index" => Key(callIndex: 1),
            "test" => Key(test: "other test"),
            "engine" => Key(engine: "web"),
            "agent" => Key(agent: "admin"),
            "agent context" => Key(agentContext: "The billing period is Daily."),
            "agent context, to none" => Key(agentContext: null),
            _ => throw new ArgumentOutOfRangeException(nameof(part)),
        };

        Assert.NotEqual(Key(), changed);
    }

    // Every committed .e2e/cache entry is filed under this key, so a change here misses all of them after an upgrade.
    // Change it only with a COMPATIBILITY.md note saying so.
    [Fact]
    public void The_key_hashes_a_fixed_input_to_the_same_value_across_releases()
    {
        Assert.Equal("093544ebd30c63d91c9979cf818a83da74d42601a0ee3eefbcad99a68b827c34", Key());
        Assert.Equal(
            "c8622dfe855e2864046efc42df132f94c907c27b3c9c3c8fd1e4f2bfc14ed7af",
            Key(instruction: "upgrade to {{plan}}", parameters: new Dictionary<string, object?> { ["plan"] = "Pro", ["seats"] = 3 }, callIndex: 2));
    }

    private static string Key(
        string engine = "document",
        string test = "billing upgrade",
        string instruction = "open billing",
        IReadOnlyDictionary<string, object?>? parameters = null,
        int callIndex = 0,
        string agent = "default",
        string? agentContext = "The billing period is Monthly.")
    {
        return CacheKeys.ForCall(CacheKeys.Create(engine, test, instruction, parameters, agent, agentContext), callIndex);
    }

    [Fact]
    public void Nested_params_key_by_their_content()
    {
        string Key(object? value) => CacheKeys.Create("document", "test", "add items", new Dictionary<string, object?> { ["items"] = value }, "default", null);

        Assert.NotEqual(Key(new[] { "apple" }), Key(new[] { "pear" }));
        Assert.Equal(Key(new[] { Values.Unique("a@example.test") }), Key(new[] { Values.Unique("b@example.test") }));
        Assert.Equal(
            Key(new { password = Secret.Create("password", "one-value") }),
            Key(new { password = Secret.Create("password", "two-value") }));
        Assert.Equal("{\"password\":\"<secret:password>\"}", CacheKeys.Canonical(new { password = Secret.Create("password", "one-value") }));

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

    [Fact]
    public async Task Act_reports_its_model_calls_and_actions()
    {
        ActResult? live = null;
        ActResult? replayed = null;
        var directory = TempCache();
        var first = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                live = await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
                await ctx.Agent.AssertAsync("the invoice preview shows a prorated amount");
            },
            model: Script(),
            cacheDirectory: directory);
        var second = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                replayed = await ctx.Agent.ActAsync("upgrade the workspace to the Pro plan");
            },
            model: Script(),
            cacheDirectory: directory);

        Assert.Null(first.Error);
        Assert.Null(second.Error);
        Assert.Equal(2, live!.ModelCalls);
        Assert.Equal(1, live.Actions);
        Assert.Equal(0, replayed!.ModelCalls);
        Assert.Equal(1, replayed.Actions);
    }

    [Theory]
    [InlineData("passed")]
    [InlineData("failed")]
    public async Task Act_past_its_action_budget_is_blocked(string status)
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("exhausted its action budget of 2", StringComparison.Ordinal)
                ? ModelResponses.Done(status, "Gave up.")
                : ModelResponses.Tap("button", "Upgrade to Pro");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { MaxSteps = 2 });
            },
            model: model);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.True(error.Blocked);
        Assert.Equal(error.Message, error.Explanation);
        Assert.Equal(4, model.CallCount);
    }

    [Fact]
    public async Task A_model_failure_code_outranks_an_exhausted_action_budget()
    {
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("exhausted its action budget", StringComparison.Ordinal)
                ? ModelResponses.Done("failed", "The button does nothing.", "ACTION_FAILED")
                : ModelResponses.Tap("button", "Upgrade to Pro");
        });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { MaxSteps = 1 });
            },
            model: model);

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("ACTION_FAILED", error.Code);
        Assert.False(error.Blocked);
    }

    [Fact]
    public async Task A_blocked_act_is_marked_blocked()
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
            },
            model: new ScriptedModel(_ => ModelResponses.Done("blocked", "No network.", "ENVIRONMENT_UNAVAILABLE")));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.True(error.Blocked);
        Assert.Equal("No network.", error.Explanation);
    }

    public static TheoryData<string> RaisedBudgets => new() { "act-steps", "act-steps-zero", "act-calls", "wait-calls", "wait-calls-negative" };

    [Theory]
    [MemberData(nameof(RaisedBudgets))]
    public async Task A_per_call_budget_can_only_lower_the_configured_one(string which)
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("passed", "ok"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await (which switch
                {
                    "act-steps" => ctx.Agent.ActAsync("upgrade", new ActOptions { MaxSteps = E2EDefaults.MaxSteps + 1 }),
                    "act-steps-zero" => ctx.Agent.ActAsync("upgrade", new ActOptions { MaxSteps = 0 }),
                    "act-calls" => ctx.Agent.ActAsync("upgrade", new ActOptions { MaxModelCalls = E2EDefaults.MaxModelCalls + 1 }),
                    "wait-calls" => ctx.Agent.WaitForAsync("the plan is Pro", new WaitForOptions { MaxModelCalls = E2EDefaults.MaxModelCalls + 1 }),
                    _ => ctx.Agent.WaitForAsync("the plan is Pro", new WaitForOptions { MaxModelCalls = -1 }),
                });
            },
            model: model);

        Assert.Equal("INVALID_ARGUMENT", Assert.IsType<TestException>(result.Error).Code);
        Assert.Equal(0, model.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\u0001b")]
    public void Unique_rejects_empty_and_marker_values(string value)
    {
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => Values.Unique(value)).Code);
    }

    [Fact]
    public void Secret_values_are_at_least_six_code_points()
    {
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => Secret.Create("pin", "12345")).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => Secret.Create("pin", "")).Code);
        Assert.Equal("INVALID_ARGUMENT", Assert.Throws<TestException>(() => Secret.Create("pin", "ab\U0001F600cd")).Code);
        Assert.Equal("pin", Secret.Create("pin", "ab\U0001F600cde").Name);
        Assert.Equal("pin", Secret.Create("pin", "123456").Name);
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

    internal static string TempCache()
    {
        return Path.Combine(Path.GetTempPath(), "e2e-tests", Guid.NewGuid().ToString("n"));
    }

    internal readonly record struct Attempt(Exception? Error, int ModelCalls, int Replayed, int Missed);

    internal static async Task<Attempt> RunAsync(
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
            ReplayTimeout = actionTimeout ?? TimeSpan.FromMilliseconds(300),
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
