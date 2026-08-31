#pragma warning disable CA1848 // Use the LoggerMessage delegates
#pragma warning disable IDE1006 // Naming Styles
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Meziantou.Extensions.Logging.Xunit.v3.Tests;

public sealed class XunitLoggerTests
{
    [Fact]
    public void XUnitLoggerProviderTest()
    {
        var output = new InMemoryTestOutputHelper();
        using var provider = new XUnitLoggerProvider(output);
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.Services.AddSingleton<ILoggerProvider>(provider);

            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<XunitLoggerTests>>();
        logger.LogInformation("Test");
        logger.LogInformation("Test {Sample}", "value");

        Assert.Equal(["Test" + Environment.NewLine, "Test value" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void XUnitLoggerLoggingBuilderTest()
    {
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.AddXunit();

            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<XunitLoggerTests>>();
        logger.LogInformation("Test");
        logger.LogInformation("Test {Sample}", "value");

        // Nothing to assert, it will throw an exception if something goes wrong
    }

    [Fact]
    public void IncludeLogLevelPrefixesTheLevel()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output, new XUnitLoggerOptions { IncludeLogLevel = true });
        logger.LogWarning("message");

        Assert.Equal(["warn message" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "trce")]
    [InlineData(LogLevel.Debug, "dbug")]
    [InlineData(LogLevel.Information, "info")]
    [InlineData(LogLevel.Warning, "warn")]
    [InlineData(LogLevel.Error, "fail")]
    [InlineData(LogLevel.Critical, "crit")]
    public void EveryLogLevelHasItsOwnPrefix(LogLevel logLevel, string expected)
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output, new XUnitLoggerOptions { IncludeLogLevel = true });
        logger.Log(logLevel, "message");

        Assert.Equal([expected + " message" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void IncludeCategoryPrefixesTheCategory()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = new XUnitLogger(output, new LoggerExternalScopeProvider(), "TheCategory", new XUnitLoggerOptions { IncludeCategory = true });
        logger.LogInformation("message");

        Assert.Equal(["[TheCategory] message" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void TheGenericLoggerUsesTheTypeNameAsCategory()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger<XunitLoggerTests>(output, new XUnitLoggerOptions { IncludeCategory = true });
        logger.LogInformation("message");

        Assert.Equal(["[" + typeof(XunitLoggerTests).FullName + "] message" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void TheExceptionIsAppendedAfterTheMessage()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output);
        var exception = new InvalidOperationException("boom");
        logger.LogError(exception, "message");

        Assert.Equal(["message\n" + exception + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void TimestampFormatIsWrittenBeforeTheMessage()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output, new XUnitLoggerOptions { TimestampFormat = "yyyy" });
        logger.LogInformation("message");

        Assert.Equal([DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy", CultureInfo.CurrentCulture) + " message" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void UseUtcTimestampSelectsTheUtcOffset()
    {
        var utc = new InMemoryTestOutputHelper();
        XUnitLogger.CreateLogger(utc, new XUnitLoggerOptions { TimestampFormat = "zzz", UseUtcTimestamp = true }).LogInformation("message");

        var local = new InMemoryTestOutputHelper();
        XUnitLogger.CreateLogger(local, new XUnitLoggerOptions { TimestampFormat = "zzz", UseUtcTimestamp = false }).LogInformation("message");

        Assert.Equal([DateTimeOffset.UtcNow.ToString("zzz", CultureInfo.CurrentCulture) + " message" + Environment.NewLine], utc.Logs, StringComparer.Ordinal);
        Assert.Equal([DateTimeOffset.UtcNow.ToLocalTime().ToString("zzz", CultureInfo.CurrentCulture) + " message" + Environment.NewLine], local.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void OptionsCombineInAStableOrder()
    {
        var output = new InMemoryTestOutputHelper();
        var options = new XUnitLoggerOptions
        {
            TimestampFormat = "yyyy",
            IncludeLogLevel = true,
            IncludeCategory = true,
            IncludeScopes = true,
        };
        var logger = new XUnitLogger(output, new LoggerExternalScopeProvider(), "TheCategory", options);
        using (logger.BeginScope("TheScope"))
        {
            logger.LogWarning("message");
        }

        var expected = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy", CultureInfo.CurrentCulture) + " warn [TheCategory] message\n => TheScope" + Environment.NewLine;
        Assert.Equal([expected], output.Logs, StringComparer.Ordinal);
    }
}
