using System.Collections.Concurrent;
using Meziantou.Framework.DnsServer.Protocol;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class DnsResponseCache
{
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _entries = new();
    private readonly IOptions<DnsProxyOptions> _options;
    private readonly TimeProvider _timeProvider;

    public DnsResponseCache(IOptions<DnsProxyOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public bool TryGet(DnsQuestion question, DnsMessage response)
    {
        var key = CacheKey.Create(question);
        if (!_entries.TryGetValue(key, out var entry))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow();
        if (entry.ExpiresAtUtc <= now)
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        var remainingTtl = GetRemainingTtl(entry.ExpiresAtUtc, now);
        response.IsAuthoritative = entry.IsAuthoritative;
        response.IsTruncated = entry.IsTruncated;
        response.RecursionAvailable = entry.RecursionAvailable;
        response.AuthenticatedData = entry.AuthenticatedData;
        response.CheckingDisabled = entry.CheckingDisabled;
        response.ResponseCode = entry.ResponseCode;

        AddRecords(response.Answers, entry.Answers, remainingTtl);
        AddRecords(response.Authorities, entry.Authorities, remainingTtl);
        AddRecords(response.AdditionalRecords, entry.AdditionalRecords, remainingTtl);
        return true;
    }

    public void Store(DnsQuestion question, DnsMessage response)
    {
        if (!TryGetCacheDuration(response, out var cacheDuration))
        {
            return;
        }

        var key = CacheKey.Create(question);
        var expiresAtUtc = _timeProvider.GetUtcNow().Add(cacheDuration);
        _entries[key] = new CacheEntry(
            expiresAtUtc,
            response.IsAuthoritative,
            response.IsTruncated,
            response.RecursionAvailable,
            response.AuthenticatedData,
            response.CheckingDisabled,
            response.ResponseCode,
            CloneRecords(response.Answers),
            CloneRecords(response.Authorities),
            CloneRecords(response.AdditionalRecords));
    }

    private bool TryGetCacheDuration(DnsMessage response, out TimeSpan cacheDuration)
    {
        var options = _options.Value;
        if (response.ResponseCode is DnsResponseCode.NoError && response.Answers.Count > 0)
        {
            if (TryGetLowestRecordTtl(GetCacheableRecords(response), out var serverTtl))
            {
                cacheDuration = Min(TimeSpan.FromSeconds(serverTtl), options.PositiveCacheDuration, options.MaximumCacheDuration);
                return cacheDuration > TimeSpan.Zero;
            }

            cacheDuration = TimeSpan.Zero;
            return false;
        }

        if (response.ResponseCode is DnsResponseCode.NameError || response.ResponseCode is DnsResponseCode.NoError && response.Answers.Count == 0)
        {
            var soaDuration = TryGetLowestRecordTtl(response.Authorities.Where(record => record.Type is DnsQueryType.SOA), out var soaTtl)
                ? TimeSpan.FromSeconds(soaTtl)
                : options.NegativeCacheDuration;

            cacheDuration = Min(soaDuration, options.NegativeCacheDuration, options.MaximumCacheDuration);
            return cacheDuration > TimeSpan.Zero;
        }

        cacheDuration = TimeSpan.Zero;
        return false;
    }

    private static IEnumerable<DnsResourceRecord> GetCacheableRecords(DnsMessage response)
    {
        return response.Answers
            .Concat(response.Authorities)
            .Concat(response.AdditionalRecords)
            .Where(record => record.Type is not DnsQueryType.OPT);
    }

    private static bool TryGetLowestRecordTtl(IEnumerable<DnsResourceRecord> records, out uint ttl)
    {
        ttl = uint.MaxValue;
        var hasRecord = false;
        foreach (var record in records)
        {
            ttl = Math.Min(ttl, record.TimeToLive);
            hasRecord = true;
        }

        return hasRecord;
    }

    private static TimeSpan Min(TimeSpan value1, TimeSpan value2, TimeSpan value3)
    {
        var minimum = value1 <= value2 ? value1 : value2;
        return minimum <= value3 ? minimum : value3;
    }

    private static uint GetRemainingTtl(DateTimeOffset expiresAtUtc, DateTimeOffset now)
    {
        var remainingSeconds = Math.Ceiling((expiresAtUtc - now).TotalSeconds);
        if (remainingSeconds >= uint.MaxValue)
        {
            return uint.MaxValue;
        }

        return (uint)remainingSeconds;
    }

    private static DnsResourceRecord[] CloneRecords(IEnumerable<DnsResourceRecord> records)
    {
        return records.Select(record => CloneRecord(record, record.TimeToLive)).ToArray();
    }

    private static void AddRecords(ICollection<DnsResourceRecord> target, IEnumerable<DnsResourceRecord> records, uint remainingTtl)
    {
        foreach (var record in records)
        {
            var ttl = record.Type is DnsQueryType.OPT ? record.TimeToLive : remainingTtl;
            target.Add(CloneRecord(record, ttl));
        }
    }

    private static DnsResourceRecord CloneRecord(DnsResourceRecord record, uint ttl)
    {
        return new DnsResourceRecord
        {
            Name = record.Name,
            Type = record.Type,
            Class = record.Class,
            TimeToLive = ttl,
            Data = record.Data,
        };
    }

    private readonly record struct CacheKey(string Name, DnsQueryType Type, DnsQueryClass Class)
    {
        public static CacheKey Create(DnsQuestion question)
        {
            return new CacheKey(NormalizeName(question.Name), question.Type, question.QueryClass);
        }

        private static string NormalizeName(string name)
        {
            if (name is ".")
            {
                return ".";
            }

            return name.TrimEnd('.').ToLowerInvariant();
        }
    }

    private sealed record CacheEntry(
        DateTimeOffset ExpiresAtUtc,
        bool IsAuthoritative,
        bool IsTruncated,
        bool RecursionAvailable,
        bool AuthenticatedData,
        bool CheckingDisabled,
        DnsResponseCode ResponseCode,
        DnsResourceRecord[] Answers,
        DnsResourceRecord[] Authorities,
        DnsResourceRecord[] AdditionalRecords);
}
