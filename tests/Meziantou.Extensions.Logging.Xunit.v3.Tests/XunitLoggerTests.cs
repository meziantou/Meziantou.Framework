#pragma warning disable CA1848 // Use the LoggerMessage delegates
#pragma warning disable IDE1006 // Naming Styles
using System.Diagnostics;
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
    public void SetScopeProviderUpdatesExistingAndNewLoggers()
    {
        var output = new InMemoryTestOutputHelper();
        using var provider = new XUnitLoggerProvider(output, new XUnitLoggerOptions { IncludeScopes = true });
        var loggerCreatedBeforeSet = provider.CreateLogger("before");

        var scopeProvider = new LoggerExternalScopeProvider();
        ((ISupportExternalScope)provider).SetScopeProvider(scopeProvider);

        var loggerCreatedAfterSet = provider.CreateLogger("after");

        using (scopeProvider.Push("external scope"))
        {
            loggerCreatedBeforeSet.LogInformation("Test");
            loggerCreatedAfterSet.LogInformation("Test");
        }

        Assert.Equal(2, output.Logs.Count());
        Assert.All(output.Logs, log => Assert.Contains("=> external scope", log, StringComparison.Ordinal));
    }

    [Fact]
    public void LoggerFactorySetsTheScopeProvider()
    {
        var output = new InMemoryTestOutputHelper();
        using var provider = new XUnitLoggerProvider(output, new XUnitLoggerOptions { IncludeScopes = true });
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId);
            builder.AddProvider(provider);
        });

        using var activity = new Activity("Test");
        activity.Start();

        var logger = loggerFactory.CreateLogger("category");
        logger.LogInformation("Test");

        Assert.Contains(activity.TraceId.ToHexString(), output.Output, StringComparison.Ordinal);
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

    [Fact]
    public void EveryConstructionPathDefaultsToNotAppendingScopes()
    {
        var constructor = new InMemoryTestOutputHelper();
        var constructorLogger = new XUnitLogger(constructor, new LoggerExternalScopeProvider(), "Category");
        using (constructorLogger.BeginScope("TheScope"))
        {
            constructorLogger.LogInformation("message");
        }

        var factory = new InMemoryTestOutputHelper();
        var factoryLogger = XUnitLogger.CreateLogger(factory);
        using (factoryLogger.BeginScope("TheScope"))
        {
            factoryLogger.LogInformation("message");
        }

        var provider = new InMemoryTestOutputHelper();
        using var loggerProvider = new XUnitLoggerProvider(provider);
        var providerLogger = loggerProvider.CreateLogger("Category");
        using (providerLogger.BeginScope("TheScope"))
        {
            providerLogger.LogInformation("message");
        }

        Assert.Equal(["message" + Environment.NewLine], constructor.Logs, StringComparer.Ordinal);
        Assert.Equal(["message" + Environment.NewLine], factory.Logs, StringComparer.Ordinal);
        Assert.Equal(["message" + Environment.NewLine], provider.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void ScopesAreAppendedWhenIncludeScopesIsSet()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output, new XUnitLoggerOptions { IncludeScopes = true });
        using (logger.BeginScope("TheScope"))
        {
            logger.LogInformation("message");
        }

        Assert.Equal(["message\n => TheScope" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void AddXunit_RegistersASingleProviderWhenCalledTwice()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddXunit();
            builder.AddXunit();
        });

        Assert.Single(services, service => service.ServiceType == typeof(ILoggerProvider));
    }

    [Fact]
    public void AddXunit_WithATestOutputHelperRegistersOneProviderPerCall()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddXunit(new InMemoryTestOutputHelper());
            builder.AddXunit(new InMemoryTestOutputHelper());
        });

        Assert.Equal(2, services.Count(service => service.ServiceType == typeof(ILoggerProvider)));
    }
}
