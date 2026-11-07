using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

namespace Meziantou.Extensions.Logging.FileLogger.Samples;

internal static class ReadmeExamples
{
    public static void ConfigureWithOptions(string[] args)
    {
        #region FileLogger_AddFileWithOptions
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddFile(options =>
        {
            options.Directory = "logs";
            options.RollInterval = RollInterval.Daily;
            options.MaxRetainedFiles = 7;
            options.Compression = LogFileCompression.GZip;
            options.CompressionMode = LogFileCompressionMode.OnRoll;
            options.IncludeScopes = true;
        });
        #endregion

        Run(builder);
    }

    public static void ConfigureFromConfiguration(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        #region FileLogger_AddFileFromConfiguration
        builder.Logging.AddFile();
        #endregion

        Run(builder);
    }

    public static void UseJsonFormatter(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        #region FileLogger_JsonFormatter
        builder.Logging.AddFile("logs", options => options.FormatterName = FileFormatterNames.Json);
        #endregion

        Run(builder);
    }

    private static void Run(HostApplicationBuilder builder)
    {
        using var host = builder.Build();
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Sample");
        logger.LogInformation("Hello {Name}", "world");
    }
}
