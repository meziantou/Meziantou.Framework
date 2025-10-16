#pragma warning disable CA1848 // Use the LoggerMessage delegates
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
}
