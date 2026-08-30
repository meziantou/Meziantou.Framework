#pragma warning disable CA1848 // Use the LoggerMessage delegates
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory.Tests;

public sealed partial class InMemoryLoggerTests
{
    private static readonly Action<ILogger, int, Exception?> SampleMessage = LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "Sample Event Id"), "Test {Number}");

    [Fact]
    public void CreateLogger()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("Test");

        var log = logger.Logs.Informations.Single();
        Assert.Equal("Test", log.Message);
        Assert.Equivalent(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") }, log.State);
        Assert.Empty(log.Scopes);
        Assert.Equal("[sample] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]", log.ToString());
    }

    [Fact]
    public void CreateTypedLogger()
    {
        var logger = InMemoryLogger.CreateLogger<InMemoryLoggerTests>();

        logger.LogInformation("Test");

        var log = logger.Logs.Informations.Single();
        Assert.Equal("Test", log.Message);
        Assert.Equivalent(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") }, log.State);
        Assert.Empty(log.Scopes);
        Assert.Equal("[Meziantou.Extensions.Logging.InMemory.Tests.InMemoryLoggerTests] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]", log.ToString());
    }

    [Fact]
    public void ToString_DoesNotThrow_WhenAParameterValueIsNotJsonSerializable()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("handled by {Handler}", typeof(string));

        var log = logger.Logs.Informations.Single();
        var text = log.ToString();
        Assert.Contains("handled by System.String", text);
        Assert.Contains("handled by System.String", logger.Logs.ToString());
    }

    [Fact]
    public void ToString_DoesNotThrow_WhenAScopeIsNotJsonSerializable()
    {
        using var provider = new InMemoryLoggerProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("my_category");

        using (logger.BeginScope(new { Callback = (Action)(() => { }) }))
        {
            logger.LogInformation("Test");
        }

        var log = provider.Logs.Informations.Single();
        Assert.StartsWith("[my_category] Information: Test", log.ToString());
    }

    [Fact]
    public void ToString_DoesNotThrow_WhenTheStateContainsAReferenceCycle()
    {
        var logger = InMemoryLogger.CreateLogger("sample");
        var node = new SelfReferencingNode();
        node.Self = node;

        logger.LogInformation("node {Node}", node);

        var log = logger.Logs.Informations.Single();
        Assert.StartsWith("[sample] Information: node ", log.ToString());
    }

    private sealed class SelfReferencingNode
    {
        public SelfReferencingNode? Self { get; set; }
    }

    [Fact]
    public void UsingDependencyInjection()
    {
        using var inMemoryLoggerProvider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder =>
        {
            builder.AddProvider(inMemoryLoggerProvider);
            builder.SetMinimumLevel(LogLevel.Trace);
        });

        var serviceProvider = serviceCollection.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<InMemoryLoggerTests>>();

        logger.LogInformation("Test");

        var log = inMemoryLoggerProvider.Logs.Informations.Single();
        Assert.Equal("Test", log.Message);
        Assert.Equivalent(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") }, log.State);
        Assert.Empty(log.Scopes);
        Assert.Equal("[Meziantou.Extensions.Logging.InMemory.Tests.InMemoryLoggerTests] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]", log.ToString());
    }

    [Fact]
    public void WithoutScope()
    {
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
        logger.LogInformation("Test");

        var log = provider.Logs.Informations.Single();
        Assert.Equal("Test", log.Message);
        Assert.Equivalent(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") }, log.State);
        Assert.Empty(log.Scopes);
        Assert.Equal("[my_category] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]", log.ToString());
    }

    [Fact]
    public void WithScope()
    {
        using var provider = new InMemoryLoggerProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("my_category");
        using (logger.BeginScope(new { Name = "test" }))
        using (logger.BeginScope(new { Age = 52, Name = "John" }))
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            logger.LogInformation("Test {Number}", 1);
#pragma warning restore CA1848
        }

        var log = provider.Logs.Informations.Single();
        Assert.Equal("Test 1", log.Message);
        Assert.Equivalent(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") }, log.State);
        Assert.Equivalent(new object[] { new { Name = "test" }, new { Age = 52, Name = "John" } }, log.Scopes);
        Assert.Equal("[my_category] Information: Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}", log.ToString());
    }

    [Fact]
    public void WithScope_LoggerMessage()
    {
        using var provider = new InMemoryLoggerProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("my_category");
        using (logger.BeginScope(new { Name = "test" }))
        using (logger.BeginScope(new { Age = 52, Name = "John" }))
        {
            SampleMessage(logger, 1, null);
        }

        var log = provider.Logs.Informations.Single();
        Assert.Equal("Test 1", log.Message);
        Assert.Equivalent(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") }, log.State);
        Assert.Equivalent(new object[] { new { Name = "test" }, new { Age = 52, Name = "John" } }, log.Scopes);
        Assert.Equal("[my_category] Information (1 Sample Event Id): Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}", log.ToString());
        Assert.True(log.TryGetParameterValue("{OriginalFormat}", out var format));
        Assert.Equal("Test {Number}", format);
        Assert.True(log.TryGetParameterValue("Name", out var name));
        Assert.Equal("test", name);
        Assert.True(log.TryGetParameterValue("Number", out var number));
        Assert.Equal(1, number);
        Assert.True(log.TryGetParameterValue("Age", out var age));
        Assert.Equal(52, age);
        Assert.Equal(["test", "John"], log.GetAllParameterValues("Name"));
    }

    [Fact]
    public void TryGetParameterValue_DictionaryScope()
    {
        using var provider = new InMemoryLoggerProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("my_category");
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Age"] = 52, ["Name"] = "John" }))
        {
            logger.LogInformation("Test {Number}", 1);
        }

        var log = provider.Logs.Informations.Single();
        Assert.True(log.TryGetParameterValue("Number", out var number));
        Assert.Equal(1, number);
        Assert.True(log.TryGetParameterValue("Name", out var name));
        Assert.Equal("John", name);
        Assert.True(log.TryGetParameterValue("Age", out var age));
        Assert.Equal(52, age);
        Assert.False(log.TryGetParameterValue("Unknown", out var unknown));
        Assert.Null(unknown);
    }

    [Fact]
    public void WithScope_Parallel()
    {
        using var provider = new InMemoryLoggerProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("my_category");
        Parallel.For(0, 10_000, i =>
        {
            using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Index"] = i }))
            {
                logger.LogInformation("Test {Number}", i);
            }
        });

        Assert.HasCount(10_000, provider.Logs);
        foreach (var log in provider.Logs)
        {
            var scope = Assert.Single(log.Scopes);
            Assert.True(log.TryGetParameterValue("Number", out var number));
            Assert.Equal(number, Assert.IsType<Dictionary<string, object?>>(scope)["Index"]);
        }
    }

    [Fact]
    public void WithDeepScopeStack_ThenShallowScope()
    {
        using var provider = new InMemoryLoggerProvider(new LoggerExternalScopeProvider());
        var logger = provider.CreateLogger("my_category");

        // Deep enough to grow the scope buffer past the capacity at which it is dropped instead of reused
        var scopes = new List<IDisposable?>();
        try
        {
            for (var i = 0; i < 200; i++)
            {
                scopes.Add(logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Index"] = i }));
            }

            logger.LogInformation("Deep");
        }
        finally
        {
            for (var i = scopes.Count - 1; i >= 0; i--)
            {
                scopes[i]?.Dispose();
            }
        }

        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["Index"] = 42 }))
        {
            logger.LogInformation("Shallow");
        }

        var deep = provider.Logs.Informations.Single(log => log.Message is "Deep");
        Assert.HasCount(200, deep.Scopes);
        Assert.Equal(Enumerable.Range(0, 200).Cast<object?>(), deep.Scopes.Select(scope => Assert.IsType<Dictionary<string, object?>>(scope)["Index"]));

        var shallow = provider.Logs.Informations.Single(log => log.Message is "Shallow");
        var shallowScope = Assert.Single(shallow.Scopes);
        Assert.Equal(42, Assert.IsType<Dictionary<string, object?>>(shallowScope)["Index"]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Value is {value}")]
    private static partial void Log(ILogger logger, int value);

    [Fact]
    public void LogManyMessages()
    {
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
        Parallel.For(0, 100_000, i => Log(logger, 1));

        Assert.HasCount(100_000, provider.Logs);
    }

    [Fact]
    public void WithTimeProvider()
    {
        using var provider = new InMemoryLoggerProvider(new CustomTimeProvider());
        var logger = provider.CreateLogger("my_category");
        Log(logger, 1);

        var log = provider.Logs.Informations.Single();
        Assert.Equal(new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), log.CreatedAt);
    }

    private sealed class CustomTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
