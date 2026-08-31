#pragma warning disable CA1848 // Use the LoggerMessage delegates
#pragma warning disable IDE1006 // Naming Styles
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

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
    public void ActivityTrackingScopesAreWrittenToTheOutput()
    {
        var output = new InMemoryTestOutputHelper();
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
            builder.AddXunit(output, new XUnitLoggerOptions { IncludeScopes = true });
        });

        using var serviceProvider = services.BuildServiceProvider();
        using var activity = new System.Diagnostics.Activity("test");
        activity.Start();
        serviceProvider.GetRequiredService<ILogger<XunitLoggerTests>>().LogInformation("message");

        var log = Assert.Single(output.Logs);
        Assert.Contains(activity.TraceId.ToString(), log, StringComparison.Ordinal);
        Assert.Contains(activity.SpanId.ToString(), log, StringComparison.Ordinal);
    }

    [Fact]
    public void ScopesStartedByTheCallerAreStillWrittenToTheOutput()
    {
        var output = new InMemoryTestOutputHelper();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddXunit(output, new XUnitLoggerOptions { IncludeScopes = true }));

        using var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<XunitLoggerTests>>();
        using (logger.BeginScope("TheScope"))
        {
            logger.LogInformation("message");
        }

        Assert.Equal(["message\n => TheScope" + Environment.NewLine], output.Logs, StringComparer.Ordinal);
    }
}
