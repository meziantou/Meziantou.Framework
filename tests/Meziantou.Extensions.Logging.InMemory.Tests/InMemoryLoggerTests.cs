using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meziantou.Extensions.Logging.InMemory.Tests;

public sealed partial class InMemoryLoggerTests
{
    private static readonly Action<ILogger, int, Exception> SampleMessage = LoggerMessage.Define<int>(LogLevel.Information, new EventId(1, "Sample Event Id"), "Test {Number}");

    [Fact]
    public void WithoutScope()
    {
        var logger = new InMemoryLogger("my_category", NullExternalScopeProvider.Instance);
#pragma warning disable CA1848 // Use the LoggerMessage delegates
        logger.LogInformation("Test");
#pragma warning restore CA1848

        var log = logger.Logs.Informations.Single();
        log.Message.Should().Be("Test");
        log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") });
        log.Scopes.Should().BeEquivalentTo(Array.Empty<object>());

        log.ToString().Should().Be("[my_category] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]");
    }

    [Fact]
    public void WithScope()
    {
        var logger = new InMemoryLogger("my_category", new LoggerExternalScopeProvider());
        using (logger.BeginScope(new { Name = "test" }))
        using (logger.BeginScope(new { Age = 52, Name = "John" }))
        {
#pragma warning disable CA1848 // Use the LoggerMessage delegates
            logger.LogInformation("Test {Number}", 1);
#pragma warning restore CA1848
        }

        var log = logger.Logs.Informations.Single();
        log.Message.Should().Be("Test 1");
        log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") });
        log.Scopes.Should().BeEquivalentTo(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } });

        log.ToString().Should().Be("[my_category] Information: Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}");
    }

    [Fact]
    public void WithScope_LoggerMessage()
    {
        var logger = new InMemoryLogger("my_category", new LoggerExternalScopeProvider());
        using (logger.BeginScope(new { Name = "test" }))
        using (logger.BeginScope(new { Age = 52, Name = "John" }))
        {
            SampleMessage(logger, 1, null);
        }

        var log = logger.Logs.Informations.Single();
        log.Message.Should().Be("Test 1");
        log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") });
        log.Scopes.Should().BeEquivalentTo(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } });

        log.ToString().Should().Be("[my_category] Information (1 Sample Event Id): Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Value is {value}")]
    private static partial void Log(ILogger logger, int value);

    [Fact]
    public void LogManyMessages()
    {
        var logger = new InMemoryLogger("my_category", NullExternalScopeProvider.Instance);
        Parallel.For(0, 100_000, i => Log(logger, 1));

        logger.Logs.Should().HaveCount(100_000);
    }
}
