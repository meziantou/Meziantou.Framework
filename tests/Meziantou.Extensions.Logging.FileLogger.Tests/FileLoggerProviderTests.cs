using Meziantou.Framework;
using Microsoft.Extensions.Logging;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

namespace Meziantou.Extensions.Logging.FileLogger.Tests;

public sealed class FileLoggerProviderTests
{
    [Fact]
    public async Task WritesLogToFile()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var provider = new FileLoggerProvider(tempDirectory);

        try
        {
            var logger = provider.CreateLogger("Test.Namespace.Category");

            logger.LogInformation("Hello from test");
            provider.Dispose();

            var logFilePath = provider.LogFilePath;
            Assert.True(File.Exists(logFilePath));

            var content = await File.ReadAllTextAsync(logFilePath);
            Assert.Contains("Hello from test", content);
            Assert.Contains("[INFO]", content);
            Assert.Contains("[Category]", content);
        }
        finally
        {
            provider.Dispose();
        }
    }
}
