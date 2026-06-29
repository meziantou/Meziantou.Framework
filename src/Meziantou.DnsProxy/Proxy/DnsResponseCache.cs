using System.Collections.Concurrent;
using Meziantou.Framework.DnsServer.Protocol;
using Microsoft.Extensions.Options;

namespace Meziantou.DnsProxy.Proxy;

internal sealed class DnsResponseCache
{
    private readonly ConcurrentDictionary<CacheKey, CacheEntry> _entries = new();
    private readonly Lock _evictionLock = new();
    private readonly IOptions<DnsProxyOptions> _options;
    private readonly TimeProvider _timeProvider;

    public DnsResponseCache(IOptions<DnsProxyOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public bool TryGet(DnsQuestion question, DnsEdnsOptions? ednsOptions, DnsMessage response)
    {
        var key = CacheKey.Create(question, ednsOptions);
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

    public void Store(DnsQuestion question, DnsEdnsOptions? ednsOptions, DnsMessage response)
    {
        var options = _options.Value;
        if (options.MaxCacheEntries <= 0)
        {
            return;
        }

        if (!TryGetCacheDuration(response, out var cacheDuration))
        {
            return;
        }

        var key = CacheKey.Create(question, ednsOptions);
        var now = _timeProvider.GetUtcNow();
        var expiresAtUtc = now.Add(cacheDuration);
        _entries[key] = new CacheEntry(
            expiresAtUtc,
            now,
            response.IsAuthoritative,
            response.IsTruncated,
            response.RecursionAvailable,
            response.AuthenticatedData,
            response.CheckingDisabled,
            response.ResponseCode,
            CloneRecords(response.Answers),
            CloneRecords(response.Authorities),
            CloneRecords(response.AdditionalRecords));

        TrimCache(now, options.MaxCacheEntries);
    }

    private bool TryGetCacheDuration(DnsMessage response, out TimeSpan cacheDuration)
    {
        var options = _options.Value;
        if (response.ResponseCode is DnsResponseCode.NoError && response.Answers.Count > 0)
        {
            if (TryGetLowestCacheableRecordTtl(response, out var serverTtl))
            {
                cacheDuration = Min(TimeSpan.FromSeconds(serverTtl), options.PositiveCacheDuration, options.MaximumCacheDuration);
                return cacheDuration > TimeSpan.Zero;
            }

            cacheDuration = TimeSpan.Zero;
            return false;
        }

        if (response.ResponseCode is DnsResponseCode.NameError || response.ResponseCode is DnsResponseCode.NoError && response.Answers.Count == 0)
        {
            var soaDuration = TryGetLowestRecordTtl(response.Authorities, DnsQueryType.SOA, out var soaTtl)
                ? TimeSpan.FromSeconds(soaTtl)
                : options.NegativeCacheDuration;

            cacheDuration = Min(soaDuration, options.NegativeCacheDuration, options.MaximumCacheDuration);
            return cacheDuration > TimeSpan.Zero;
        }

        cacheDuration = TimeSpan.Zero;
        return false;
    }

    private void TrimCache(DateTimeOffset now, int maxCacheEntries)
    {
        if (_entries.Count <= maxCacheEntries)
        {
            return;
        }

        lock (_evictionLock)
        {
            foreach (var entry in _entries)
            {
                if (entry.Value.ExpiresAtUtc <= now)
                {
                    _entries.TryRemove(entry.Key, out _);
                }
            }

            while (_entries.Count > maxCacheEntries)
            {
                KeyValuePair<CacheKey, CacheEntry>? oldestEntry = null;
                foreach (var entry in _entries)
                {
                    if (oldestEntry is null || entry.Value.StoredAtUtc < oldestEntry.Value.Value.StoredAtUtc)
                    {
                        oldestEntry = entry;
                    }
                }

                if (oldestEntry is not { } entryToRemove)
                {
                    return;
                }

                _entries.TryRemove(entryToRemove.Key, out _);
            }
        }
    }

    private static bool TryGetLowestCacheableRecordTtl(DnsMessage response, out uint ttl)
    {
        ttl = uint.MaxValue;
        var hasRecord = false;
        AddLowestRecordTtl(response.Answers, DnsQueryType.OPT, excludedType: true, ref ttl, ref hasRecord);
        AddLowestRecordTtl(response.Authorities, DnsQueryType.OPT, excludedType: true, ref ttl, ref hasRecord);
        AddLowestRecordTtl(response.AdditionalRecords, DnsQueryType.OPT, excludedType: true, ref ttl, ref hasRecord);
        return hasRecord;
    }

    private static bool TryGetLowestRecordTtl(IEnumerable<DnsResourceRecord> records, DnsQueryType type, out uint ttl)
    {
        ttl = uint.MaxValue;
        var hasRecord = false;
        AddLowestRecordTtl(records, type, excludedType: false, ref ttl, ref hasRecord);
        return hasRecord;
    }

    private static void AddLowestRecordTtl(IEnumerable<DnsResourceRecord> records, DnsQueryType type, bool excludedType, ref uint ttl, ref bool hasRecord)
    {
        foreach (var record in records)
        {
            if (excludedType == (record.Type == type))
            {
                continue;
            }

            ttl = Math.Min(ttl, record.TimeToLive);
            hasRecord = true;
        }
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
        if (records is ICollection<DnsResourceRecord> collection)
        {
            var result = new DnsResourceRecord[collection.Count];
            var index = 0;
            foreach (var record in collection)
            {
                result[index] = CloneRecord(record, record.TimeToLive);
                index++;
            }

            return result;
        }

        var list = new List<DnsResourceRecord>();
        foreach (var record in records)
        {
            list.Add(CloneRecord(record, record.TimeToLive));
        }

        return [.. list];
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

    private readonly record struct CacheKey(string Name, DnsQueryType Type, DnsQueryClass Class, bool HasEdns, bool DnssecOk)
    {
        public static CacheKey Create(DnsQuestion question, DnsEdnsOptions? ednsOptions)
        {
            return new CacheKey(NormalizeName(question.Name), question.Type, question.QueryClass, ednsOptions is not null, ednsOptions?.DnssecOk == true);
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
        DateTimeOffset StoredAtUtc,
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
