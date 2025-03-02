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

public sealed partial class HttpClientMock : IAsyncDisposable
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
        : this(configureLogging, configureServices: null)
    {
    }

    public HttpClientMock(ILogger? logger, Action<IServiceCollection>? configureServices)
        : this(logger is null ? null : builder => builder.AddProvider(new SingletonLogger(logger)), configureServices)
    {
    }

    public HttpClientMock(ILoggerProvider? loggerProvider, Action<IServiceCollection>? configureServices)
        : this(loggerProvider is null ? null : builder => builder.AddProvider(loggerProvider), configureServices)
    {
    }

    public HttpClientMock(Action<ILoggingBuilder>? configureLogging, Action<IServiceCollection>? configureServices)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Configure<ConsoleLifetimeOptions>(opts => opts.SuppressStatusMessages = true);
        builder.Services.AddSingleton<HttpClient>();
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

    public void ForwardUnknownRequestsToUpstream()
    {
        Application.Map(RoutePatternFactory.Parse("{**catchAll}"), () => Results.Extensions.ForwardToUpstream());
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
