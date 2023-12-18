using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework;

public sealed class HttpClientMock : IAsyncDisposable
{
    private bool _running;

    public HttpClientMock()
        : this(loggerProvider: null)
    {
    }

    public HttpClientMock(ILoggerProvider? loggerProvider)
        : this(loggerProvider is null ? null : builder => builder.AddProvider(loggerProvider))
    {
    }

    public HttpClientMock(ILogger? logger)
        : this(logger is null ? null : builder => builder.AddProvider(new SingletonLogger(logger)))
    {
    }

    public HttpClientMock(Action<ILoggingBuilder>? configureLogging)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
        builder.Logging.ClearProviders();
        configureLogging?.Invoke(builder.Logging);
        builder.WebHost.UseTestServer();
        Application = builder.Build();
    }

    public WebApplication Application { get; }

    public HttpClient CreateHttpClient()
    {
        StartServer();
        return Application.GetTestClient();
    }

    public HttpMessageHandler CreateHttpMessageHandler()
    {
        StartServer();
        return Application.GetTestServer().CreateHandler();
    }

    private void StartServer()
    {
        if (!_running)
        {
            _running = true;
            _ = Application.RunAsync();
        }
    }

    public ValueTask DisposeAsync() => Application.DisposeAsync();

    private sealed class SingletonLogger(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
}
