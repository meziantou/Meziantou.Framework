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

    [Fact]
    public void LogLevelProperties_ReturnOnlyTheMatchingEntries()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogTrace("trace");
        logger.LogDebug("debug");
        logger.LogInformation("information");
        logger.LogWarning("warning");
        logger.LogError("error");
        logger.LogCritical("critical");

        Assert.Equal("trace", Assert.Single(logger.Logs.Traces).Message);
        Assert.Equal("debug", Assert.Single(logger.Logs.Debugs).Message);
        Assert.Equal("information", Assert.Single(logger.Logs.Informations).Message);
        Assert.Equal("warning", Assert.Single(logger.Logs.Warnings).Message);
        Assert.Equal("error", Assert.Single(logger.Logs.Errors).Message);
        Assert.Equal("critical", Assert.Single(logger.Logs.Criticals).Message);
    }

    [Fact]
    public void LogLevelProperties_AreEmptyWhenNothingMatches()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("information");

        Assert.Empty(logger.Logs.Traces);
        Assert.Empty(logger.Logs.Debugs);
        Assert.Empty(logger.Logs.Warnings);
        Assert.Empty(logger.Logs.Errors);
        Assert.Empty(logger.Logs.Criticals);
    }

    [Fact]
    public void Find_ReturnsTheFirstMatchingEntry()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("alpha");
        logger.LogInformation("beta-match");
        logger.LogInformation("gamma-match");

        var found = logger.Logs.Find(log => log.Message.EndsWith("-match", StringComparison.Ordinal));
        Assert.NotNull(found);
        Assert.Equal("beta-match", found.Message);
    }

    [Fact]
    public void Find_ReturnsNullWhenNothingMatches()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("first");

        Assert.Null(logger.Logs.Find(log => log.Message is "missing"));
    }

    [Fact]
    public void Contains_ReportsWhetherAnEntryMatches()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("first");

        Assert.True(logger.Logs.Contains(log => log.Message is "first"));
        Assert.False(logger.Logs.Contains(log => log.Message is "missing"));
    }

    [Fact]
    public void Exception_IsCapturedAndRendered()
    {
        var logger = InMemoryLogger.CreateLogger("sample");
        var exception = new InvalidOperationException("kaboom");

        logger.LogError(exception, "boom");

        var log = Assert.Single(logger.Logs.Errors);
        Assert.Same(exception, log.Exception);
        Assert.Equal("boom", log.Message);
        Assert.Contains("kaboom", log.ToString());
        Assert.Contains("kaboom", logger.Logs.ToString());
    }

    [Fact]
    public void CollectionToString_RendersEveryEntryOnItsOwnLine()
    {
        var logger = InMemoryLogger.CreateLogger("sample");

        logger.LogInformation("first");
        logger.LogWarning("second");

        var lines = logger.Logs.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(4, lines);
        Assert.StartsWith("[sample] Information: first", lines[0]);
        Assert.StartsWith("[sample] Warning: second", lines[2]);
    }

    private sealed class CustomTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}
