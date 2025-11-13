using Meziantou.Framework.Internals;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Meziantou.Framework;

/// <summary>A mock HTTP server for testing HTTP clients. Use this to simulate HTTP endpoints without requiring a real server.</summary>
/// <example>
/// Create a mock server with a simple endpoint:
/// <code>
/// using var mock = new HttpClientMock();
/// mock.MapGet("/api/users", () => Results.Ok(new { name = "John" }));
/// using var httpClient = mock.CreateHttpClient();
/// var response = await httpClient.GetAsync("/api/users");
/// </code>
/// </example>
public sealed partial class HttpClientMock : IAsyncDisposable
{
    private bool _running;

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class.</summary>
    public HttpClientMock()
        : this(loggerProvider: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class with a logger provider.</summary>
    /// <param name="loggerProvider">The logger provider to use for logging HTTP requests and responses.</param>
    public HttpClientMock(ILoggerProvider? loggerProvider)
        : this(loggerProvider is null ? null : builder => builder.AddProvider(loggerProvider))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class with a logger.</summary>
    /// <param name="logger">The logger to use for logging HTTP requests and responses.</param>
    public HttpClientMock(ILogger? logger)
        : this(logger is null ? null : builder => builder.AddProvider(new SingletonLogger(logger)))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class with custom logging configuration.</summary>
    /// <param name="configureLogging">An action to configure logging.</param>
    public HttpClientMock(Action<ILoggingBuilder>? configureLogging)
        : this(configureLogging, configureServices: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class with a logger and custom services.</summary>
    /// <param name="logger">The logger to use for logging HTTP requests and responses.</param>
    /// <param name="configureServices">An action to configure services.</param>
    public HttpClientMock(ILogger? logger, Action<IServiceCollection>? configureServices)
        : this(logger is null ? null : builder => builder.AddProvider(new SingletonLogger(logger)), configureServices)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class with a logger provider and custom services.</summary>
    /// <param name="loggerProvider">The logger provider to use for logging HTTP requests and responses.</param>
    /// <param name="configureServices">An action to configure services.</param>
    public HttpClientMock(ILoggerProvider? loggerProvider, Action<IServiceCollection>? configureServices)
        : this(loggerProvider is null ? null : builder => builder.AddProvider(loggerProvider), configureServices)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="HttpClientMock"/> class with custom logging and service configuration.</summary>
    /// <param name="configureLogging">An action to configure logging.</param>
    /// <param name="configureServices">An action to configure services.</param>
    public HttpClientMock(Action<ILoggingBuilder>? configureLogging, Action<IServiceCollection>? configureServices)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<MatcherPolicy, SchemeMatcherPolicy>();
        builder.Services.AddSingleton<MatcherPolicy, QueryStringMatcherPolicy>();
        builder.Services.AddSingleton<RequestCounter>();
        builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        builder.Logging.ClearProviders();
        configureLogging?.Invoke(builder.Logging);
        configureServices?.Invoke(builder.Services);

        builder.WebHost.UseTestServer();
        Application = builder.Build();
        Application.Use(async (context, next) =>
        {
            await next().ConfigureAwait(false);
            var counter = context.RequestServices.GetRequiredService<RequestCounter>();
            counter.IncrementTotal();
            counter.IncrementEndpoint(context);
        });
    }

    /// <summary>Gets the underlying <see cref="WebApplication"/> instance.</summary>
    /// <summary>Gets the underlying <see cref="WebApplication"/> instance.</summary>
    public WebApplication Application { get; }

    /// <summary>Creates an <see cref="HttpClient"/> that sends requests to the mock server.</summary>
    /// <returns>An <see cref="HttpClient"/> configured to send requests to the mock server.</returns>
    public HttpClient CreateHttpClient()
    {
        StartServer();
        return Application.GetTestClient();
    }

    /// <summary>Creates an <see cref="HttpMessageHandler"/> that sends requests to the mock server.</summary>
    /// <returns>An <see cref="HttpMessageHandler"/> configured to send requests to the mock server.</returns>
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

    /// <summary>Disposes the mock server asynchronously.</summary>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync() => Application.DisposeAsync();

    /// <summary>Configures the mock to forward unmatched requests to the actual upstream server.</summary>
    public void ForwardUnknownRequestsToUpstream()
    {
        Application.Map(RoutePatternFactory.Parse("{**catchAll}"), () => Results.Extensions.ForwardToUpstream());
    }

    /// <summary>Configures the mock to forward unmatched requests to the actual upstream server using the specified <see cref="HttpClient"/>.</summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> to use for forwarding requests.</param>
    public void ForwardUnknownRequestsToUpstream(HttpClient httpClient)
    {
        Application.Map(RoutePatternFactory.Parse("{**catchAll}"), () => Results.Extensions.ForwardToUpstream(httpClient));
    }

    private IEndpointConventionBuilder Map(string[] methods, string path, Delegate handler)
    {
        return MapCore(path, path => Application.MapMethods(path, methods, handler));
    }

    private IEndpointConventionBuilder Map(string[] methods, string path, RequestDelegate handler)
    {
        return MapCore(path, path => Application.MapMethods(path, methods, handler));
    }

    private static IEndpointConventionBuilder MapCore(string path, Func<string, IEndpointConventionBuilder> mapMethodFunc)
    {
        var (scheme, domain, pathOnly, query) = ParseUrl(path);
        var method = mapMethodFunc(pathOnly);
        var order = 0;

        if (domain is not null)
        {
            method = method.RequireHost(domain);
        }

        if (scheme is not null)
        {
            method = method.WithMetadata(new SchemeMetadata(scheme));
            order--;
        }

        if (query is not null)
        {
            method = method.WithMetadata(new QueryStringMetadata(query));
            order--;
        }

        method.WithOrder(order);
        return method;

        static (string Scheme, string Domain, string Path, string Query) ParseUrl(string path)
        {
            if (path.Contains('#', StringComparison.Ordinal))
                throw new ArgumentException("Fragment ('#') is not supported", nameof(path));

            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && IsHttpScheme(uri.Scheme))
            {
                var index = uri.PathAndQuery.IndexOf('?', StringComparison.Ordinal);
                if (index >= 0)
                {
                    var pathOnly = uri.PathAndQuery.Substring(0, index);
                    var query = uri.PathAndQuery.Substring(index);
                    return (uri.Scheme, uri.Authority, pathOnly, query);
                }

                return (uri.Scheme, uri.Authority, uri.PathAndQuery, null);
            }
            else
            {
                var index = path.IndexOf('?', StringComparison.Ordinal);
                if (index >= 0)
                {
                    var pathOnly = path.Substring(0, index);
                    var query = path.Substring(index);
                    return (null, null, pathOnly, query);
                }
            }

            return (null, null, path, null);
        }

        static bool IsHttpScheme(string scheme) => scheme == Uri.UriSchemeHttp || scheme == Uri.UriSchemeHttps;
    }

    private sealed class SingletonLogger(ILogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }
}
