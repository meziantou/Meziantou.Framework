using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.Http.Caching.Sqlite;

/// <summary>
/// Stores HTTP cache entries in a SQLite database.
/// </summary>
public sealed class SqliteHttpCacheStore : IHttpCacheStore, IDisposable
{
    private const string SplitColumnsSql =
        "SecondaryKeyMatchNone, " +
        "SecondaryKeyHeadersJson, " +
        "NoVarySearch, " +
        "NormalizedQuery, " +
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

    // The size of an entry. A row written by a version that only had the Payload column has no SerializedResponse.
    private const string EntrySizeSql = "COALESCE(length(SerializedResponse), length(Payload), 0)";

    // The access counter is the largest one of the table plus one, which the index on LastAccess answers directly. A
    // counter rather than a time keeps the order exact when several entries are written within the resolution of the clock.
    private const string NextLastAccessSql = "(SELECT COALESCE(MAX(LastAccess), 0) + 1 FROM HttpCacheEntries)";

    private readonly Lock _initializationLock = new();
    private readonly string _connectionString;
    private readonly long? _maximumEntryCount;
    private readonly long? _maximumTotalSize;
    private Task? _initializationTask;
    private SqliteConnection? _anchorConnection;
    private volatile bool _initialized;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteHttpCacheStore"/> class with no limit.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    public SqliteHttpCacheStore(string connectionString)
        : this(connectionString, limits: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteHttpCacheStore"/> class that evicts the least recently used
    /// entries to stay within <paramref name="limits"/>.
    /// </summary>
    /// <param name="connectionString">The SQLite connection string.</param>
    /// <param name="limits">The bounds of the store, or <see langword="null"/> for no limit. The values are read once, by the constructor.</param>
    public SqliteHttpCacheStore(string connectionString, HttpCacheStoreLimits? limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        _connectionString = new SqliteConnectionStringBuilder(connectionString).ToString();
        _maximumEntryCount = limits?.MaximumEntryCount;
        _maximumTotalSize = limits?.MaximumTotalSize;
    }

    private bool IsBounded => _maximumEntryCount is not null || _maximumTotalSize is not null;

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

        var entries = new List<HttpCachePersistenceEntry>();
        var hasRows = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            var ordinals = EntryColumnOrdinals.Create(reader);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                hasRows = true;
                var entry = TryReadEntry(reader, ordinals);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        // Only a bounded store needs the recency of the entries, so an unbounded one does not pay for a write on every read
        if (hasRows && IsBounded)
        {
            await using var touchCommand = connection.CreateCommand();
            touchCommand.CommandText = "UPDATE HttpCacheEntries SET LastAccess = " + NextLastAccessSql + " WHERE PrimaryKey = $primaryKey";
            touchCommand.Parameters.AddWithValue("$primaryKey", primaryKey);
            await touchCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        var secondaryKey = entry.ComputeSecondaryKeyHash();
        var secondaryKeyHeadersJson = SerializeSecondaryKeyHeaders(entry.SecondaryKeyHeaders);
        var staleAtUtcTicks = entry.GetStaleTime().UtcDateTime.Ticks;
        var deleteWhenExpired = entry.IsUnusableWhenStale ? 1 : 0;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // A bounded store writes the entry and evicts the others in one transaction, so that the limits hold for the
        // other connections too
        await using var transaction = IsBounded ? (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO HttpCacheEntries(" +
            "PrimaryKey, SecondaryKey, " +
            "SecondaryKeyMatchNone, SecondaryKeyHeadersJson, NoVarySearch, NormalizedQuery, " +
            "RequestTimeUtcTicks, ResponseTimeUtcTicks, ResponseDateUtcTicks, " +
            "AgeValueTicks, MaxAgeTicks, SharedMaxAgeTicks, ExpiresUtcTicks, " +
            "MustRevalidate, ProxyRevalidate, ResponseNoCache, Public, Private, NoTransform, Immutable, " +
            "StaleIfErrorTicks, ETag, LastModifiedUtcTicks, SerializedResponse, " +
            "StaleAtUtcTicks, DeleteWhenExpired, Payload, LastAccess) " +
            "VALUES (" +
            "$primaryKey, $secondaryKey, " +
            "$secondaryKeyMatchNone, $secondaryKeyHeadersJson, $noVarySearch, $normalizedQuery, " +
            "$requestTimeUtcTicks, $responseTimeUtcTicks, $responseDateUtcTicks, " +
            "$ageValueTicks, $maxAgeTicks, $sharedMaxAgeTicks, $expiresUtcTicks, " +
            "$mustRevalidate, $proxyRevalidate, $responseNoCache, $public, $private, $noTransform, $immutable, " +
            "$staleIfErrorTicks, $etag, $lastModifiedUtcTicks, $serializedResponse, " +
            "$staleAtUtcTicks, $deleteWhenExpired, NULL, " + NextLastAccessSql + ") " +
            "ON CONFLICT(PrimaryKey, SecondaryKey) DO UPDATE SET " +
            "SecondaryKeyMatchNone = excluded.SecondaryKeyMatchNone, " +
            "SecondaryKeyHeadersJson = excluded.SecondaryKeyHeadersJson, " +
            "NoVarySearch = excluded.NoVarySearch, " +
            "NormalizedQuery = excluded.NormalizedQuery, " +
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
            "Payload = NULL, " +
            "LastAccess = excluded.LastAccess";
        command.Parameters.AddWithValue("$primaryKey", primaryKey);
        command.Parameters.AddWithValue("$secondaryKey", secondaryKey);
        command.Parameters.AddWithValue("$secondaryKeyMatchNone", entry.SecondaryKeyMatchNone ? 1 : 0);
        command.Parameters.AddWithValue("$secondaryKeyHeadersJson", (object?)secondaryKeyHeadersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$noVarySearch", (object?)entry.NoVarySearch ?? DBNull.Value);
        command.Parameters.AddWithValue("$normalizedQuery", (object?)entry.NormalizedQuery ?? DBNull.Value);
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

        if (transaction is not null)
        {
            await EvictAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Removes the least recently used entries until the store is within its limits.</summary>
    private async ValueTask EvictAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        long entryCount;
        long totalSize;
        await using (var statisticsCommand = connection.CreateCommand())
        {
            statisticsCommand.Transaction = transaction;
            statisticsCommand.CommandText = "SELECT EntryCount, TotalSize FROM HttpCacheStatistics WHERE Id = 0";
            await using var reader = await statisticsCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return;

            entryCount = reader.GetInt64(0);
            totalSize = reader.GetInt64(1);
        }

        var excessCount = _maximumEntryCount is { } maximumEntryCount ? entryCount - maximumEntryCount : 0;
        var excessSize = _maximumTotalSize is { } maximumTotalSize ? totalSize - maximumTotalSize : 0;
        if (excessCount <= 0 && excessSize <= 0)
            return;

        // The entry that was just written is the most recently used, so it is evicted last: only when it alone exceeds the limits.
        // The rows without LastAccess, written by a version that did not track it, come first.
        var rowsToDelete = new List<long>();
        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = "SELECT rowid, " + EntrySizeSql + " FROM HttpCacheEntries ORDER BY LastAccess, rowid";
            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while ((excessCount > 0 || excessSize > 0) && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                rowsToDelete.Add(reader.GetInt64(0));
                excessCount--;
                excessSize -= reader.GetInt64(1);
            }
        }

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

                var staleAtUtcTicks = entry.GetStaleTime().UtcDateTime.Ticks;
                var deleteWhenExpired = entry.IsUnusableWhenStale ? 1 : 0;
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
        ObjectDisposedException.ThrowIf(_disposed, this);

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

        // One connection is kept open for the lifetime of the store. A SQLite in-memory database exists only
        // while a connection to it is open, so without this the caller has to hold one itself just to keep
        // the cache from vanishing between operations. It also gives Dispose something to release: the
        // per-operation connections below are returned to the pool as soon as they are used.
        var anchorConnection = await OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            _anchorConnection = anchorConnection;
        }
        catch
        {
            await anchorConnection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await using var connection = await OpenConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS HttpCacheEntries (" +
            "PrimaryKey TEXT NOT NULL, " +
            "SecondaryKey TEXT NOT NULL, " +
            "SecondaryKeyMatchNone INTEGER NOT NULL, " +
            "SecondaryKeyHeadersJson TEXT NULL, " +
            "NoVarySearch TEXT NULL, " +
            "NormalizedQuery TEXT NULL, " +
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
            "LastAccess INTEGER NULL, " +
            "PRIMARY KEY(PrimaryKey, SecondaryKey))";
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        await EnsureSchemaColumnsAsync(connection, CancellationToken.None).ConfigureAwait(false);

        await using var indexCommand = connection.CreateCommand();
        indexCommand.CommandText =
            "CREATE INDEX IF NOT EXISTS IX_HttpCacheEntries_Cleanup " +
            "ON HttpCacheEntries(DeleteWhenExpired, StaleAtUtcTicks); " +
            "CREATE INDEX IF NOT EXISTS IX_HttpCacheEntries_LastAccess " +
            "ON HttpCacheEntries(LastAccess)";
        await indexCommand.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);

        await EnsureStatisticsAsync(connection, CancellationToken.None).ConfigureAwait(false);

        _initialized = true;
    }

    // The number of entries and their total size, kept up to date by triggers so that a bounded store does not scan the
    // table on every write. The triggers are part of the database, so they also count the rows written by an unbounded
    // store or by a version of the store that does not know them. The table is seeded and the triggers are created in
    // one transaction, so no row written by another connection in between is missed.
    private static async ValueTask EnsureStatisticsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS HttpCacheStatistics (" +
            "Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 0), " +
            "EntryCount INTEGER NOT NULL, " +
            "TotalSize INTEGER NOT NULL); " +

            // The WHERE clause skips the scan of the table once the row exists
            "INSERT OR IGNORE INTO HttpCacheStatistics (Id, EntryCount, TotalSize) " +
            "SELECT 0, COUNT(*), COALESCE(SUM(" + EntrySizeSql + "), 0) FROM HttpCacheEntries " +
            "WHERE NOT EXISTS (SELECT 1 FROM HttpCacheStatistics); " +

            "CREATE TRIGGER IF NOT EXISTS TR_HttpCacheEntries_Insert AFTER INSERT ON HttpCacheEntries BEGIN " +
            "UPDATE HttpCacheStatistics SET EntryCount = EntryCount + 1, " +
            "TotalSize = TotalSize + COALESCE(length(NEW.SerializedResponse), length(NEW.Payload), 0) WHERE Id = 0; END; " +

            "CREATE TRIGGER IF NOT EXISTS TR_HttpCacheEntries_Delete AFTER DELETE ON HttpCacheEntries BEGIN " +
            "UPDATE HttpCacheStatistics SET EntryCount = EntryCount - 1, " +
            "TotalSize = TotalSize - COALESCE(length(OLD.SerializedResponse), length(OLD.Payload), 0) WHERE Id = 0; END; " +

            "CREATE TRIGGER IF NOT EXISTS TR_HttpCacheEntries_Update AFTER UPDATE OF SerializedResponse, Payload ON HttpCacheEntries BEGIN " +
            "UPDATE HttpCacheStatistics SET " +
            "TotalSize = TotalSize - COALESCE(length(OLD.SerializedResponse), length(OLD.Payload), 0) " +
            "+ COALESCE(length(NEW.SerializedResponse), length(NEW.Payload), 0) WHERE Id = 0; END;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    // Columns added to databases created by an earlier version. The names are compile-time constants, so
    // building the ALTER TABLE statement from them is not an injection point.
    private static readonly (string Name, string Type)[] MigratedColumns =
    [
        ("SecondaryKeyMatchNone", "INTEGER"),
        ("SecondaryKeyHeadersJson", "TEXT"),
        ("NoVarySearch", "TEXT"),
        ("NormalizedQuery", "TEXT"),
        ("RequestTimeUtcTicks", "INTEGER"),
        ("ResponseTimeUtcTicks", "INTEGER"),
        ("ResponseDateUtcTicks", "INTEGER"),
        ("AgeValueTicks", "INTEGER"),
        ("MaxAgeTicks", "INTEGER"),
        ("SharedMaxAgeTicks", "INTEGER"),
        ("ExpiresUtcTicks", "INTEGER"),
        ("MustRevalidate", "INTEGER"),
        ("ProxyRevalidate", "INTEGER"),
        ("ResponseNoCache", "INTEGER"),
        ("Public", "INTEGER"),
        ("Private", "INTEGER"),
        ("NoTransform", "INTEGER"),
        ("Immutable", "INTEGER"),
        ("StaleIfErrorTicks", "INTEGER"),
        ("ETag", "TEXT"),
        ("LastModifiedUtcTicks", "INTEGER"),
        ("SerializedResponse", "BLOB"),
        ("Payload", "BLOB"),
        ("StaleAtUtcTicks", "INTEGER"),
        ("DeleteWhenExpired", "INTEGER"),
        ("LastAccess", "INTEGER"),
    ];

    [SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "The column names and types come from MigratedColumns, which holds compile-time constants")]
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

        foreach (var (name, type) in MigratedColumns)
        {
            if (existingColumns.Contains(name))
                continue;

            await using var addColumnCommand = connection.CreateCommand();
            addColumnCommand.CommandText = "ALTER TABLE HttpCacheEntries ADD COLUMN " + name + " " + type + " NULL";
            await addColumnCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
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
                NoVarySearch = ReadNullableString(reader, ordinals.NoVarySearch),
                NormalizedQuery = ReadNullableString(reader, ordinals.NormalizedQuery),
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

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        if (ordinal < 0 || reader.IsDBNull(ordinal))
            return null;

        return reader.GetString(ordinal);
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
        public int NoVarySearch { get; init; }
        public int NormalizedQuery { get; init; }
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
                NoVarySearch = GetOrdinal(reader, "NoVarySearch"),
                NormalizedQuery = GetOrdinal(reader, "NormalizedQuery"),
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

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        SqliteConnection? anchorConnection;
        lock (_initializationLock)
        {
            anchorConnection = _anchorConnection;
            _anchorConnection = null;
        }

        anchorConnection?.Dispose();
    }
}
