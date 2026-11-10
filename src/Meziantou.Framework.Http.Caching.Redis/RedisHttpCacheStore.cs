using System.Buffers.Text;
using System.Text.Json;
using StackExchange.Redis;

namespace Meziantou.Framework.Http.Caching.Redis;

/// <summary>
/// Stores HTTP cache entries in Redis.
/// </summary>
public sealed class RedisHttpCacheStore : IHttpCacheStore
{
    // The scripts of a bounded store. The members of the sorted set (recency) and of the hash of sizes are
    // "<secondary key> <storage key>": the secondary key is hexadecimal, so the first space separates them.
    // KEYS: entries, primary keys, recency, sizes, statistics. ARGV: secondary key, payload, size, maximum count, maximum size (-1 for none).
    private const string SetEntryScript = """
        local member = ARGV[1] .. ' ' .. KEYS[1]
        redis.call('HSET', KEYS[1], ARGV[1], ARGV[2])
        redis.call('SADD', KEYS[2], KEYS[1])
        local size = tonumber(ARGV[3])
        local previousSize = redis.call('HGET', KEYS[4], member)
        if previousSize then
          redis.call('HINCRBY', KEYS[5], 'size', size - tonumber(previousSize))
        else
          redis.call('HINCRBY', KEYS[5], 'count', 1)
          redis.call('HINCRBY', KEYS[5], 'size', size)
        end
        redis.call('HSET', KEYS[4], member, size)
        redis.call('ZADD', KEYS[3], redis.call('HINCRBY', KEYS[5], 'clock', 1), member)
        local maxCount = tonumber(ARGV[4])
        local maxSize = tonumber(ARGV[5])
        while true do
          local count = tonumber(redis.call('HGET', KEYS[5], 'count') or 0)
          local total = tonumber(redis.call('HGET', KEYS[5], 'size') or 0)
          if (maxCount < 0 or count <= maxCount) and (maxSize < 0 or total <= maxSize) then
            break
          end
          local oldest = redis.call('ZRANGE', KEYS[3], 0, 0)
          if #oldest == 0 then
            break
          end
          local victim = oldest[1]
          local separator = string.find(victim, ' ', 1, true)
          local victimKey = string.sub(victim, separator + 1)
          redis.call('HDEL', victimKey, string.sub(victim, 1, separator - 1))
          if redis.call('HLEN', victimKey) == 0 then
            redis.call('SREM', KEYS[2], victimKey)
          end
          redis.call('HINCRBY', KEYS[5], 'count', -1)
          redis.call('HINCRBY', KEYS[5], 'size', -tonumber(redis.call('HGET', KEYS[4], victim) or 0))
          redis.call('HDEL', KEYS[4], victim)
          redis.call('ZREM', KEYS[3], victim)
        end
        return 0
        """;

    // KEYS: entries, recency, statistics. Returns the fields and values of the entries hash.
    private const string GetEntriesScript = """
        local values = redis.call('HGETALL', KEYS[1])
        if #values > 0 then
          local clock = redis.call('HINCRBY', KEYS[3], 'clock', 1)
          for i = 1, #values, 2 do
            redis.call('ZADD', KEYS[2], 'XX', clock, values[i] .. ' ' .. KEYS[1])
          end
        end
        return values
        """;

    // KEYS: entries, primary keys, recency, sizes, statistics. ARGV: the secondary keys to remove, or none to remove them all.
    private const string RemoveEntriesScript = """
        local fields = ARGV
        if #fields == 0 then
          fields = redis.call('HKEYS', KEYS[1])
        end
        for _, field in ipairs(fields) do
          local member = field .. ' ' .. KEYS[1]
          redis.call('HDEL', KEYS[1], field)
          local size = redis.call('HGET', KEYS[4], member)
          if size then
            redis.call('HINCRBY', KEYS[5], 'count', -1)
            redis.call('HINCRBY', KEYS[5], 'size', -tonumber(size))
            redis.call('HDEL', KEYS[4], member)
            redis.call('ZREM', KEYS[3], member)
          end
        end
        if redis.call('HLEN', KEYS[1]) == 0 then
          redis.call('SREM', KEYS[2], KEYS[1])
        end
        return 0
        """;

    private readonly IDatabase _database;
    private readonly RedisKey _primaryKeysSetKey;
    private readonly RedisKey _recencyKey;
    private readonly RedisKey _sizesKey;
    private readonly RedisKey _statisticsKey;
    private readonly string _keyPrefix;
    private readonly long? _maximumEntryCount;
    private readonly long? _maximumTotalSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisHttpCacheStore"/> class with no limit.
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer.</param>
    /// <param name="keyPrefix">The key prefix used to isolate cache entries in Redis.</param>
    public RedisHttpCacheStore(IConnectionMultiplexer connectionMultiplexer, string keyPrefix = "Meziantou:HttpCache")
        : this(connectionMultiplexer, keyPrefix, limits: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisHttpCacheStore"/> class that evicts the least recently used
    /// entries to stay within <paramref name="limits"/>.
    /// </summary>
    /// <param name="connectionMultiplexer">The Redis connection multiplexer.</param>
    /// <param name="keyPrefix">The key prefix used to isolate cache entries in Redis.</param>
    /// <param name="limits">The bounds of the store, or <see langword="null"/> for no limit. The values are read once, by the constructor.</param>
    /// <remarks>
    /// A bounded store updates its keys with Lua scripts. With Redis Cluster, the keys of the store must be in the same
    /// slot: use a key prefix with a hash tag, such as <c>{MyApp}:HttpCache</c>.
    /// </remarks>
    public RedisHttpCacheStore(IConnectionMultiplexer connectionMultiplexer, string keyPrefix, HttpCacheStoreLimits? limits)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);

        _database = connectionMultiplexer.GetDatabase();
        _keyPrefix = keyPrefix.TrimEnd(':');
        _primaryKeysSetKey = _keyPrefix + ":primary-keys";
        _recencyKey = _keyPrefix + ":recency";
        _sizesKey = _keyPrefix + ":sizes";
        _statisticsKey = _keyPrefix + ":statistics";
        _maximumEntryCount = limits?.MaximumEntryCount;
        _maximumTotalSize = limits?.MaximumTotalSize;
    }

    private bool IsBounded => _maximumEntryCount is not null || _maximumTotalSize is not null;

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        var storageKey = GetPrimaryStorageKey(primaryKey);
        RedisValue[] values;
        if (IsBounded)
        {
            var result = await _database.ScriptEvaluateAsync(GetEntriesScript, [storageKey, _recencyKey, _statisticsKey]).ConfigureAwait(false);
            var fieldsAndValues = (RedisValue[]?)result ?? [];
            values = new RedisValue[fieldsAndValues.Length / 2];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = fieldsAndValues[(2 * i) + 1];
            }
        }
        else
        {
            var hashEntries = await _database.HashGetAllAsync(storageKey).ConfigureAwait(false);
            values = Array.ConvertAll(hashEntries, static hashEntry => hashEntry.Value);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (values.Length is 0)
            return Array.Empty<HttpCachePersistenceEntry>();

        var entries = new List<HttpCachePersistenceEntry>(values.Length);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = TryDeserializeEntry(value);
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
        var secondaryKey = entry.ComputeSecondaryKeyHash();
        var payload = JsonSerializer.SerializeToUtf8Bytes(entry, RedisSerializationContext.Default.HttpCachePersistenceEntry);

        if (IsBounded)
        {
            await _database.ScriptEvaluateAsync(
                SetEntryScript,
                [storageKey, _primaryKeysSetKey, _recencyKey, _sizesKey, _statisticsKey],
                [secondaryKey, payload, entry.SerializedResponse.Length, _maximumEntryCount ?? -1, _maximumTotalSize ?? -1]).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

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
        if (IsBounded)
        {
            await RemoveTrackedEntriesAsync(storageKey, []).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return;
        }

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
                if (entry is null || entry.IsObsolete(now))
                {
                    fieldsToDelete ??= new List<RedisValue>();
                    fieldsToDelete.Add(hashEntry.Name);
                }
            }

            if (fieldsToDelete is not null)
            {
                if (IsBounded)
                {
                    await RemoveTrackedEntriesAsync(storageKey, [.. fieldsToDelete]).ConfigureAwait(false);
                }
                else
                {
                    await _database.HashDeleteAsync(storageKey, fieldsToDelete.ToArray()).ConfigureAwait(false);
                }
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

    private Task<RedisResult> RemoveTrackedEntriesAsync(RedisKey storageKey, RedisValue[] secondaryKeys)
    {
        return _database.ScriptEvaluateAsync(RemoveEntriesScript, [storageKey, _primaryKeysSetKey, _recencyKey, _sizesKey, _statisticsKey], secondaryKeys);
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
        var bytes = Encoding.UTF8.GetBytes(primaryKey);
        return Base64Url.EncodeToString(bytes);
    }
}
