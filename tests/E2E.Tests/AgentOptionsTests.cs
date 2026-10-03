// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;

namespace E2E.Tests;

public sealed class AgentOptionsTests
{
    [Fact]
    public async Task Params_over_64_KiB_are_rejected_before_any_model_call()
    {
        var model = Passing();
        var error = await FailsAsync<TestException>(
            ctx => ctx.Agent.ActAsync("upgrade", new ActOptions { Params = new Dictionary<string, object?> { ["note"] = new string('a', 65_536) } }),
            model);

        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal("agent.act params are 65547 canonical bytes; the maximum is 65536", error.Message);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task Params_count_a_secret_by_its_projection_and_a_unique_by_its_value()
    {
        var model = Passing();
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions
                {
                    Params = new Dictionary<string, object?>
                    {
                        ["password"] = Secret.Create("password", new string('s', 70_000)),
                        ["note"] = Values.Unique(new string('a', 65_000)),
                    },
                });
            },
            model);

        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Params_deeper_than_32_levels_are_rejected()
    {
        object? Nest(int levels) => levels == 0 ? "leaf" : new Dictionary<string, object?> { ["inner"] = Nest(levels - 1) };

        var model = Passing();
        var error = await FailsAsync<TestException>(
            ctx => ctx.Agent.ActAsync("upgrade", new ActOptions { Params = new Dictionary<string, object?> { ["deep"] = Nest(32) } }),
            model);
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal("agent.act params exceeds 32 levels of nesting", error.Message);

        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { Params = new Dictionary<string, object?> { ["deep"] = Nest(31) } });
            },
            model);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Params_with_a_cycle_are_rejected_and_a_shared_value_is_not()
    {
        var loop = new List<object?>();
        loop.Add(loop);
        var model = Passing();
        var error = await FailsAsync<TestException>(
            ctx => ctx.Agent.ActAsync("upgrade", new ActOptions { Params = new Dictionary<string, object?> { ["loop"] = loop } }),
            model);
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal("agent.act params contains a cycle", error.Message);

        var shared = new[] { "a", "b" };
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { Params = new Dictionary<string, object?> { ["one"] = shared, ["two"] = shared } });
            },
            model);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Params_with_a_non_finite_number_are_rejected()
    {
        var error = await FailsAsync<TestException>(
            ctx => ctx.Agent.ActAsync("upgrade", new ActOptions { Params = new Dictionary<string, object?> { ["price"] = double.NaN } }),
            Passing());

        Assert.Equal("agent.act params contains a non-finite number", error.Message);
    }

    [Fact]
    public async Task An_instruction_over_8_KiB_is_rejected()
    {
        var error = await FailsAsync<TestException>(ctx => ctx.Agent.ActAsync(new string('x', 8_193)), Passing());

        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal("agent.act instruction is 8193 bytes; the maximum is 8192", error.Message);
    }

    public static TheoryData<string> BadCallOptions => new() { "act-timeout", "assert-timeout", "extract-timeout", "interval-low", "interval-high" };

    [Theory]
    [MemberData(nameof(BadCallOptions))]
    public async Task Per_call_timeouts_and_intervals_are_range_checked(string which)
    {
        var model = Passing();
        var error = await FailsAsync<TestException>(
            ctx => which switch
            {
                "act-timeout" => ctx.Agent.ActAsync("upgrade", new ActOptions { Timeout = TimeSpan.Zero }),
                "assert-timeout" => ctx.Agent.AssertAsync("the plan is Pro", new AssertOptions { Timeout = TimeSpan.FromSeconds(-1) }),
                "extract-timeout" => ctx.Agent.ExtractAsync<string>("the plan", new ExtractOptions { Timeout = TimeSpan.Zero }),
                "interval-low" => ctx.Agent.WaitForAsync("the plan is Pro", new WaitForOptions { Interval = TimeSpan.FromMilliseconds(99) }),
                _ => ctx.Agent.WaitForAsync("the plan is Pro", new WaitForOptions { Interval = TimeSpan.FromSeconds(61) }),
            },
            model);

        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal(0, model.CallCount);
    }

    public static TheoryData<string, string> BadAgents => new()
    {
        { "steps-high", "agents.default.maxSteps must be an integer from 1 to 100" },
        { "calls-zero", "agents.default.maxModelCalls must be an integer from 1 to 100" },
        { "judgment", "agents.default.judgmentTimeout must be a positive integer of milliseconds" },
        { "context", "agents.default.context is 16385 bytes; the maximum is 16384" },
        { "provider", "agents.default.providerOptions.openai must be an object of provider options" },
        { "named", "agents.fast.maxSteps must be an integer from 1 to 100" },
        { "default", "Agents cannot hold \"default\"" },
    };

    [Theory]
    [MemberData(nameof(BadAgents))]
    public async Task Configured_agents_are_checked_when_the_session_starts(string which, string message)
    {
        var options = which switch
        {
            "steps-high" => Options(maxSteps: 101),
            "calls-zero" => Options(maxModelCalls: 0),
            "judgment" => Options(judgmentTimeout: TimeSpan.Zero),
            "context" => Options(context: new string('c', 16_385)),
            "provider" => Options(providerOptions: new Dictionary<string, JsonElement> { ["openai"] = JsonSerializer.SerializeToElement(3) }),
            "named" => Options(agents: new Dictionary<string, AgentOptions> { ["fast"] = new() { MaxSteps = 0 } }),
            _ => Options(agents: new Dictionary<string, AgentOptions> { ["default"] = new() }),
        };

        var error = await Assert.ThrowsAsync<ConfigurationException>(() => E2ESession.StartAsync(options));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.StartsWith(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task System_reaches_only_the_act_loop_and_context_reaches_every_call()
    {
        var model = Script();
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
                await ctx.Agent.AssertAsync("the invoice is Prorated");
            },
            model,
            options => Options(model, system: "Read the price twice.", context: "Plans live under Billing."));

        Assert.Null(result.Error);
        var act = model.Requests.First(request => request.Tools.Any(tool => tool.Name == "tap"));
        Assert.Contains("Read the price twice.", act.System, StringComparison.Ordinal);
        Assert.Contains("Project context:\nPlans live under Billing.", act.System, StringComparison.Ordinal);
        var judge = model.Requests.Last();
        Assert.DoesNotContain("Read the price twice.", judge.System, StringComparison.Ordinal);
        Assert.Contains("<project-context>\nPlans live under Billing.\n</project-context>", judge.System, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_judge_decides_assert_waitFor_and_extract_and_the_model_acts()
    {
        var model = Script();
        var judge = new ScriptedModel(request => request.Tools.Any(tool => tool.Name == "extract")
            ? ModelResponses.Call("extract", new { data = "Pro" })
            : ModelResponses.Done("passed", "holds"));
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
                await ctx.Agent.AssertAsync("the invoice is Prorated");
                await ctx.Agent.WaitForAsync("the plan is Pro");
                Assert.Equal("Pro", await ctx.Agent.ExtractAsync<string>("the plan"));
            },
            model,
            _ => Options(model, judge: judge));

        Assert.Null(result.Error);
        Assert.All(model.Requests, request => Assert.Contains(request.Tools, tool => tool.Name == "tap"));
        Assert.Equal(3, judge.CallCount);
    }

    [Fact]
    public async Task A_call_names_its_agent_and_takes_that_agents_model_and_budgets()
    {
        var model = Script();
        var careful = new ScriptedModel(_ => new ModelResponse { Content = "thinking" });
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { Agent = "careful" });
            },
            model,
            _ => Options(model, agents: new Dictionary<string, AgentOptions> { ["careful"] = new() { Model = careful, MaxModelCalls = 3 } }));

        var error = Assert.IsType<AgentException>(result.Error);
        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.Equal("agent.act exhausted its model-call budget of 3", error.Message);
        Assert.Equal(3, careful.CallCount);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task A_named_agent_does_not_inherit_the_default_agents_values()
    {
        var model = Script();
        var careful = Script();
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade", new ActOptions { Agent = "careful", MaxSteps = 20 });
            },
            model,
            _ => Options(model, maxSteps: 5, context: "default only", agents: new Dictionary<string, AgentOptions> { ["careful"] = new() { Model = careful } }));

        Assert.Null(result.Error);
        Assert.DoesNotContain("default only", careful.Requests[0].System, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("nobody", "unknown agent \"nobody\"; configured: default, careful")]
    [InlineData(" ", "agent must be the name of a configured agent")]
    public async Task An_unknown_agent_name_is_an_invalid_argument(string name, string message)
    {
        var model = Script();
        var error = await FailsAsync<TestException>(
            ctx => ctx.Agent.AssertAsync("the plan is Pro", new AssertOptions { Agent = name }),
            model,
            _ => Options(model, agents: new Dictionary<string, AgentOptions> { ["careful"] = new() { Model = model } }));

        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Equal(message, error.Message);
        Assert.Equal(0, model.CallCount);
    }

    [Fact]
    public async Task A_judgment_runs_within_the_agents_judgment_timeout()
    {
        var slow = new SlowModel();
        var started = DateTime.UtcNow;
        var error = await FailsAsync<AgentException>(
            ctx => ctx.Agent.AssertAsync("the plan is Pro"),
            slow,
            _ => Options(slow, judgmentTimeout: TimeSpan.FromMilliseconds(200)));

        Assert.Equal("STEP_TIMEOUT", error.Code);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(4));
    }

    [Fact]
    public async Task WaitFor_stops_at_its_model_call_budget_with_the_last_judgment()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("failed", "the plan is Free", "ASSERTION_FAILED"));
        var error = await FailsAsync<AgentException>(
            ctx => ctx.Agent.WaitForAsync("the plan is Pro", new WaitForOptions { MaxModelCalls = 1 }),
            model);

        Assert.Equal("STEP_BUDGET_EXHAUSTED", error.Code);
        Assert.Equal("waitFor exhausted its model-call budget; last judgment: the plan is Free", error.Message);
        Assert.False(error.Blocked);
        Assert.Equal(1, model.CallCount);
    }

    [Fact]
    public async Task WaitFor_times_out_with_the_last_judgment()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("failed", "the plan is Free", "ASSERTION_FAILED"));
        var error = await FailsAsync<AgentException>(
            ctx => ctx.Agent.WaitForAsync("the plan is Pro", new WaitForOptions { Timeout = TimeSpan.FromMilliseconds(300), Interval = TimeSpan.FromMilliseconds(100) }),
            model);

        Assert.Equal("STEP_TIMEOUT", error.Code);
        Assert.Equal("waitFor timed out; last judgment: the plan is Free", error.Message);
    }

    [Fact]
    public async Task Provider_options_ride_every_model_request()
    {
        var model = Script();
        var providerOptions = new Dictionary<string, JsonElement> { ["openai"] = JsonSerializer.SerializeToElement(new { reasoning_effort = "low" }) };
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await ctx.Agent.ActAsync("upgrade");
                await ctx.Agent.AssertAsync("the invoice is Prorated");
            },
            model,
            _ => Options(model, providerOptions: providerOptions));

        Assert.Null(result.Error);
        Assert.All(model.Requests, request => Assert.Same(providerOptions, request.ProviderOptions));
    }

    [Fact]
    public async Task OpenAi_compatible_model_adds_its_provider_options_to_the_body()
    {
        var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        using var client = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "m", ApiKey = "k", BaseUrl = "https://llm.test/v1" }, http);
        await client.CompleteAsync(
            new ModelRequest
            {
                System = "s",
                Messages = [new ModelMessage { Role = "user", Content = "hi" }],
                Tools = [],
                ProviderOptions = new Dictionary<string, JsonElement>
                {
                    ["openai"] = JsonSerializer.SerializeToElement(new { reasoning_effort = "low", store = false }),
                    ["anthropic"] = JsonSerializer.SerializeToElement(new { thinking = true }),
                },
            },
            CancellationToken.None);

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("low", body.RootElement.GetProperty("reasoning_effort").GetString());
        Assert.False(body.RootElement.GetProperty("store").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("thinking", out _));
        Assert.Equal("m", body.RootElement.GetProperty("model").GetString());

        var error = await Assert.ThrowsAsync<ConfigurationException>(() => client.CompleteAsync(
            new ModelRequest
            {
                System = "s",
                Messages = [],
                Tools = [],
                ProviderOptions = new Dictionary<string, JsonElement> { ["openai"] = JsonSerializer.SerializeToElement(new { model = "other" }) },
            },
            CancellationToken.None));
        Assert.Equal("providerOptions.openai.model cannot replace a field the client sets", error.Message);
    }

    [Fact]
    public async Task Extract_sends_the_schema_of_the_type_and_returns_valid_data()
    {
        var model = new ScriptedModel(_ => ModelResponses.Call("extract", new { data = new { name = "Pro", price = 12.5 } }));
        Plan? plan = null;
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                plan = await ctx.Agent.ExtractAsync<Plan>("the plan");
            },
            model);

        Assert.Null(result.Error);
        Assert.Equal(new Plan("Pro", 12.5m), plan);
        var data = model.Requests[0].Tools.Single(tool => tool.Name == "extract").Parameters.GetProperty("properties").GetProperty("data");
        Assert.True(data.GetProperty("properties").TryGetProperty("price", out _));
    }

    [Fact]
    public async Task Extract_repairs_an_invalid_answer_once()
    {
        var model = new ScriptedModel(request => request.Messages.Count == 1
            ? ModelResponses.Call("extract", new { data = new { name = "Pro", price = "twelve" } })
            : ModelResponses.Call("extract", new { data = new { name = "Pro", price = 12 } }));
        Plan? plan = null;
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                plan = await ctx.Agent.ExtractAsync<Plan>("the plan");
            },
            model);

        Assert.Null(result.Error);
        Assert.Equal(new Plan("Pro", 12m), plan);
        Assert.Equal(2, model.CallCount);
        var repair = model.Requests[1].Messages[^1].Content!;
        Assert.Contains("<previous-attempt-rejected>", repair, StringComparison.Ordinal);
        Assert.Contains("previous response: {\"name\":\"Pro\",\"price\":\"twelve\"}", repair, StringComparison.Ordinal);
        Assert.Contains("The value must be a JSON object whose top-level fields include: price.", repair, StringComparison.Ordinal);
        Assert.Contains(model.Requests[1].Messages, message => message.Role == "tool" && message.ToolCallId == "call_extract");
    }

    [Fact]
    public async Task Extract_fails_with_model_output_invalid_after_one_repair()
    {
        var model = new ScriptedModel(_ => ModelResponses.Call("extract", new { data = new { name = "Pro" } }));
        var error = await FailsAsync<AgentException>(ctx => ctx.Agent.ExtractAsync<Plan>("the plan"), model);

        Assert.Equal("MODEL_OUTPUT_INVALID", error.Code);
        Assert.StartsWith("extracted data failed schema validation: ", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, model.CallCount);
    }

    [Fact]
    public async Task Extract_rejects_a_null_member_of_a_non_nullable_type()
    {
        var model = new ScriptedModel(_ => ModelResponses.Call("extract", new { data = new { name = (string?)null, price = 1 } }));
        var error = await FailsAsync<AgentException>(ctx => ctx.Agent.ExtractAsync<Plan>("the plan"), model);

        Assert.Equal("MODEL_OUTPUT_INVALID", error.Code);
        Assert.Equal(2, model.CallCount);
    }

    [Fact]
    public async Task Extract_is_inconclusive_when_the_judge_finds_nothing()
    {
        var model = new ScriptedModel(_ => ModelResponses.Done("failed", "no plan is shown", "ASSERTION_INCONCLUSIVE"));
        var error = await FailsAsync<AgentException>(ctx => ctx.Agent.ExtractAsync<Plan>("the plan"), model);

        Assert.Equal("ASSERTION_INCONCLUSIVE", error.Code);
        Assert.Equal("nothing to extract: no plan is shown", error.Message);
        Assert.Equal(1, model.CallCount);
    }

    [Fact]
    public async Task Extract_without_a_tool_call_gets_one_repair_then_fails()
    {
        var model = new ScriptedModel(_ => new ModelResponse { Content = "Pro" });
        var error = await FailsAsync<AgentException>(ctx => ctx.Agent.ExtractAsync<Plan>("the plan"), model);

        Assert.Equal("MODEL_OUTPUT_INVALID", error.Code);
        Assert.StartsWith("extract did not return data", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, model.CallCount);
    }

    private sealed record Plan(string Name, decimal Price);

    private static ScriptedModel Passing() => new(_ => ModelResponses.Done("passed", "ok"));

    // Taps the upgrade button, then passes; any judgment holds.
    private static ScriptedModel Script()
    {
        return new ScriptedModel(request =>
        {
            if (request.Tools.All(tool => tool.Name != "tap"))
            {
                return ModelResponses.Done("passed", "holds");
            }

            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            return text.Contains("tapped", StringComparison.Ordinal)
                ? ModelResponses.Done("passed", "upgraded")
                : ModelResponses.Tap("button", "Upgrade to Pro");
        });
    }

    private static E2ESessionOptions Options(
        IAgentModel? model = null,
        IAgentModel? judge = null,
        string? system = null,
        string? context = null,
        int maxSteps = E2EDefaults.MaxSteps,
        int maxModelCalls = E2EDefaults.MaxModelCalls,
        TimeSpan? judgmentTimeout = null,
        IReadOnlyDictionary<string, JsonElement>? providerOptions = null,
        IReadOnlyDictionary<string, AgentOptions>? agents = null)
    {
        return new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            Model = model,
            Judge = judge,
            AgentSystem = system,
            AgentContext = context,
            MaxSteps = maxSteps,
            MaxModelCalls = maxModelCalls,
            JudgmentTimeout = judgmentTimeout ?? TimeSpan.FromSeconds(5),
            ProviderOptions = providerOptions,
            Agents = agents,
            BaseUrl = "https://billing.test",
            CacheEnabled = false,
            TestTitle = "agent options > case",
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        };
    }

    private static async Task<TException> FailsAsync<TException>(
        Func<TestContext, Task> step,
        IAgentModel model,
        Func<E2ESessionOptions, E2ESessionOptions>? configure = null)
        where TException : Exception
    {
        var result = await RunAsync(
            async ctx =>
            {
                await ctx.App.OpenAsync("/settings/billing");
                await step(ctx);
            },
            model,
            configure);
        return Assert.IsType<TException>(result.Error);
    }

    private static async Task<(Exception? Error, int ModelCalls)> RunAsync(
        Func<TestContext, Task> body,
        IAgentModel model,
        Func<E2ESessionOptions, E2ESessionOptions>? configure = null)
    {
        var options = Options(model);
        await using var session = await E2ESession.StartAsync(configure is null ? options : configure(options));
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
        return (error, session.ModelCalls);
    }

    private sealed class SlowModel : IAgentModel
    {
        public string Name => "slow";

        public async Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return ModelResponses.Done("passed", "late");
        }
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "choices": [{ "message": { "content": "ok" } }] }""", System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
