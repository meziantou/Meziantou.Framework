#pragma warning disable CA1848 // Use the LoggerMessage delegates
using Meziantou.Extensions.Logging.Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

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
