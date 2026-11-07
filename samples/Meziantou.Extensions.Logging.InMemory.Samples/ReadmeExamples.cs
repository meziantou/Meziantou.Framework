using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meziantou.Extensions.Logging.InMemory.Samples;

public static class ReadmeExamples
{
    public static void UseWebApplicationFactory()
    {
        #region InMemoryLogger_WebApplicationFactory
        using var loggerProvider = new InMemoryLoggerProvider();
        using var application = new WebApplicationFactory<Program>();
        using var factory = application.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(builder =>
            {
                // You can override the logging configuration if needed
                //builder.SetMinimumLevel(LogLevel.Trace);
                //builder.AddFilter(_ => true);

                builder.Services.AddSingleton<ILoggerProvider>(loggerProvider);
            });
        });
        #endregion
    }
}
