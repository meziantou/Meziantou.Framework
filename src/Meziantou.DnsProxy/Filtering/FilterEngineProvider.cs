using System.Diagnostics;
using System.Security.Cryptography;
using Meziantou.Framework.DnsFilter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Filtering;

internal sealed class FilterEngineProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<DnsProxyOptions> _options;
    private readonly ILogger<FilterEngineProvider> _logger;
    private DnsFilterEngine _engine;
    private int _ruleCount;

    public FilterEngineProvider(IHttpClientFactory httpClientFactory, IOptions<DnsProxyOptions> options, ILogger<FilterEngineProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;

        var initialRuleSet = new DnsFilterRuleSet();
        AddCachedFilterLists(initialRuleSet, options.Value);
        _engine = new DnsFilterEngine(initialRuleSet);
        _ruleCount = initialRuleSet.Rules.Count;
    }

    public DnsFilterEngine Engine => Volatile.Read(ref _engine);

    public int RuleCount => Volatile.Read(ref _ruleCount);

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var ruleSet = new DnsFilterRuleSet();
        using var httpClient = _httpClientFactory.CreateClient();
        using var activity = DnsProxyTelemetry.ActivitySource.StartActivity("dns_proxy.filters.refresh");
        var filterCount = 0;
        var loadedFilterCount = 0;
        var failedFilterCount = 0;

        foreach (var filter in options.Filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Url))
            {
                continue;
            }

            filterCount++;
            if (await TryLoadFilterAsync(httpClient, ruleSet, options, filter, cancellationToken).ConfigureAwait(false))
            {
                loadedFilterCount++;
            }
            else
            {
                failedFilterCount++;
            }
        }

        activity?.SetTag("dns_proxy.filter.count", filterCount);
        activity?.SetTag("dns_proxy.filter.loaded_count", loadedFilterCount);
        activity?.SetTag("dns_proxy.filter.failed_count", failedFilterCount);
        activity?.SetTag("dns_proxy.rule.count", ruleSet.Rules.Count);

        if (failedFilterCount == 0)
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, $"{failedFilterCount} filter lists failed to load");
        }

        Volatile.Write(ref _ruleCount, ruleSet.Rules.Count);
        Volatile.Write(ref _engine, new DnsFilterEngine(ruleSet));
    }

    private async Task<bool> TryLoadFilterAsync(HttpClient httpClient, DnsFilterRuleSet ruleSet, DnsProxyOptions options, FilterListOption filter, CancellationToken cancellationToken)
    {
        using var activity = DnsProxyTelemetry.ActivitySource.StartActivity("dns_proxy.filters.load");
        activity?.SetTag("dns_proxy.filter.url", filter.Url);

        try
        {
            var format = Enum.TryParse<DnsFilterListFormat>(filter.Format, ignoreCase: true, out var parsedFormat)
                ? parsedFormat
                : DnsFilterListFormat.AutoDetect;
            activity?.SetTag("dns_proxy.filter.format", format.ToString());

            var ruleCount = ruleSet.Rules.Count;
            var listText = await httpClient.GetStringAsync(filter.Url, cancellationToken).ConfigureAwait(false);
            ruleSet.AddFromList(listText, format);
            await WriteFilterListToCacheAsync(options, filter, listText, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("dns_proxy.filter.rule_count", ruleSet.Rules.Count - ruleCount);
            activity?.SetStatus(ActivityStatusCode.Ok);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AddCachedFilterList(ruleSet, options, filter);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogWarning(ex, "Cannot load filter list {FilterUrl}", filter.Url);

            return false;
        }
    }

    private void AddCachedFilterLists(DnsFilterRuleSet ruleSet, DnsProxyOptions options)
    {
        foreach (var filter in options.Filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Url))
            {
                continue;
            }

            AddCachedFilterList(ruleSet, options, filter);
        }
    }

    private void AddCachedFilterList(DnsFilterRuleSet ruleSet, DnsProxyOptions options, FilterListOption filter)
    {
        try
        {
            var cacheFilePath = GetCacheFilePath(options, filter);
            if (!File.Exists(cacheFilePath))
            {
                return;
            }

            var listText = File.ReadAllText(cacheFilePath);
            AddFilterList(ruleSet, filter, listText);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot load cached filter list {FilterUrl}", filter.Url);
        }
    }

    private async Task WriteFilterListToCacheAsync(DnsProxyOptions options, FilterListOption filter, string listText, CancellationToken cancellationToken)
    {
        string? temporaryFilePath = null;
        try
        {
            var cacheFilePath = GetCacheFilePath(options, filter);
            var cacheDirectory = Path.GetDirectoryName(cacheFilePath);
            if (string.IsNullOrWhiteSpace(cacheDirectory))
            {
                return;
            }

            Directory.CreateDirectory(cacheDirectory);

            temporaryFilePath = Path.Combine(cacheDirectory, Path.GetRandomFileName());
            await File.WriteAllTextAsync(temporaryFilePath, listText, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryFilePath, cacheFilePath, overwrite: true);
            temporaryFilePath = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot cache filter list {FilterUrl}", filter.Url);
        }
        finally
        {
            if (temporaryFilePath is not null)
            {
                try
                {
                    File.Delete(temporaryFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cannot delete temporary cached filter list {TemporaryFilePath}", temporaryFilePath);
                }
            }
        }
    }

    private static void AddFilterList(DnsFilterRuleSet ruleSet, FilterListOption filter, string listText)
    {
        var format = Enum.TryParse<DnsFilterListFormat>(filter.Format, ignoreCase: true, out var parsedFormat)
            ? parsedFormat
            : DnsFilterListFormat.AutoDetect;
        ruleSet.AddFromList(listText, format);
    }

    private static string GetCacheFilePath(DnsProxyOptions options, FilterListOption filter)
    {
        var cacheFolderPath = string.IsNullOrWhiteSpace(options.BlockListCacheFolderPath)
            ? DnsProxyOptions.GetDefaultBlockListCacheFolderPath()
            : options.BlockListCacheFolderPath;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(filter.Url))).ToLowerInvariant();

        return Path.Combine(cacheFolderPath, hash + ".txt");
    }
}
