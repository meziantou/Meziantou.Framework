#pragma warning disable CA1848 // Use the LoggerMessage delegates
#pragma warning disable IDE1006 // Naming Styles
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Meziantou.Extensions.Logging.Xunit.v3.Tests;

public sealed class XunitLoggerTests
{
    [Fact]
    public void ReadmeExample_CreateLoggerForTheCurrentTest()
    {
        #region XUnitLogger_CreateLogger
        ILogger logger = XUnitLogger.CreateLogger();
        ILogger<MyType> typedLogger = XUnitLogger.CreateLogger<MyType>();
        #endregion

        logger.LogInformation("Written by the untyped logger");
        typedLogger.LogInformation("Written by the typed logger");

        var output = TestContext.Current.TestOutputHelper?.Output;
        Assert.Contains("Written by the untyped logger", output, StringComparison.Ordinal);
        Assert.Contains("Written by the typed logger", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadmeExample_CreateLoggerWithTestOutputHelper()
    {
        var testOutputHelper = new InMemoryTestOutputHelper();

        #region XUnitLogger_CreateLoggerWithTestOutputHelper
        ILogger logger = XUnitLogger.CreateLogger(testOutputHelper);
        ILogger<MyType> typedLogger = XUnitLogger.CreateLogger<MyType>(testOutputHelper);
        #endregion

        logger.LogInformation("Untyped");
        typedLogger.LogInformation("Typed");

        Assert.Equal(["Untyped" + Environment.NewLine, "Typed" + Environment.NewLine], testOutputHelper.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void ReadmeExample_Options()
    {
        var testOutputHelper = new InMemoryTestOutputHelper();

        #region XUnitLogger_Options
        ILogger<MyType> logger = XUnitLogger.CreateLogger<MyType>(testOutputHelper, new XUnitLoggerOptions
        {
            IncludeLogLevel = true,
            IncludeCategory = true,
            IncludeScopes = true,
            TimestampFormat = "HH:mm:ss.fff",
            UseUtcTimestamp = true,
        });

        using (logger.BeginScope("TheScope"))
        {
            logger.LogWarning("Something happened");
        }

        // 12:20:41.812 warn [MyNamespace.MyType] Something happened
        //  => TheScope
        #endregion

        var log = Assert.Single(testOutputHelper.Logs);
        Assert.Matches(@"^[0-9]{2}:[0-9]{2}:[0-9]{2}\.[0-9]{3} warn \[Meziantou\.Extensions\.Logging\.Xunit\.v3\.Tests\.XunitLoggerTests\.MyType\] Something happened\r?\n => TheScope\r?\n$", log);
    }

    [Fact]
    public void ReadmeExample_AddXunit()
    {
        #region XUnitLogger_AddXunit
        using var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.AddXunit();
            })
            .Build();
        #endregion

        var logger = host.Services.GetRequiredService<ILogger<MyType>>();
        logger.LogInformation("Written through the host");

        Assert.Contains("Written through the host", TestContext.Current.TestOutputHelper?.Output, StringComparison.Ordinal);
    }

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
    public void ScopesAreWrittenBetweenTheMessageAndTheException()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output, new XUnitLoggerOptions { IncludeScopes = true });
        var exception = new InvalidOperationException("boom");
        using (logger.BeginScope("TheScope"))
        {
            logger.LogError(exception, "the message");
        }

        var expected = "the message" + Environment.NewLine + " => TheScope" + Environment.NewLine + exception + Environment.NewLine;
        Assert.Equal([expected], output.Logs, StringComparer.Ordinal);
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
    public void TheProviderAliasCanBeUsedToConfigureFilters()
    {
        var output = new InMemoryTestOutputHelper();
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.AddXunit(output);
                builder.AddConfiguration(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["XUnit:LogLevel:Default"] = "Error" })
                    .Build());
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<XunitLoggerTests>>();
        logger.LogInformation("filtered out");
        logger.LogError("kept");

        Assert.Equal(["kept" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
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

        Assert.Equal(["message" + Environment.NewLine + exception + Environment.NewLine], output.Logs, StringComparer.Ordinal);
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

        var expected = DateTimeOffset.UtcNow.ToLocalTime().ToString("yyyy", CultureInfo.CurrentCulture) + " warn [TheCategory] message" + Environment.NewLine + " => TheScope" + Environment.NewLine;
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

        Assert.Equal(["message" + Environment.NewLine + " => TheScope" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
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

    [Fact]
    public void AnOutputHelperWhoseTestEndedDoesNotFailTheLogCall()
    {
        var logger = XUnitLogger.CreateLogger(new ThrowingTestOutputHelper(new InvalidOperationException("There is no currently active test.")));

        logger.LogInformation("message");
    }

    [Fact]
    public void AFailingOutputHelperSurfacesItsError()
    {
        var logger = XUnitLogger.CreateLogger(new ThrowingTestOutputHelper(new NotSupportedException("broken helper")));

        var exception = Assert.Throws<NotSupportedException>(() => logger.LogInformation("message"));
        Assert.Equal("broken helper", exception.Message, StringComparer.Ordinal);
    }

    private sealed class MyType;

    private sealed class ThrowingTestOutputHelper : ITestOutputHelper
    {
        private readonly Exception _exception;

        public ThrowingTestOutputHelper(Exception exception) => _exception = exception;

        public string Output => throw _exception;

        public void Write(string message) => throw _exception;

        public void Write(string format, params object[] args) => throw _exception;

        public void WriteLine(string message) => throw _exception;

        public void WriteLine(string format, params object[] args) => throw _exception;
    }
}
