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
        var cacheFolderPath = CreateTemporaryDirectory();

        try
        {
            var options = Options.Create(new DnsProxyOptions
            {
                BlockListCacheFolderPath = cacheFolderPath,
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
        finally
        {
            Directory.Delete(cacheFolderPath, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshAsync_CachesRemoteFiltersAndLoadsCachedFiltersOnStartup()
    {
        var cacheFolderPath = CreateTemporaryDirectory();

        try
        {
            var options = Options.Create(new DnsProxyOptions
            {
                BlockListCacheFolderPath = cacheFolderPath,
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
            Assert.Single(Directory.EnumerateFiles(cacheFolderPath, "*.txt"));

            await cachedProvider.RefreshAsync(CancellationToken.None);

            result = cachedProvider.Engine.Evaluate("cached.example.com");
            Assert.True(result.IsMatched);
            Assert.Equal(1, cachedProvider.RuleCount);
        }
        finally
        {
            Directory.Delete(cacheFolderPath, recursive: true);
        }
    }

    [Fact]
    public async Task RefreshAsync_WhenCanceled_ThrowsOperationCanceledException()
    {
        var cacheFolderPath = CreateTemporaryDirectory();
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheFolderPath,
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

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.RefreshAsync(cancellationTokenSource.Token));
        }
        finally
        {
            Directory.Delete(cacheFolderPath, recursive: true);
        }
    }

    [Fact]
    public async Task FilterEngineRefreshService_RefreshesAtConfiguredInterval()
    {
        var cacheFolderPath = CreateTemporaryDirectory();
        var requestCount = 0;
        var options = Options.Create(new DnsProxyOptions
        {
            BlockListCacheFolderPath = cacheFolderPath,
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
            Directory.Delete(cacheFolderPath, recursive: true);
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

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "meziantou-dnsproxy-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);

        return path;
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
