#pragma warning disable CA1848 // Use the LoggerMessage delegates
using System.Diagnostics;
using Meziantou.Extensions.Logging.Xunit.v3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory.Tests;

public sealed class HostTests
{
    private readonly ITestOutputHelper _testOutputHelper;

    public HostTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public void Test()
    {
        using var provider = new InMemoryLoggerProvider();
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.Services.AddSingleton<ILoggerProvider>(provider);
                builder.Services.AddSingleton<ILoggerProvider>(new XUnitLoggerProvider(_testOutputHelper));
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<HostTests>>();
        logger.LogInformation("Test");

        Assert.Single(provider.Logs);
    }

    [Fact]
    public void ScopeIsCapturedExactlyOnce()
    {
        using var provider = new InMemoryLoggerProvider();
        var host = new HostBuilder()
            .ConfigureLogging(builder => builder.Services.AddSingleton<ILoggerProvider>(provider))
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<HostTests>>();
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["UserId"] = 42 }))
        {
            logger.LogInformation("Test");
        }

        var log = Assert.Single(provider.Logs);
        var scope = Assert.Single(log.Scopes);
        Assert.Equal(42, Assert.IsType<Dictionary<string, object?>>(scope)["UserId"]);
    }

    [Fact]
    public void ActivityTrackingScopesAreCaptured()
    {
        using var provider = new InMemoryLoggerProvider();
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.Services.AddSingleton<ILoggerProvider>(provider);
                builder.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
            })
            .Build();

        using var activitySource = new ActivitySource("Meziantou.Extensions.Logging.InMemory.Tests");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source == activitySource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);

        var logger = host.Services.GetRequiredService<ILogger<HostTests>>();
        using var activity = activitySource.StartActivity("operation");
        Assert.NotNull(activity);

        logger.LogInformation("Test");

        var log = Assert.Single(provider.Logs);
        Assert.True(log.TryGetParameterValue("TraceId", out var traceId));
        Assert.Equal(activity.TraceId.ToString(), traceId?.ToString());
        Assert.True(log.TryGetParameterValue("SpanId", out var spanId));
        Assert.Equal(activity.SpanId.ToString(), spanId?.ToString());
    }

    [Fact]
    public void ScopesAreNotCapturedWhenTheFactoryDisablesThem()
    {
        using var provider = new InMemoryLoggerProvider();
        var host = new HostBuilder()
            .ConfigureLogging(builder =>
            {
                builder.Services.AddSingleton<ILoggerProvider>(provider);
                builder.Services.Configure<LoggerFilterOptions>(options => options.CaptureScopes = false);
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<HostTests>>();
        using (logger.BeginScope(new Dictionary<string, object?>(StringComparer.Ordinal) { ["UserId"] = 42 }))
        {
            logger.LogInformation("Test");
        }

        Assert.Empty(Assert.Single(provider.Logs).Scopes);
    }
}
