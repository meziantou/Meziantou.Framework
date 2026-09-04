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
            var listText = await DownloadFilterListAsync(httpClient, options, filter, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Downloads a filter list with an explicit size limit and timeout. The lists are third-party URLs, so an
    /// oversized or slow response must not be buffered into memory unbounded.
    /// </summary>
    private static async Task<string> DownloadFilterListAsync(HttpClient httpClient, DnsProxyOptions options, FilterListOption filter, CancellationToken cancellationToken)
    {
        var maxSize = options.MaxFilterListSizeInBytes > 0 ? options.MaxFilterListSizeInBytes : long.MaxValue;

        using var timeout = options.FilterDownloadTimeout > TimeSpan.Zero ? new CancellationTokenSource(options.FilterDownloadTimeout) : null;
        using var linked = timeout is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var downloadCancellationToken = linked?.Token ?? cancellationToken;

        using var response = await httpClient.GetAsync(filter.Url, HttpCompletionOption.ResponseHeadersRead, downloadCancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is { } contentLength && contentLength > maxSize)
            throw new InvalidOperationException($"The filter list '{filter.Url}' is {contentLength} bytes, which exceeds the {maxSize} bytes limit.");

        using var stream = await response.Content.ReadAsStreamAsync(downloadCancellationToken).ConfigureAwait(false);
        using var limitedStream = new LimitedStream(stream, maxSize, filter.Url);
        using var reader = new StreamReader(limitedStream);

        return await reader.ReadToEndAsync(downloadCancellationToken).ConfigureAwait(false);
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

    /// <summary>Fails the read as soon as more than <paramref name="maxSize"/> bytes have been consumed.</summary>
    private sealed class LimitedStream(Stream innerStream, long maxSize, string filterUrl) : Stream
    {
        private long _totalRead;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _totalRead;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = innerStream.Read(buffer);
            Account(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await innerStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Account(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private void Account(int read)
        {
            _totalRead += read;
            if (_totalRead > maxSize)
                throw new InvalidOperationException($"The filter list '{filterUrl}' exceeds the {maxSize} bytes limit.");
        }
    }
}
