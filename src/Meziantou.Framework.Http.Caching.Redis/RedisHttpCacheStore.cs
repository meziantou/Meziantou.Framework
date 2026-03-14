using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace Meziantou.Framework.Http.Caching.Redis;

/// <summary>
/// Stores HTTP cache entries in Redis.
/// </summary>
public sealed class RedisHttpCacheStore : IHttpCacheStore
{
    private readonly IDatabase _database;
    private readonly RedisKey _primaryKeysSetKey;
    private readonly string _keyPrefix;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisHttpCacheStore"/> class.
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer.</param>
    /// <param name="keyPrefix">The key prefix used to isolate cache entries in Redis.</param>
    public RedisHttpCacheStore(IConnectionMultiplexer connectionMultiplexer, string keyPrefix = "Meziantou:HttpCache")
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix.TrimEnd(':');
        _primaryKeysSetKey = _keyPrefix + ":primary-keys";
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = GetPrimaryStorageKey(primaryKey);
        var values = await _database.HashGetAllAsync(storageKey).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (values.Length is 0)
            return Array.Empty<HttpCachePersistenceEntry>();

        var entries = new List<HttpCachePersistenceEntry>(values.Length);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = TryDeserializeEntry(value.Value);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    /// <inheritdoc />
    public async ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = GetPrimaryStorageKey(primaryKey);
        var storageKeyValue = storageKey.ToString();
        var secondaryKey = ComputeSecondaryKey(entry);
        var payload = JsonSerializer.SerializeToUtf8Bytes(entry, RedisSerializationContext.Default.HttpCachePersistenceEntry);

        var indexTask = _database.SetAddAsync(_primaryKeysSetKey, storageKeyValue);
        var hashTask = _database.HashSetAsync(storageKey, new[] { new HashEntry(secondaryKey, payload) });
        await Task.WhenAll(indexTask, hashTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public async ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = GetPrimaryStorageKey(primaryKey);
        var storageKeyValue = storageKey.ToString();
        var deleteTask = _database.KeyDeleteAsync(storageKey);
        var indexTask = _database.SetRemoveAsync(_primaryKeysSetKey, storageKeyValue);
        await Task.WhenAll(deleteTask, indexTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Removes expired entries that cannot be reused when stale.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask PruneObsoleteEntriesAsync(CancellationToken cancellationToken = default)
    {
        return PruneObsoleteEntriesAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// Removes expired entries that cannot be reused when stale.
    /// </summary>
    /// <param name="now">The current time used to evaluate expiration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask PruneObsoleteEntriesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var storageKeys = await _database.SetMembersAsync(_primaryKeysSetKey).ConfigureAwait(false);
        foreach (var storageKeyValue in storageKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (storageKeyValue.IsNullOrEmpty)
                continue;

            var storageKey = (RedisKey)storageKeyValue.ToString();
            var hashEntries = await _database.HashGetAllAsync(storageKey).ConfigureAwait(false);
            if (hashEntries.Length is 0)
            {
                await _database.SetRemoveAsync(_primaryKeysSetKey, storageKeyValue).ConfigureAwait(false);
                continue;
            }

            List<RedisValue>? fieldsToDelete = null;
            foreach (var hashEntry in hashEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry = TryDeserializeEntry(hashEntry.Value);
                if (entry is null || ShouldDelete(entry, now))
                {
                    fieldsToDelete ??= new List<RedisValue>();
                    fieldsToDelete.Add(hashEntry.Name);
                }
            }

            if (fieldsToDelete is not null)
            {
                await _database.HashDeleteAsync(storageKey, fieldsToDelete.ToArray()).ConfigureAwait(false);
            }

            if (await _database.HashLengthAsync(storageKey).ConfigureAwait(false) is 0)
            {
                var deleteTask = _database.KeyDeleteAsync(storageKey);
                var removeIndexTask = _database.SetRemoveAsync(_primaryKeysSetKey, storageKeyValue);
                await Task.WhenAll(deleteTask, removeIndexTask).ConfigureAwait(false);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private RedisKey GetPrimaryStorageKey(string primaryKey)
    {
        var encodedPrimaryKey = EncodePrimaryKey(primaryKey);
        return _keyPrefix + ":entries:" + encodedPrimaryKey;
    }

    private static HttpCachePersistenceEntry? TryDeserializeEntry(RedisValue value)
    {
        if (value.IsNullOrEmpty)
            return null;

        var payload = (byte[]?)value;
        if (payload is null || payload.Length is 0)
            return null;

        try
        {
            return JsonSerializer.Deserialize(payload, RedisSerializationContext.Default.HttpCachePersistenceEntry);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string EncodePrimaryKey(string primaryKey)
    {
        // TODO Check if it can use Base64Url
        var bytes = Encoding.UTF8.GetBytes(primaryKey);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ComputeSecondaryKey(HttpCachePersistenceEntry entry)
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.Append(entry.SecondaryKeyMatchNone ? '1' : '0');

        var headers = entry.SecondaryKeyHeaders;
        if (headers is not null)
        {
            foreach (var (key, value) in headers.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                stringBuilder.Append('\u001f');
                stringBuilder.Append(key);
                stringBuilder.Append('\u001e');
                stringBuilder.Append(value);
            }
        }

        var bytes = Encoding.UTF8.GetBytes(stringBuilder.ToString());
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash);
    }

    private static bool ShouldDelete(HttpCachePersistenceEntry entry, DateTimeOffset now)
    {
        return IsExpired(entry, now) && ShouldDeleteWhenExpired(entry);
    }

    private static bool ShouldDeleteWhenExpired(HttpCachePersistenceEntry entry)
    {
        var cannotBeUsedStale = entry.MustRevalidate || entry.ProxyRevalidate || entry.ResponseNoCache;
        if (!cannotBeUsedStale)
            return false;

        return string.IsNullOrEmpty(entry.ETag) && entry.LastModified is null;
    }

    private static bool IsExpired(HttpCachePersistenceEntry entry, DateTimeOffset now)
    {
        var freshnessLifetime = GetFreshnessLifetime(entry);
        var currentAge = CalculateCurrentAge(entry, now);
        return currentAge >= freshnessLifetime;
    }

    private static TimeSpan GetFreshnessLifetime(HttpCachePersistenceEntry entry)
    {
        if (entry.SharedMaxAge.HasValue)
            return entry.SharedMaxAge.Value;

        if (entry.MaxAge.HasValue)
            return entry.MaxAge.Value;

        if (entry.Expires.HasValue)
        {
            var expiresTime = entry.Expires.Value;
            if (expiresTime == DateTimeOffset.MinValue)
                return TimeSpan.Zero;

            var freshness = expiresTime - entry.ResponseDate;
            return freshness > TimeSpan.Zero ? freshness : TimeSpan.Zero;
        }

        if (entry.LastModified.HasValue)
        {
            var age = entry.ResponseDate - entry.LastModified.Value;
            if (age > TimeSpan.Zero)
            {
                return TimeSpan.FromSeconds(age.TotalSeconds * 0.1);
            }
        }

        return TimeSpan.Zero;
    }

    private static TimeSpan CalculateCurrentAge(HttpCachePersistenceEntry entry, DateTimeOffset now)
    {
        var correctedInitialAge = CalculateCorrectedInitialAge(entry);
        var residentTime = now - entry.ResponseTime;
        return correctedInitialAge + residentTime;
    }

    private static TimeSpan CalculateCorrectedInitialAge(HttpCachePersistenceEntry entry)
    {
        var apparentAge = entry.ResponseTime - entry.ResponseDate;
        if (apparentAge < TimeSpan.Zero)
            apparentAge = TimeSpan.Zero;

        var responseDelay = entry.ResponseTime - entry.RequestTime;
        var correctedAgeValue = entry.AgeValue + responseDelay;
        return apparentAge > correctedAgeValue ? apparentAge : correctedAgeValue;
    }
}
