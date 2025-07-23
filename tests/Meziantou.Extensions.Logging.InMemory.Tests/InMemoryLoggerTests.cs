#pragma warning disable CA1848 // Use the LoggerMessage delegates
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meziantou.Extensions.Logging.InMemory.Tests;

public sealed partial class InMemoryLoggerTests
{
    private static readonly Action<ILogger, int, Exception> SampleMessage = LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "Sample Event Id"), "Test {Number}");

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
        Assert.Equivalent(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } }, log.Scopes);
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
        Assert.Equivalent(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } }, log.Scopes);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Value is {value}")]
    private static partial void Log(ILogger logger, int value);

    [Fact]
    public void LogManyMessages()
    {
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
        Parallel.For(0, 100_000, i => Log(logger, 1));

        Assert.Equivalent(100_000, provider.Logs.Count());
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
