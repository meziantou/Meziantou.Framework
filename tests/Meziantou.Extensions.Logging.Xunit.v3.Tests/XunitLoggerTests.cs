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
}
