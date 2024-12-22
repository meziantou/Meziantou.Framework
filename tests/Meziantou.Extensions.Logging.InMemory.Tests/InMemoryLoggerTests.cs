using Microsoft.Extensions.Logging;
using Xunit;

namespace Meziantou.Extensions.Logging.InMemory.Tests;

public sealed partial class InMemoryLoggerTests
{
    private static readonly Action<ILogger, int, Exception> SampleMessage = LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "Sample Event Id"), "Test {Number}");

    [Fact]
    public void WithoutScope()
    {
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
#pragma warning disable CA1848 // Use the LoggerMessage delegates
        logger.LogInformation("Test");
#pragma warning restore CA1848

        var log = provider.Logs.Informations.Single();
        Assert.Equal("Test", log.Message);
        Assert.Collection(log.State, item => Assert.Equal(new KeyValuePair<string, object>("{OriginalFormat}", "Test"), item));
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
        Assert.Collection(log.State,
            item => Assert.Equal(new KeyValuePair<string, object>("Number", 1), item),
            item => Assert.Equal(new KeyValuePair<string, object>("{OriginalFormat}", "Test {Number}"), item));
        Assert.Collection(log.Scopes,
            item => Assert.Equal(new { Age = 52, Name = "John" }, item),
            item => Assert.Equal(new { Name = "test" }, item));

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
        Assert.Collection(log.State,
            item => Assert.Equal(new KeyValuePair<string, object>("Number", 1), item),
            item => Assert.Equal(new KeyValuePair<string, object>("{OriginalFormat}", "Test {Number}"), item));
        Assert.Collection(log.Scopes,
            item => Assert.Equal(new { Age = 52, Name = "John" }, item),
            item => Assert.Equal(new { Name = "test" }, item));

        Assert.Equal("[my_category] Information (1 Sample Event Id): Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}", log.ToString());

        Assert.True(log.TryGetParameterValue("{OriginalFormat}", out var format));
        Assert.Equal("Test {Number}", format);

        Assert.True(log.TryGetParameterValue("Name", out var name));
        Assert.Equal("test", name);

        Assert.True(log.TryGetParameterValue("Number", out var number));
        Assert.Equal(1, number);

        Assert.True(log.TryGetParameterValue("Age", out var age));
        Assert.Equal(52, age);

        Assert.Equal(new object[] { "test", "John" }, log.GetAllParameterValues("Name").ToArray());
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Value is {value}")]
    private static partial void Log(ILogger logger, int value);

    [Fact]
    public void LogManyMessages()
    {
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
        Parallel.For(0, 100_000, i => Log(logger, 1));

        Assert.Equal(100_000, provider.Logs.Count);
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void WithTimeProvider()
    {
        using var provider = new InMemoryLoggerProvider(new CustomTimeProvider());
        var logger = provider.CreateLogger("my_category");
        Log(logger, 1);

        var log = provider.Logs.Informations.Single();
        Assert.Equal(new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero), log.CreatedAt);
    }

    private sealed class CustomTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
#endif
}
