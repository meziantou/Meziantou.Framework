using System.Net;
using System.Net.Http;
using System.Threading;
using Meziantou.DnsProxy;
using Meziantou.DnsProxy.Filtering;
using Meziantou.Framework.DnsFilter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Meziantou.Framework.DnsProxy.Tests;

public sealed class FilterEngineProviderTests
{
    [Fact]
    public async Task RefreshAsync_CreatesRewriteRules()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            Filters = [],
            Rewrites =
            [
                new RewriteRuleOption { Domain = "example.com", Type = "A", Value = "127.0.0.1" },
                new RewriteRuleOption { Domain = "v6.example.com", Type = "AAAA", Value = "::1" },
            ],
        });

        using var serviceProvider = CreateServiceProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var provider = new FilterEngineProvider(factory, options, NullLogger<FilterEngineProvider>.Instance);
        await provider.RefreshAsync(CancellationToken.None);

        var resultA = provider.Engine.Evaluate("example.com");
        Assert.True(resultA.IsMatched);
        Assert.NotNull(resultA.Rewrite);
        Assert.Equal("127.0.0.1", resultA.Rewrite!.Value);

        var resultAaaa = provider.Engine.Evaluate("v6.example.com", DnsFilterQueryType.AAAA);
        Assert.True(resultAaaa.IsMatched);
        Assert.NotNull(resultAaaa.Rewrite);
        Assert.Equal("::1", resultAaaa.Rewrite!.Value);
    }

    [Fact]
    public async Task RefreshAsync_LoadsRemoteFiltersWithoutBlockingCalls()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
            Rewrites = [],
        });

        using var serviceProvider = CreateServiceProvider(request =>
        {
            Assert.Equal("https://filters.example/list.txt", request.RequestUri!.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("||blocked.example.com^"),
            };
        });
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var provider = new FilterEngineProvider(
            factory,
            options,
            NullLogger<FilterEngineProvider>.Instance);

        await provider.RefreshAsync(CancellationToken.None);

        var result = provider.Engine.Evaluate("blocked.example.com");
        Assert.True(result.IsMatched);
        Assert.Equal(1, provider.RuleCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
            Rewrites = [],
        });

        using var serviceProvider = CreateServiceProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("||blocked.example.com^"),
        });
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var provider = new FilterEngineProvider(
            factory,
            options,
            NullLogger<FilterEngineProvider>.Instance);

        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.RefreshAsync(cancellationTokenSource.Token));
    }

    [Fact]
    public async Task FilterEngineRefreshService_RefreshesAtConfiguredInterval()
    {
        var requestCount = 0;
        var options = Options.Create(new DnsProxyOptions
        {
            FilterRefreshInterval = TimeSpan.FromMilliseconds(100),
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
            Rewrites = [],
        });

        using var serviceProvider = CreateServiceProvider(_ =>
        {
            Interlocked.Increment(ref requestCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("||blocked.example.com^"),
            };
        });
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var provider = new FilterEngineProvider(
            factory,
            options,
            NullLogger<FilterEngineProvider>.Instance);

        using var service = new FilterEngineRefreshService(provider, options, NullLogger<FilterEngineRefreshService>.Instance);
        await service.StartAsync(CancellationToken.None);

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(450));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(Volatile.Read(ref requestCount) >= 2);
    }

    private static ServiceProvider CreateServiceProvider(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(responseFactory));
        return services.BuildServiceProvider();
    }

    private sealed class DelegateHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }
}
