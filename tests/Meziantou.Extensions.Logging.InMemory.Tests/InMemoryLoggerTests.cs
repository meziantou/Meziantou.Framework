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
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
#pragma warning disable CA1848 // Use the LoggerMessage delegates
        logger.LogInformation("Test");
#pragma warning restore CA1848

        var log = provider.Logs.Informations.Single();
        log.Message.Should().Be("Test");
        log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("{OriginalFormat}", "Test") });
        log.Scopes.Should().BeEquivalentTo(Array.Empty<object>());

        log.ToString().Should().Be("[my_category] Information: Test\n  => [{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test\"}]");
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
        log.Message.Should().Be("Test 1");
        log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") });
        log.Scopes.Should().BeEquivalentTo(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } });

        log.ToString().Should().Be("[my_category] Information: Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}");
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
        log.Message.Should().Be("Test 1");
        log.State.Should().BeEquivalentTo(new[] { KeyValuePair.Create<string, object>("Number", 1), KeyValuePair.Create<string, object>("{OriginalFormat}", "Test {Number}") });
        log.Scopes.Should().BeEquivalentTo(new object[] { new { Age = 52, Name = "John" }, new { Name = "test" } });

        log.ToString().Should().Be("[my_category] Information (1 Sample Event Id): Test 1\n  => [{\"Key\":\"Number\",\"Value\":1},{\"Key\":\"{OriginalFormat}\",\"Value\":\"Test {Number}\"}]\n  => {\"Name\":\"test\"}\n  => {\"Age\":52,\"Name\":\"John\"}");

        log.TryGetParameterValue("{OriginalFormat}", out var format).Should().BeTrue();
        format.Should().Be("Test {Number}");

        log.TryGetParameterValue("Name", out var name).Should().BeTrue();
        name.Should().Be("test");

        log.TryGetParameterValue("Number", out var number).Should().BeTrue();
        number.Should().Be(1);
        
        log.TryGetParameterValue("Age", out var age).Should().BeTrue();
        age.Should().Be(52);

        log.GetAllParameterValues("Name").Should().Equal(["test", "John"]);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Value is {value}")]
    private static partial void Log(ILogger logger, int value);

    [Fact]
    public void LogManyMessages()
    {
        using var provider = new InMemoryLoggerProvider(NullExternalScopeProvider.Instance);
        var logger = provider.CreateLogger("my_category");
        Parallel.For(0, 100_000, i => Log(logger, 1));

        provider.Logs.Should().HaveCount(100_000);
    }
}
