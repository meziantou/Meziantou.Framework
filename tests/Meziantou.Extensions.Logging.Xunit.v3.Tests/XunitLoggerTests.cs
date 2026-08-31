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
    public void LogLevelNoneIsIgnoredInsteadOfThrowing()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output, new XUnitLoggerOptions { IncludeLogLevel = true });
        logger.Log(LogLevel.None, "message");

        Assert.Empty(output.Logs);
    }

    [Fact]
    public void LogLevelNoneIsIgnoredWhenTheLevelIsNotWritten()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = XUnitLogger.CreateLogger(output);
        logger.Log(LogLevel.None, "message");

        Assert.Empty(output.Logs);
    }

    [Fact]
    public void ADerivedLoggerCanReplaceTheFormatting()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = new PrefixingLogger(output, new XUnitLoggerOptions { IncludeLogLevel = true });
        logger.LogWarning("the message");

        Assert.Equal(["warn [TheCategory] the message (IncludeLogLevel=True)" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    [Fact]
    public void ADerivedLoggerCanDelegateToTheDefaultFormatting()
    {
        var output = new InMemoryTestOutputHelper();
        var logger = new DelegatingLogger(output);
        logger.LogInformation("kept");
        logger.LogInformation("dropped");

        Assert.Equal(["kept" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }

    private sealed class PrefixingLogger : XUnitLogger
    {
        public PrefixingLogger(ITestOutputHelper testOutputHelper, XUnitLoggerOptions options)
            : base(testOutputHelper, new LoggerExternalScopeProvider(), "TheCategory", options)
        {
        }

        public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"{GetLogLevelString(logLevel)} [{CategoryName}] {formatter(state, exception)} (IncludeLogLevel={Options.IncludeLogLevel})";
            TestOutputHelper?.WriteLine(message);
        }
    }

    private sealed class DelegatingLogger : XUnitLogger
    {
        public DelegatingLogger(ITestOutputHelper testOutputHelper)
            : base(testOutputHelper, new LoggerExternalScopeProvider(), "TheCategory", options: null)
        {
        }

        public override void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception) is "dropped")
                return;

            base.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
