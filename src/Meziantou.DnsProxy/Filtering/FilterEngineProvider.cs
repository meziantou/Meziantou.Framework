using Meziantou.Framework.DnsFilter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading;

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
        AddRewriteRules(initialRuleSet, options.Value);
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

        foreach (var filter in options.Filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Url))
            {
                continue;
            }

            try
            {
                var listText = await httpClient.GetStringAsync(filter.Url, cancellationToken).ConfigureAwait(false);
                var format = Enum.TryParse<DnsFilterListFormat>(filter.Format, ignoreCase: true, out var parsedFormat)
                    ? parsedFormat
                    : DnsFilterListFormat.AutoDetect;
                ruleSet.AddFromList(listText, format);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot load filter list {FilterUrl}", filter.Url);
            }
        }

        AddRewriteRules(ruleSet, options);

        Volatile.Write(ref _ruleCount, ruleSet.Rules.Count);
        Volatile.Write(ref _engine, new DnsFilterEngine(ruleSet));
    }

    private void AddRewriteRules(DnsFilterRuleSet ruleSet, DnsProxyOptions options)
    {
        foreach (var rewrite in options.Rewrites)
        {
            if (string.IsNullOrWhiteSpace(rewrite.Domain))
            {
                continue;
            }

            if (!TryBuildRewriteValue(rewrite, out var rewriteValue))
            {
                _logger.LogWarning("Skipping invalid rewrite rule for domain {Domain}", rewrite.Domain);
                continue;
            }

            ruleSet.AddFromList($"||{rewrite.Domain}^$dnsrewrite={rewriteValue}", DnsFilterListFormat.AdBlock);
        }
    }

    private static bool TryBuildRewriteValue(RewriteRuleOption rewrite, out string rewriteValue)
    {
        rewriteValue = string.Empty;
        if (string.IsNullOrWhiteSpace(rewrite.Value))
        {
            return false;
        }

        if (Enum.TryParse<DnsFilterRewriteResponseCode>(rewrite.Value, ignoreCase: true, out _))
        {
            rewriteValue = rewrite.Value;
            return true;
        }

        if (!Enum.TryParse<DnsFilterQueryType>(rewrite.Type, ignoreCase: true, out var rewriteType))
        {
            return false;
        }

        rewriteValue = $"NOERROR;{rewriteType};{rewrite.Value}";
        return true;
    }
}
