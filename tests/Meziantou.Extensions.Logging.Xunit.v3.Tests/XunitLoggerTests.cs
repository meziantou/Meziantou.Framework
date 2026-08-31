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
