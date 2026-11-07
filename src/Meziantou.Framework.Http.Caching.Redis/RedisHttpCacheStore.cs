using System.Buffers.Text;
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
        var secondaryKey = entry.ComputeSecondaryKeyHash();
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
                if (entry is null || entry.IsObsolete(now))
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
        var bytes = Encoding.UTF8.GetBytes(primaryKey);
        return Base64Url.EncodeToString(bytes);
    }
}
