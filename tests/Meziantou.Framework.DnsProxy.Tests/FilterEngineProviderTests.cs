using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
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
    public async Task RefreshAsync_WithNoFilters_KeepsRuleSetEmpty()
    {
        var options = Options.Create(new DnsProxyOptions
        {
            Filters = [],
        });

        using var serviceProvider = CreateServiceProvider(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var provider = new FilterEngineProvider(factory, options, NullLogger<FilterEngineProvider>.Instance);
        await provider.RefreshAsync(CancellationToken.None);

        var result = provider.Engine.Evaluate("example.com", DnsFilterQueryType.A);
        Assert.False(result.IsMatched);
        Assert.Equal(0, provider.RuleCount);
    }

    [Fact]
    public async Task RefreshAsync_LoadsRemoteFiltersWithoutBlockingCalls()
    {
        using var cacheDirectory = TemporaryDirectory.Create();
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheDirectory.FullPath,
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
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
    public async Task RefreshAsync_EmitsActivitiesWhenLoadingRemoteFilters()
    {
        using var cacheDirectory = TemporaryDirectory.Create();
        var filterUrl = $"https://filters.example/{Guid.NewGuid():N}.txt";
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheDirectory.FullPath,
            Filters =
            [
                new FilterListOption
                {
                    Url = filterUrl,
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
        });

        var activities = new ConcurrentQueue<Activity>();
        using var listener = CreateDnsProxyActivityListener(activities.Enqueue);
        ActivitySource.AddActivityListener(listener);

        using var serviceProvider = CreateServiceProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("||blocked.example.com^"),
        });
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var provider = new FilterEngineProvider(
            factory,
            options,
            NullLogger<FilterEngineProvider>.Instance);

        await provider.RefreshAsync(CancellationToken.None);

        var loadActivity = Assert.Single(activities, activity =>
            activity.OperationName == "dns_proxy.filters.load" &&
            Equals(filterUrl, activity.GetTagItem("dns_proxy.filter.url")));
        var refreshActivity = Assert.Single(activities, activity =>
            activity.OperationName == "dns_proxy.filters.refresh" &&
            activity.SpanId == loadActivity.ParentSpanId);

        Assert.Equal(refreshActivity.SpanId, loadActivity.ParentSpanId);
        Assert.Equal(ActivityStatusCode.Ok, refreshActivity.Status);
        Assert.Equal(ActivityStatusCode.Ok, loadActivity.Status);
        Assert.Equal(1, Assert.IsType<int>(refreshActivity.GetTagItem("dns_proxy.filter.count")));
        Assert.Equal(1, Assert.IsType<int>(refreshActivity.GetTagItem("dns_proxy.filter.loaded_count")));
        Assert.Equal(0, Assert.IsType<int>(refreshActivity.GetTagItem("dns_proxy.filter.failed_count")));
        Assert.Equal(1, Assert.IsType<int>(refreshActivity.GetTagItem("dns_proxy.rule.count")));
        Assert.Equal(filterUrl, loadActivity.GetTagItem("dns_proxy.filter.url"));
        Assert.Equal(nameof(DnsFilterListFormat.AdBlock), loadActivity.GetTagItem("dns_proxy.filter.format"));
        Assert.Equal(1, Assert.IsType<int>(loadActivity.GetTagItem("dns_proxy.filter.rule_count")));
    }

    [Fact]
    public async Task RefreshAsync_CachesRemoteFiltersAndLoadsCachedFiltersOnStartup()
    {
        using var cacheDirectory = TemporaryDirectory.Create();
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheDirectory.FullPath,
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
        });

        using var serviceProvider = CreateServiceProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("||cached.example.com^"),
        });
        var factory = serviceProvider.GetRequiredService<IHttpClientFactory>();

        var provider = new FilterEngineProvider(
            factory,
            options,
            NullLogger<FilterEngineProvider>.Instance);

        await provider.RefreshAsync(CancellationToken.None);

        using var cachedServiceProvider = CreateServiceProvider(_ => throw new InvalidOperationException("Remote refresh failed."));
        var cachedFactory = cachedServiceProvider.GetRequiredService<IHttpClientFactory>();

        var cachedProvider = new FilterEngineProvider(
            cachedFactory,
            options,
            NullLogger<FilterEngineProvider>.Instance);

        var result = cachedProvider.Engine.Evaluate("cached.example.com");
        Assert.True(result.IsMatched);
        Assert.Equal(1, cachedProvider.RuleCount);
        Assert.Single(Directory.EnumerateFiles(cacheDirectory.FullPath, "*.txt"));

        await cachedProvider.RefreshAsync(CancellationToken.None);

        result = cachedProvider.Engine.Evaluate("cached.example.com");
        Assert.True(result.IsMatched);
        Assert.Equal(1, cachedProvider.RuleCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        using var cacheDirectory = TemporaryDirectory.Create();
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheDirectory.FullPath,
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
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
        using var cacheDirectory = TemporaryDirectory.Create();
        var requestCount = 0;
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheDirectory.FullPath,
            FilterRefreshInterval = TimeSpan.FromMilliseconds(100),
            Filters =
            [
                new FilterListOption
                {
                    Url = "https://filters.example/list.txt",
                    Format = nameof(DnsFilterListFormat.AdBlock),
                },
            ],
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
            var timeout = TimeSpan.FromSeconds(5);
            var stopwatch = Stopwatch.StartNew();
            while (Volatile.Read(ref requestCount) < 2 && stopwatch.Elapsed < timeout)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50));
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }

        Assert.True(Volatile.Read(ref requestCount) >= 2, $"Expected at least 2 refresh attempts, but got {requestCount}");
    }

    private static ServiceProvider CreateServiceProvider(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
    {
        var services = new ServiceCollection();
        services
            .AddHttpClient(Options.DefaultName)
            .ConfigurePrimaryHttpMessageHandler(() => new DelegateHttpMessageHandler(responseFactory));
        return services.BuildServiceProvider();
    }

    private static ActivityListener CreateDnsProxyActivityListener(Action<Activity> activityStopped)
    {
        return new ActivityListener
        {
            ShouldListenTo = static source => source.Name == "Meziantou.DnsProxy",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activityStopped,
        };
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
