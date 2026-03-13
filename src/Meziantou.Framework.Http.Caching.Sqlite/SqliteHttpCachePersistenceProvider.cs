using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Http.Caching.Sqlite;

/// <summary>
/// Stores HTTP cache entries in a SQLite database.
/// </summary>
public sealed class SqliteHttpCachePersistenceProvider : IHttpCachePersistenceProvider, IDisposable
{
    private const string SplitColumnsSql =
        "SecondaryKeyMatchNone, " +
        "SecondaryKeyHeadersJson, " +
        "RequestTimeUtcTicks, " +
        "ResponseTimeUtcTicks, " +
        "ResponseDateUtcTicks, " +
        "AgeValueTicks, " +
        "MaxAgeTicks, " +
        "SharedMaxAgeTicks, " +
        "ExpiresUtcTicks, " +
        "MustRevalidate, " +
        "ProxyRevalidate, " +
        "ResponseNoCache, " +
        "Public, " +
        "Private, " +
        "NoTransform, " +
        "Immutable, " +
        "StaleIfErrorTicks, " +
        "ETag, " +
        "LastModifiedUtcTicks, " +
        "SerializedResponse, " +
        "Payload";

    private readonly Lock _initializationLock = new();
    private readonly string _connectionString;
    private readonly bool _usesInMemoryDatabase;
    private SqliteConnection? _memoryDatabaseAnchorConnection;
    private Task? _initializationTask;
    private bool _initialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteHttpCachePersistenceProvider"/> class.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public SqliteHttpCachePersistenceProvider(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var builder = new SqliteConnectionStringBuilder(connectionString);
        _usesInMemoryDatabase = IsInMemoryDatabase(builder);
        _connectionString = builder.ToString();
    }

    private static bool IsInMemoryDatabase(SqliteConnectionStringBuilder builder)
    {
        return builder.Mode is SqliteOpenMode.Memory || string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT " + SplitColumnsSql + " FROM HttpCacheEntries WHERE PrimaryKey = $primaryKey";
        command.Parameters.AddWithValue("$primaryKey", primaryKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var ordinals = EntryColumnOrdinals.Create(reader);
        var entries = new List<HttpCachePersistenceEntry>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var entry = TryReadEntry(reader, ordinals);
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

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var secondaryKey = ComputeSecondaryKey(entry);
        var secondaryKeyHeadersJson = SerializeSecondaryKeyHeaders(entry.SecondaryKeyHeaders);
        var staleAtUtcTicks = ComputeStaleAtUtcTicks(entry);
        var deleteWhenExpired = ShouldDeleteWhenExpired(entry) ? 1 : 0;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO HttpCacheEntries(" +
            "PrimaryKey, SecondaryKey, " +
            "SecondaryKeyMatchNone, SecondaryKeyHeadersJson, " +
            "RequestTimeUtcTicks, ResponseTimeUtcTicks, ResponseDateUtcTicks, " +
            "AgeValueTicks, MaxAgeTicks, SharedMaxAgeTicks, ExpiresUtcTicks, " +
            "MustRevalidate, ProxyRevalidate, ResponseNoCache, Public, Private, NoTransform, Immutable, " +
            "StaleIfErrorTicks, ETag, LastModifiedUtcTicks, SerializedResponse, " +
            "StaleAtUtcTicks, DeleteWhenExpired, Payload) " +
            "VALUES (" +
            "$primaryKey, $secondaryKey, " +
            "$secondaryKeyMatchNone, $secondaryKeyHeadersJson, " +
            "$requestTimeUtcTicks, $responseTimeUtcTicks, $responseDateUtcTicks, " +
            "$ageValueTicks, $maxAgeTicks, $sharedMaxAgeTicks, $expiresUtcTicks, " +
            "$mustRevalidate, $proxyRevalidate, $responseNoCache, $public, $private, $noTransform, $immutable, " +
            "$staleIfErrorTicks, $etag, $lastModifiedUtcTicks, $serializedResponse, " +
            "$staleAtUtcTicks, $deleteWhenExpired, NULL) " +
            "ON CONFLICT(PrimaryKey, SecondaryKey) DO UPDATE SET " +
            "SecondaryKeyMatchNone = excluded.SecondaryKeyMatchNone, " +
            "SecondaryKeyHeadersJson = excluded.SecondaryKeyHeadersJson, " +
            "RequestTimeUtcTicks = excluded.RequestTimeUtcTicks, " +
            "ResponseTimeUtcTicks = excluded.ResponseTimeUtcTicks, " +
            "ResponseDateUtcTicks = excluded.ResponseDateUtcTicks, " +
            "AgeValueTicks = excluded.AgeValueTicks, " +
            "MaxAgeTicks = excluded.MaxAgeTicks, " +
            "SharedMaxAgeTicks = excluded.SharedMaxAgeTicks, " +
            "ExpiresUtcTicks = excluded.ExpiresUtcTicks, " +
            "MustRevalidate = excluded.MustRevalidate, " +
            "ProxyRevalidate = excluded.ProxyRevalidate, " +
            "ResponseNoCache = excluded.ResponseNoCache, " +
            "Public = excluded.Public, " +
            "Private = excluded.Private, " +
            "NoTransform = excluded.NoTransform, " +
            "Immutable = excluded.Immutable, " +
            "StaleIfErrorTicks = excluded.StaleIfErrorTicks, " +
            "ETag = excluded.ETag, " +
            "LastModifiedUtcTicks = excluded.LastModifiedUtcTicks, " +
            "SerializedResponse = excluded.SerializedResponse, " +
            "StaleAtUtcTicks = excluded.StaleAtUtcTicks, " +
            "DeleteWhenExpired = excluded.DeleteWhenExpired, " +
            "Payload = NULL";
        command.Parameters.AddWithValue("$primaryKey", primaryKey);
        command.Parameters.AddWithValue("$secondaryKey", secondaryKey);
        command.Parameters.AddWithValue("$secondaryKeyMatchNone", entry.SecondaryKeyMatchNone ? 1 : 0);
        command.Parameters.AddWithValue("$secondaryKeyHeadersJson", (object?)secondaryKeyHeadersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$requestTimeUtcTicks", entry.RequestTime.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$responseTimeUtcTicks", entry.ResponseTime.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$responseDateUtcTicks", entry.ResponseDate.UtcDateTime.Ticks);
        command.Parameters.AddWithValue("$ageValueTicks", entry.AgeValue.Ticks);
        command.Parameters.AddWithValue("$maxAgeTicks", entry.MaxAge?.Ticks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$sharedMaxAgeTicks", entry.SharedMaxAge?.Ticks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$expiresUtcTicks", entry.Expires?.UtcDateTime.Ticks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$mustRevalidate", entry.MustRevalidate ? 1 : 0);
        command.Parameters.AddWithValue("$proxyRevalidate", entry.ProxyRevalidate ? 1 : 0);
        command.Parameters.AddWithValue("$responseNoCache", entry.ResponseNoCache ? 1 : 0);
        command.Parameters.AddWithValue("$public", entry.Public ? 1 : 0);
        command.Parameters.AddWithValue("$private", entry.Private ? 1 : 0);
        command.Parameters.AddWithValue("$noTransform", entry.NoTransform ? 1 : 0);
        command.Parameters.AddWithValue("$immutable", entry.Immutable ? 1 : 0);
        command.Parameters.AddWithValue("$staleIfErrorTicks", entry.StaleIfError?.Ticks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$etag", (object?)entry.ETag ?? DBNull.Value);
        command.Parameters.AddWithValue("$lastModifiedUtcTicks", entry.LastModified?.UtcDateTime.Ticks ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$serializedResponse", entry.SerializedResponse.ToArray());
        command.Parameters.AddWithValue("$staleAtUtcTicks", staleAtUtcTicks);
        command.Parameters.AddWithValue("$deleteWhenExpired", deleteWhenExpired);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM HttpCacheEntries WHERE PrimaryKey = $primaryKey";
        command.Parameters.AddWithValue("$primaryKey", primaryKey);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
        var nowUtcTicks = now.UtcDateTime.Ticks;

        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var fastDeleteCommand = connection.CreateCommand())
        {
            fastDeleteCommand.Transaction = transaction;
            fastDeleteCommand.CommandText =
                "DELETE FROM HttpCacheEntries " +
                "WHERE DeleteWhenExpired = 1 " +
                "AND StaleAtUtcTicks IS NOT NULL " +
                "AND StaleAtUtcTicks <= $nowUtcTicks";
            fastDeleteCommand.Parameters.AddWithValue("$nowUtcTicks", nowUtcTicks);
            await fastDeleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var rowsToDelete = new List<long>();
        var rowsToUpdate = new List<(long RowId, long StaleAtUtcTicks, int DeleteWhenExpired)>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                "SELECT rowid, " + SplitColumnsSql + " FROM HttpCacheEntries " +
                "WHERE DeleteWhenExpired IS NULL OR StaleAtUtcTicks IS NULL";

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var ordinals = EntryColumnOrdinals.Create(reader);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var rowId = reader.GetInt64(0);
                var entry = TryReadEntry(reader, ordinals);
                if (entry is null)
                {
                    rowsToDelete.Add(rowId);
                    continue;
                }

                var staleAtUtcTicks = ComputeStaleAtUtcTicks(entry);
                var deleteWhenExpired = ShouldDeleteWhenExpired(entry) ? 1 : 0;
                if (deleteWhenExpired is 1 && staleAtUtcTicks <= nowUtcTicks)
                {
                    rowsToDelete.Add(rowId);
                }
                else
                {
                    rowsToUpdate.Add((rowId, staleAtUtcTicks, deleteWhenExpired));
                }
            }
        }

        if (rowsToDelete.Count is 0 && rowsToUpdate.Count is 0)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (rowsToDelete.Count > 0)
        {
            await using var deleteCommand = connection.CreateCommand();
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM HttpCacheEntries WHERE rowid = $rowId";
            var rowIdParameter = deleteCommand.Parameters.Add("$rowId", SqliteType.Integer);

            foreach (var rowId in rowsToDelete)
            {
                rowIdParameter.Value = rowId;
                await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (rowsToUpdate.Count > 0)
        {
            await using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                "UPDATE HttpCacheEntries " +
                "SET StaleAtUtcTicks = $staleAtUtcTicks, DeleteWhenExpired = $deleteWhenExpired, Payload = NULL " +
                "WHERE rowid = $rowId";
            var rowIdParameter = updateCommand.Parameters.Add("$rowId", SqliteType.Integer);
            var staleAtUtcTicksParameter = updateCommand.Parameters.Add("$staleAtUtcTicks", SqliteType.Integer);
            var deleteWhenExpiredParameter = updateCommand.Parameters.Add("$deleteWhenExpired", SqliteType.Integer);

            foreach (var row in rowsToUpdate)
            {
                rowIdParameter.Value = row.RowId;
                staleAtUtcTicksParameter.Value = row.StaleAtUtcTicks;
                deleteWhenExpiredParameter.Value = row.DeleteWhenExpired;
                await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        Task initializationTask;
        lock (_initializationLock)
        {
            if (_initialized)
                return;

            initializationTask = _initializationTask ??= EnsureInitializedCoreAsync();
        }

        try
        {
            await initializationTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!initializationTask.IsFaulted && !initializationTask.IsCanceled)
            {
                throw;
            }

            lock (_initializationLock)
            {
                if (!_initialized && ReferenceEquals(_initializationTask, initializationTask))
                {
                    _initializationTask = null;
                }
            }

            throw;
        }
    }

    private async Task EnsureInitializedCoreAsync()
    {
        if (_initialized)
            return;

        if (_usesInMemoryDatabase && _memoryDatabaseAnchorConnection is null)
        {
            _memoryDatabaseAnchorConnection = new SqliteConnection(_connectionString);
            await _memoryDatabaseAnchorConnection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await using var connection = await OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS HttpCacheEntries (" +
            "PrimaryKey TEXT NOT NULL, " +
            "SecondaryKey TEXT NOT NULL, " +
            "SecondaryKeyMatchNone INTEGER NOT NULL, " +
            "SecondaryKeyHeadersJson TEXT NULL, " +
            "RequestTimeUtcTicks INTEGER NOT NULL, " +
            "ResponseTimeUtcTicks INTEGER NOT NULL, " +
            "ResponseDateUtcTicks INTEGER NOT NULL, " +
            "AgeValueTicks INTEGER NOT NULL, " +
            "MaxAgeTicks INTEGER NULL, " +
            "SharedMaxAgeTicks INTEGER NULL, " +
            "ExpiresUtcTicks INTEGER NULL, " +
            "MustRevalidate INTEGER NOT NULL, " +
            "ProxyRevalidate INTEGER NOT NULL, " +
            "ResponseNoCache INTEGER NOT NULL, " +
            "Public INTEGER NOT NULL, " +
            "Private INTEGER NOT NULL, " +
            "NoTransform INTEGER NOT NULL, " +
            "Immutable INTEGER NOT NULL, " +
            "StaleIfErrorTicks INTEGER NULL, " +
            "ETag TEXT NULL, " +
            "LastModifiedUtcTicks INTEGER NULL, " +
            "SerializedResponse BLOB NOT NULL, " +
            "StaleAtUtcTicks INTEGER NULL, " +
            "DeleteWhenExpired INTEGER NULL, " +
            "Payload BLOB NULL, " +
            "PRIMARY KEY(PrimaryKey, SecondaryKey))";
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        await EnsureSchemaColumnsAsync(connection, CancellationToken.None).ConfigureAwait(false);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            "CREATE INDEX IF NOT EXISTS IX_HttpCacheEntries_Cleanup " +
            "ON HttpCacheEntries(DeleteWhenExpired, StaleAtUtcTicks)";
        await indexCommand.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        _initialized = true;
    }

    private static async ValueTask EnsureSchemaColumnsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA table_info(HttpCacheEntries)";

            await using var reader = await pragmaCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        if (!existingColumns.Contains("SecondaryKeyMatchNone"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN SecondaryKeyMatchNone INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("SecondaryKeyHeadersJson"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN SecondaryKeyHeadersJson TEXT NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("RequestTimeUtcTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN RequestTimeUtcTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("ResponseTimeUtcTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN ResponseTimeUtcTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("ResponseDateUtcTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN ResponseDateUtcTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("AgeValueTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN AgeValueTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("MaxAgeTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN MaxAgeTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("SharedMaxAgeTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN SharedMaxAgeTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("ExpiresUtcTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN ExpiresUtcTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("MustRevalidate"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN MustRevalidate INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("ProxyRevalidate"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN ProxyRevalidate INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("ResponseNoCache"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN ResponseNoCache INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("Public"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN Public INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("Private"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN Private INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("NoTransform"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN NoTransform INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("Immutable"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN Immutable INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("StaleIfErrorTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN StaleIfErrorTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("ETag"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN ETag TEXT NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("LastModifiedUtcTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN LastModifiedUtcTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("SerializedResponse"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN SerializedResponse BLOB NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("Payload"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN Payload BLOB NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("StaleAtUtcTicks"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN StaleAtUtcTicks INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!existingColumns.Contains("DeleteWhenExpired"))
        {
            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN DeleteWhenExpired INTEGER NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
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

    private static string? SerializeSecondaryKeyHeaders(Dictionary<string, string>? headers)
    {
        if (headers is null || headers.Count is 0)
            return null;

        return JsonSerializer.Serialize(headers, SqliteSerializationContext.Default.DictionaryStringString);
    }

    private static Dictionary<string, string>? DeserializeSecondaryKeyHeaders(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var headers = JsonSerializer.Deserialize(json, SqliteSerializationContext.Default.DictionaryStringString);
        if (headers is null || headers.Count is 0)
            return null;

        return new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
    }

    private static HttpCachePersistenceEntry? TryReadEntry(SqliteDataReader reader, EntryColumnOrdinals ordinals)
    {
        var entry = TryReadSplitEntry(reader, ordinals);
        if (entry is not null)
            return entry;

        if (ordinals.Payload < 0 || reader.IsDBNull(ordinals.Payload))
            return null;

        try
        {
            var payload = reader.GetFieldValue<byte[]>(ordinals.Payload);
            return JsonSerializer.Deserialize(payload, SqliteSerializationContext.Default.HttpCachePersistenceEntry);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static HttpCachePersistenceEntry? TryReadSplitEntry(SqliteDataReader reader, EntryColumnOrdinals ordinals)
    {
        if (ordinals.SerializedResponse < 0 || reader.IsDBNull(ordinals.SerializedResponse))
            return null;

        try
        {
            var headersJson = reader.IsDBNull(ordinals.SecondaryKeyHeadersJson) ? null : reader.GetString(ordinals.SecondaryKeyHeadersJson);
            return new HttpCachePersistenceEntry
            {
                SecondaryKeyMatchNone = ReadBoolean(reader, ordinals.SecondaryKeyMatchNone),
                SecondaryKeyHeaders = DeserializeSecondaryKeyHeaders(headersJson),
                RequestTime = FromUtcTicks(reader.GetInt64(ordinals.RequestTimeUtcTicks)),
                ResponseTime = FromUtcTicks(reader.GetInt64(ordinals.ResponseTimeUtcTicks)),
                ResponseDate = FromUtcTicks(reader.GetInt64(ordinals.ResponseDateUtcTicks)),
                AgeValue = TimeSpan.FromTicks(reader.GetInt64(ordinals.AgeValueTicks)),
                MaxAge = ReadNullableTimeSpan(reader, ordinals.MaxAgeTicks),
                SharedMaxAge = ReadNullableTimeSpan(reader, ordinals.SharedMaxAgeTicks),
                Expires = ReadNullableDateTimeOffset(reader, ordinals.ExpiresUtcTicks),
                MustRevalidate = ReadBoolean(reader, ordinals.MustRevalidate),
                ProxyRevalidate = ReadBoolean(reader, ordinals.ProxyRevalidate),
                ResponseNoCache = ReadBoolean(reader, ordinals.ResponseNoCache),
                Public = ReadBoolean(reader, ordinals.Public),
                Private = ReadBoolean(reader, ordinals.Private),
                NoTransform = ReadBoolean(reader, ordinals.NoTransform),
                Immutable = ReadBoolean(reader, ordinals.Immutable),
                StaleIfError = ReadNullableTimeSpan(reader, ordinals.StaleIfErrorTicks),
                ETag = reader.IsDBNull(ordinals.ETag) ? null : reader.GetString(ordinals.ETag),
                LastModified = ReadNullableDateTimeOffset(reader, ordinals.LastModifiedUtcTicks),
                SerializedResponse = reader.GetFieldValue<byte[]>(ordinals.SerializedResponse),
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
            return false;

        var value = reader.GetValue(ordinal);
        return value switch
        {
            bool boolean => boolean,
            byte byteValue => byteValue != 0,
            short shortValue => shortValue != 0,
            int intValue => intValue != 0,
            long longValue => longValue != 0,
            string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedValue) => parsedValue != 0,
            _ => Convert.ToInt64(value, CultureInfo.InvariantCulture) != 0,
        };
    }

    private static TimeSpan? ReadNullableTimeSpan(SqliteDataReader reader, int ordinal)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
            return null;

        return TimeSpan.FromTicks(reader.GetInt64(ordinal));
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
            return null;

        return FromUtcTicks(reader.GetInt64(ordinal));
    }

    private static DateTimeOffset FromUtcTicks(long ticks)
    {
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly struct EntryColumnOrdinals
    {
        public int SecondaryKeyMatchNone { get; init; }
        public int SecondaryKeyHeadersJson { get; init; }
        public int RequestTimeUtcTicks { get; init; }
        public int ResponseTimeUtcTicks { get; init; }
        public int ResponseDateUtcTicks { get; init; }
        public int AgeValueTicks { get; init; }
        public int MaxAgeTicks { get; init; }
        public int SharedMaxAgeTicks { get; init; }
        public int ExpiresUtcTicks { get; init; }
        public int MustRevalidate { get; init; }
        public int ProxyRevalidate { get; init; }
        public int ResponseNoCache { get; init; }
        public int Public { get; init; }
        public int Private { get; init; }
        public int NoTransform { get; init; }
        public int Immutable { get; init; }
        public int StaleIfErrorTicks { get; init; }
        public int ETag { get; init; }
        public int LastModifiedUtcTicks { get; init; }
        public int SerializedResponse { get; init; }
        public int Payload { get; init; }

        public static EntryColumnOrdinals Create(SqliteDataReader reader)
        {
            return new EntryColumnOrdinals
            {
                SecondaryKeyMatchNone = GetOrdinal(reader, "SecondaryKeyMatchNone"),
                SecondaryKeyHeadersJson = GetOrdinal(reader, "SecondaryKeyHeadersJson"),
                RequestTimeUtcTicks = GetOrdinal(reader, "RequestTimeUtcTicks"),
                ResponseTimeUtcTicks = GetOrdinal(reader, "ResponseTimeUtcTicks"),
                ResponseDateUtcTicks = GetOrdinal(reader, "ResponseDateUtcTicks"),
                AgeValueTicks = GetOrdinal(reader, "AgeValueTicks"),
                MaxAgeTicks = GetOrdinal(reader, "MaxAgeTicks"),
                SharedMaxAgeTicks = GetOrdinal(reader, "SharedMaxAgeTicks"),
                ExpiresUtcTicks = GetOrdinal(reader, "ExpiresUtcTicks"),
                MustRevalidate = GetOrdinal(reader, "MustRevalidate"),
                ProxyRevalidate = GetOrdinal(reader, "ProxyRevalidate"),
                ResponseNoCache = GetOrdinal(reader, "ResponseNoCache"),
                Public = GetOrdinal(reader, "Public"),
                Private = GetOrdinal(reader, "Private"),
                NoTransform = GetOrdinal(reader, "NoTransform"),
                Immutable = GetOrdinal(reader, "Immutable"),
                StaleIfErrorTicks = GetOrdinal(reader, "StaleIfErrorTicks"),
                ETag = GetOrdinal(reader, "ETag"),
                LastModifiedUtcTicks = GetOrdinal(reader, "LastModifiedUtcTicks"),
                SerializedResponse = GetOrdinal(reader, "SerializedResponse"),
                Payload = GetOrdinal(reader, "Payload"),
            };
        }

        private static int GetOrdinal(SqliteDataReader reader, string columnName)
        {
            for (var i = 0; i < reader.FieldCount; i++)
            {
                if (string.Equals(reader.GetName(i), columnName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }
    }

    private static bool ShouldDeleteWhenExpired(HttpCachePersistenceEntry entry)
    {
        var cannotBeUsedStale = entry.MustRevalidate || entry.ProxyRevalidate || entry.ResponseNoCache;
        if (!cannotBeUsedStale)
            return false;

        return string.IsNullOrEmpty(entry.ETag) && entry.LastModified is null;
    }

    private static long ComputeStaleAtUtcTicks(HttpCachePersistenceEntry entry)
    {
        var correctedInitialAge = CalculateCorrectedInitialAge(entry);
        var freshnessLifetime = GetFreshnessLifetime(entry);

        long ticks;
        try
        {
            ticks = checked(entry.ResponseTime.UtcDateTime.Ticks + freshnessLifetime.Ticks - correctedInitialAge.Ticks);
        }
        catch (OverflowException)
        {
            ticks = freshnessLifetime >= correctedInitialAge ? DateTime.MaxValue.Ticks : DateTime.MinValue.Ticks;
        }

        if (ticks < DateTime.MinValue.Ticks)
            return DateTime.MinValue.Ticks;

        if (ticks > DateTime.MaxValue.Ticks)
            return DateTime.MaxValue.Ticks;

        return ticks;
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

    /// <inheritdoc />
    public void Dispose()
    {
        _memoryDatabaseAnchorConnection?.Dispose();
        _memoryDatabaseAnchorConnection = null;
    }
}
