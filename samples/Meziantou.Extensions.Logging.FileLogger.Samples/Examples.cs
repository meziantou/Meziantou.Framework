using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

namespace Meziantou.Extensions.Logging.FileLogger.Samples;

internal static class Examples
{
    public static async Task AddFileToHost(string[] args)
    {
        #region FileLoggerBuilderExtensions_AddFile
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddFile(options =>
        {
            options.Directory = "logs";
            options.RollInterval = RollInterval.Daily;
            options.MaxRetainedFiles = 7;
        });
        #endregion

        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    public static void CreateProvider()
    {
        #region FileLoggerProvider_Create
        using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = "logs",
            RollInterval = RollInterval.Daily,
            MaxRetainedFiles = 7,
        });

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        #endregion

        loggerFactory.CreateLogger("Sample").LogInformation("Hello from the file logger");
    }
}
