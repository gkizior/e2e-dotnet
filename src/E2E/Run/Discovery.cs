// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace E2E;

/// <summary>Finds <see cref="E2ETestAttribute"/> methods and turns them into a <see cref="Suite"/>.</summary>
public static class SuiteDiscovery
{
    public static Suite Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var suite = new Suite();
        IEnumerable<Type> types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(type => type is not null)!;
        }

        foreach (var type in types.OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            if (type is null || !type.IsClass || type.IsAbstract)
            {
                continue;
            }

            var methods = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .OrderBy(method => method.MetadataToken)
                .ToList();
            var tests = methods.Where(method => method.GetCustomAttribute<E2ETestAttribute>() is not null).ToList();
            if (tests.Count == 0)
            {
                continue;
            }

            var suiteAttribute = type.GetCustomAttribute<E2ESuiteAttribute>();
            var suiteName = suiteAttribute?.Title ?? type.Name;
            var suiteTags = suiteAttribute?.Tags ?? [];
            var beforeAll = BindStatic(methods.Where(method => method.GetCustomAttribute<E2EBeforeAllAttribute>() is not null).ToList(), type);
            var afterAll = BindStatic(methods.Where(method => method.GetCustomAttribute<E2EAfterAllAttribute>() is not null).ToList(), type);
            var beforeEach = methods.Where(method => method.GetCustomAttribute<E2EBeforeEachAttribute>() is not null).ToList();
            var afterEach = methods.Where(method => method.GetCustomAttribute<E2EAfterEachAttribute>() is not null).ToList();

            foreach (var method in tests)
            {
                var attribute = method.GetCustomAttribute<E2ETestAttribute>()!;
                var holder = new InstanceHolder();
                var group = type.FullName ?? type.Name;
                suite.Add(new SuiteTest
                {
                    Title = suiteName + " > " + attribute.Title,
                    SuiteName = suiteName,
                    GroupKey = group,
                    Body = ctx => Invoke(method, holder, type, ctx),
                    BeforeEach = beforeEach.Count == 0 ? null : ctx => InvokeAll(beforeEach, holder, type, ctx),
                    AfterEach = afterEach.Count == 0 ? null : ctx => InvokeAll(afterEach, holder, type, ctx),
                    BeforeAll = beforeAll,
                    AfterAll = afterAll,
                    Tags = suiteTags.Concat(attribute.Tags).ToArray(),
                    Options = new TestOptions
                    {
                        Skip = attribute.Skip,
                        Only = attribute.Only,
                        Retries = attribute.Retries,
                        Timeout = attribute.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(attribute.TimeoutMs) : null,
                        Tags = suiteTags.Concat(attribute.Tags).ToArray(),
                    },
                });
            }
        }

        return suite;
    }

    private static Func<Task>? BindStatic(List<MethodInfo> methods, Type type)
    {
        if (methods.Count == 0)
        {
            return null;
        }

        foreach (var method in methods)
        {
            if (!method.IsStatic)
            {
                throw new TestException("INVALID_ARGUMENT", type.Name + "." + method.Name + " must be static.");
            }

            if (method.GetParameters().Length > 0)
            {
                throw new TestException("INVALID_ARGUMENT", type.Name + "." + method.Name + " cannot take parameters.");
            }
        }

        return async () =>
        {
            foreach (var method in methods)
            {
                var result = method.Invoke(null, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }
            }
        };
    }

    private static async Task InvokeAll(List<MethodInfo> methods, InstanceHolder holder, Type type, TestContext context)
    {
        foreach (var method in methods)
        {
            await Invoke(method, holder, type, context).ConfigureAwait(false);
        }
    }

    private static async Task Invoke(MethodInfo method, InstanceHolder holder, Type type, TestContext context)
    {
        object? target = null;
        if (!method.IsStatic)
        {
            holder.Instance ??= Activator.CreateInstance(type) ?? throw new TestException("INVALID_ARGUMENT", "Could not create " + type.Name + ".");
            target = holder.Instance;
        }

        var arguments = method.GetParameters().Select(parameter => Bind(parameter.ParameterType, context, type, method)).ToArray();
        var result = method.Invoke(target, arguments);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
    }

    private static object Bind(Type parameterType, TestContext context, Type owner, MethodInfo method)
    {
        if (parameterType == typeof(TestContext))
        {
            return context;
        }

        if (parameterType == typeof(App))
        {
            return context.App;
        }

        if (parameterType == typeof(Agent))
        {
            return context.Agent;
        }

        if (parameterType == typeof(Screen))
        {
            return context.Screen;
        }

        if (parameterType == typeof(CancellationToken))
        {
            return context.CancellationToken;
        }

        throw new TestException(
            "INVALID_ARGUMENT",
            owner.Name + "." + method.Name + " cannot bind a parameter of type " + parameterType.Name + ".");
    }

    private sealed class InstanceHolder
    {
        public object? Instance { get; set; }
    }
}

/// <summary>Names a test class. The default name is the class name.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class E2ESuiteAttribute : Attribute
{
    public E2ESuiteAttribute(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    public string Title { get; }

    public string[] Tags { get; set; } = [];
}

/// <summary>Marks a public method as a test. The runner injects <see cref="App"/>, <see cref="Agent"/>, <see cref="Screen"/>, or <see cref="TestContext"/>.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2ETestAttribute : Attribute
{
    public E2ETestAttribute(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Title = title;
    }

    public string Title { get; }

    public string? Skip { get; set; }

    public bool Only { get; set; }

    public int Retries { get; set; }

    public int TimeoutMs { get; set; }

    public string[] Tags { get; set; } = [];
}

/// <summary>Runs once before the tests in the class. The method must be static and take no parameters.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EBeforeAllAttribute : Attribute;

/// <summary>Runs once after the tests in the class. The method must be static and take no parameters.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EAfterAllAttribute : Attribute;

/// <summary>Runs before each test in the class. It can take the same fixtures as a test.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EBeforeEachAttribute : Attribute;

/// <summary>Runs after each test in the class, including when the test fails.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2EAfterEachAttribute : Attribute;
