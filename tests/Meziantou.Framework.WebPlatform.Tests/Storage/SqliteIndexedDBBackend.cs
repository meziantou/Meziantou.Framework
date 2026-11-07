using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.Data.Sqlite;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>
/// An <see cref="IndexedDBBackend"/> that stores the databases in SQLite files: a catalog of the databases, and a file per
/// database, so that the read/write transactions of different databases can be open at the same time (the library opens
/// at most one read/write transaction per database). The encoded keys are BLOB columns: SQLite compares them with
/// <c>memcmp</c>, which is the order of the IndexedDB keys. The names are stored as UTF-16 BLOBs (they can contain lone
/// surrogates, which a TEXT column cannot store).
/// </summary>
internal sealed class SqliteIndexedDBBackend : IndexedDBBackend, IDisposable
{
    private readonly string _directory;
    private readonly Lock _lock = new();
    private readonly SqliteConnection _catalog;
    private readonly Dictionary<string, DatabaseConnection> _connections = new(StringComparer.Ordinal);

    public SqliteIndexedDBBackend(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
        _catalog = Open(Path.Combine(directory, "catalog.db"));
        Execute(_catalog, null, "CREATE TABLE IF NOT EXISTS databases (origin BLOB NOT NULL, name BLOB NOT NULL, file TEXT NOT NULL, PRIMARY KEY (origin, name))");
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var connection in _connections.Values)
            {
                connection.Connection.Dispose();
            }

            _connections.Clear();
            _catalog.Dispose();
        }
    }

    public override IReadOnlyList<IndexedDBDatabaseInfo> GetDatabases(string origin)
    {
        var result = new List<IndexedDBDatabaseInfo>();
        List<(string Name, string File)> databases;
        lock (_lock)
        {
            databases = Query(_catalog, null, "SELECT name, file FROM databases WHERE origin = @origin", reader => (FromBlob(reader, 0), reader.GetString(1)), ("@origin", ToBlob(origin)));
        }

        foreach (var (name, file) in databases)
        {
            var database = GetConnection(file);
            lock (database.Lock)
            {
                result.Add(new IndexedDBDatabaseInfo(name, ReadVersion(database)));
            }
        }

        return result;
    }

    public override IndexedDBDatabaseSchema? GetDatabase(string origin, string name)
    {
        if (FindFile(origin, name) is not { } file)
            return null;

        var database = GetConnection(file);
        lock (database.Lock)
        {
            var connection = database.Connection;
            var indexes = Query(connection, database.CurrentTransaction, "SELECT id, store_id, name, key_path, is_unique, multi_entry FROM indexes ORDER BY id", reader => (StoreId: reader.GetInt64(1), Index: new IndexedDBIndexSchema
            {
                Id = reader.GetInt64(0),
                Name = FromBlob(reader, 2),
                KeyPath = ParseKeyPath(reader.GetString(3))!,
                Unique = reader.GetBoolean(4),
                MultiEntry = reader.GetBoolean(5),
            }));
            var stores = Query(connection, database.CurrentTransaction, "SELECT id, name, key_path, auto_increment FROM object_stores ORDER BY id", reader => new IndexedDBObjectStoreSchema
            {
                Id = reader.GetInt64(0),
                Name = FromBlob(reader, 1),
                KeyPath = reader.IsDBNull(2) ? null : ParseKeyPath(reader.GetString(2)),
                AutoIncrement = reader.GetBoolean(3),
                Indexes = [.. indexes.Where(index => index.StoreId == reader.GetInt64(0)).Select(index => index.Index)],
            });
            return new IndexedDBDatabaseSchema { Name = name, Version = ReadVersion(database), ObjectStores = stores };
        }
    }

    public override void DeleteDatabase(string origin, string name)
    {
        lock (_lock)
        {
            if (FindFile(origin, name) is not { } file)
                return;

            Execute(_catalog, null, "DELETE FROM databases WHERE origin = @origin AND name = @name", ("@origin", ToBlob(origin)), ("@name", ToBlob(name)));
            DeleteFile(file);
        }
    }

    public override IndexedDBBackendTransaction BeginTransaction(string origin, string name, IdbTransactionMode mode, IReadOnlyList<long> objectStoreIds)
    {
        var created = false;
        string? file;
        lock (_lock)
        {
            file = FindFile(origin, name);
            if (file is null)
            {
                if (mode != IdbTransactionMode.VersionChange)
                    throw new InvalidOperationException($"The database '{name}' does not exist");

                file = Guid.NewGuid().ToString("N") + ".db";
                Execute(_catalog, null, "INSERT INTO databases (origin, name, file) VALUES (@origin, @name, @file)", ("@origin", ToBlob(origin)), ("@name", ToBlob(name)), ("@file", file));
                created = true;
            }
        }

        var database = GetConnection(file);
        lock (database.Lock)
        {
            // The read-only transactions read the committed data (the library guarantees that no read/write transaction
            // writes their object stores while they are open), the other ones are transactions of SQLite
            var transaction = mode == IdbTransactionMode.ReadOnly ? null : database.Connection.BeginTransaction();
            if (transaction is not null)
            {
                database.CurrentTransaction = transaction;
            }

            return new Transaction(this, database, transaction, created ? (origin, name, file) : null);
        }
    }

    private string? FindFile(string origin, string name)
    {
        lock (_lock)
        {
            return Query(_catalog, null, "SELECT file FROM databases WHERE origin = @origin AND name = @name", reader => reader.GetString(0), ("@origin", ToBlob(origin)), ("@name", ToBlob(name))).FirstOrDefault();
        }
    }

    private DatabaseConnection GetConnection(string file)
    {
        lock (_lock)
        {
            if (!_connections.TryGetValue(file, out var database))
            {
#pragma warning disable CA2000 // The connection is disposed with the backend, or when the database is deleted
                var connection = Open(Path.Combine(_directory, file));
#pragma warning restore CA2000
                Execute(connection, null, """
                    CREATE TABLE IF NOT EXISTS meta (version INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS object_stores (id INTEGER PRIMARY KEY, name BLOB NOT NULL, key_path TEXT NULL, auto_increment INTEGER NOT NULL, current_number INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS indexes (id INTEGER PRIMARY KEY, store_id INTEGER NOT NULL, name BLOB NOT NULL, key_path TEXT NOT NULL, is_unique INTEGER NOT NULL, multi_entry INTEGER NOT NULL);
                    CREATE TABLE IF NOT EXISTS records (store_id INTEGER NOT NULL, key BLOB NOT NULL, value BLOB NOT NULL, PRIMARY KEY (store_id, key)) WITHOUT ROWID;
                    CREATE TABLE IF NOT EXISTS index_records (index_id INTEGER NOT NULL, key BLOB NOT NULL, primary_key BLOB NOT NULL, PRIMARY KEY (index_id, key, primary_key)) WITHOUT ROWID;
                    CREATE INDEX IF NOT EXISTS index_records_by_primary_key ON index_records (index_id, primary_key);
                    INSERT INTO meta (version) SELECT 0 WHERE NOT EXISTS (SELECT 1 FROM meta);
                    """);
                database = new DatabaseConnection(connection);
                _connections.Add(file, database);
            }

            return database;
        }
    }

    private void DeleteFile(string file)
    {
        if (_connections.Remove(file, out var database))
        {
            database.Connection.Dispose();
        }

        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(_directory, file));
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static long ReadVersion(DatabaseConnection database) => Query(database.Connection, database.CurrentTransaction, "SELECT version FROM meta", reader => reader.GetInt64(0))[0];

    private static string? FormatKeyPath(IndexedDBKeyPath? keyPath) => keyPath is null ? null : keyPath.IsList ? JsonSerializer.Serialize(keyPath.Paths) : JsonSerializer.Serialize(keyPath.Path);

    private static IndexedDBKeyPath? ParseKeyPath(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? IndexedDBKeyPath.FromList([.. document.RootElement.EnumerateArray().Select(item => item.GetString()!)])
            : IndexedDBKeyPath.FromString(document.RootElement.GetString()!);
    }

    // The UTF-16 code units, little-endian (Encoding.Unicode would replace the lone surrogates)
    internal static byte[] ToBlob(string value)
    {
        var bytes = new byte[value.Length * 2];
        for (var i = 0; i < value.Length; i++)
        {
            bytes[2 * i] = (byte)value[i];
            bytes[(2 * i) + 1] = (byte)(value[i] >> 8);
        }

        return bytes;
    }

    private static string FromBlob(SqliteDataReader reader, int ordinal)
    {
        var bytes = (byte[])reader.GetValue(ordinal);
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(bytes[2 * i] | (bytes[(2 * i) + 1] << 8));
        }

        return new string(chars);
    }

    internal static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        command.ExecuteNonQuery();
    }

    internal static List<T> Query<T>(SqliteConnection connection, SqliteTransaction? transaction, string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        var result = new List<T>();
        while (reader.Read())
        {
            result.Add(read(reader));
        }

        return result;
    }

    private static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction? transaction, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
#pragma warning disable CA2100 // The SQL is built from constants; the values are parameters
        command.CommandText = sql;
#pragma warning restore CA2100
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    // The conditions of a key range on a column
    private static string RangeCondition(IndexedDBKeyRange range, string column, List<(string, object?)> parameters)
    {
        var condition = new System.Text.StringBuilder();
        if (range.Lower is { } lower)
        {
            condition.Append(" AND ").Append(column).Append(range.LowerOpen ? " > " : " >= ").Append("@lower");
            parameters.Add(("@lower", lower));
        }

        if (range.Upper is { } upper)
        {
            condition.Append(" AND ").Append(column).Append(range.UpperOpen ? " < " : " <= ").Append("@upper");
            parameters.Add(("@upper", upper));
        }

        return condition.ToString();
    }

    private sealed class DatabaseConnection(SqliteConnection connection)
    {
        public SqliteConnection Connection { get; } = connection;

        public Lock Lock { get; } = new();

        // The open read/write transaction: the commands of the connection must be enlisted in it
        public SqliteTransaction? CurrentTransaction { get; set; }
    }

    private sealed class Transaction(SqliteIndexedDBBackend backend, DatabaseConnection database, SqliteTransaction? transaction, (string Origin, string Name, string File)? created) : IndexedDBBackendTransaction
    {
        private void Execute(string sql, params (string Name, object? Value)[] parameters)
        {
            lock (database.Lock)
            {
                SqliteIndexedDBBackend.Execute(database.Connection, transaction ?? database.CurrentTransaction, sql, parameters);
            }
        }

        private List<T> Query<T>(string sql, Func<SqliteDataReader, T> read, params (string Name, object? Value)[] parameters)
        {
            lock (database.Lock)
            {
                return SqliteIndexedDBBackend.Query(database.Connection, transaction ?? database.CurrentTransaction, sql, read, parameters);
            }
        }

        private void EndTransaction()
        {
            if (transaction is not null && ReferenceEquals(database.CurrentTransaction, transaction))
            {
                database.CurrentTransaction = null;
            }
        }

        public override void SetVersion(long version) => Execute("UPDATE meta SET version = @version", ("@version", version));

        public override void CreateObjectStore(IndexedDBObjectStoreSchema objectStore)
            => Execute("INSERT INTO object_stores (id, name, key_path, auto_increment, current_number) VALUES (@id, @name, @keyPath, @autoIncrement, 1)",
                ("@id", objectStore.Id), ("@name", ToBlob(objectStore.Name)), ("@keyPath", FormatKeyPath(objectStore.KeyPath)), ("@autoIncrement", objectStore.AutoIncrement));

        public override void DeleteObjectStore(long objectStoreId)
        {
            Execute("DELETE FROM index_records WHERE index_id IN (SELECT id FROM indexes WHERE store_id = @id)", ("@id", objectStoreId));
            Execute("DELETE FROM indexes WHERE store_id = @id", ("@id", objectStoreId));
            Execute("DELETE FROM records WHERE store_id = @id", ("@id", objectStoreId));
            Execute("DELETE FROM object_stores WHERE id = @id", ("@id", objectStoreId));
        }

        public override void RenameObjectStore(long objectStoreId, string name) => Execute("UPDATE object_stores SET name = @name WHERE id = @id", ("@id", objectStoreId), ("@name", ToBlob(name)));

        public override void CreateIndex(long objectStoreId, IndexedDBIndexSchema index)
            => Execute("INSERT INTO indexes (id, store_id, name, key_path, is_unique, multi_entry) VALUES (@id, @storeId, @name, @keyPath, @unique, @multiEntry)",
                ("@id", index.Id), ("@storeId", objectStoreId), ("@name", ToBlob(index.Name)), ("@keyPath", FormatKeyPath(index.KeyPath)), ("@unique", index.Unique), ("@multiEntry", index.MultiEntry));

        public override void DeleteIndex(long indexId)
        {
            Execute("DELETE FROM index_records WHERE index_id = @id", ("@id", indexId));
            Execute("DELETE FROM indexes WHERE id = @id", ("@id", indexId));
        }

        public override void RenameIndex(long indexId, string name) => Execute("UPDATE indexes SET name = @name WHERE id = @id", ("@id", indexId), ("@name", ToBlob(name)));

        public override long GetKeyGeneratorCurrentNumber(long objectStoreId) => Query("SELECT current_number FROM object_stores WHERE id = @id", reader => reader.GetInt64(0), ("@id", objectStoreId))[0];

        public override void SetKeyGeneratorCurrentNumber(long objectStoreId, long value) => Execute("UPDATE object_stores SET current_number = @value WHERE id = @id", ("@id", objectStoreId), ("@value", value));

        public override IReadOnlyList<IndexedDBRecord> GetRecords(long objectStoreId, IndexedDBKeyRange range, bool reverse, int limit)
        {
            var parameters = new List<(string, object?)> { ("@id", objectStoreId), ("@limit", limit) };
            var condition = RangeCondition(range, "key", parameters);
            return Query($"SELECT key, value FROM records WHERE store_id = @id{condition} ORDER BY key {(reverse ? "DESC" : "ASC")} LIMIT @limit", reader => new IndexedDBRecord((byte[])reader.GetValue(0), (byte[])reader.GetValue(1)), [.. parameters]);
        }

        public override long CountRecords(long objectStoreId, IndexedDBKeyRange range)
        {
            var parameters = new List<(string, object?)> { ("@id", objectStoreId) };
            var condition = RangeCondition(range, "key", parameters);
            return Query($"SELECT COUNT(*) FROM records WHERE store_id = @id{condition}", reader => reader.GetInt64(0), [.. parameters])[0];
        }

        public override void PutRecord(long objectStoreId, byte[] key, byte[] value, IReadOnlyList<IndexedDBIndexKey> indexKeys)
        {
            Execute("DELETE FROM index_records WHERE index_id IN (SELECT id FROM indexes WHERE store_id = @id) AND primary_key = @key", ("@id", objectStoreId), ("@key", key));
            Execute("INSERT OR REPLACE INTO records (store_id, key, value) VALUES (@id, @key, @value)", ("@id", objectStoreId), ("@key", key), ("@value", value));
            foreach (var indexKey in indexKeys)
            {
                Execute("INSERT OR IGNORE INTO index_records (index_id, key, primary_key) VALUES (@index, @indexKey, @key)", ("@index", indexKey.IndexId), ("@indexKey", indexKey.Key), ("@key", key));
            }
        }

        public override void DeleteRecords(long objectStoreId, IndexedDBKeyRange range)
        {
            var parameters = new List<(string, object?)> { ("@id", objectStoreId) };
            var condition = RangeCondition(range, "primary_key", parameters);
            Execute($"DELETE FROM index_records WHERE index_id IN (SELECT id FROM indexes WHERE store_id = @id){condition}", [.. parameters]);
            Execute($"DELETE FROM records WHERE store_id = @id{condition.Replace("primary_key", "key", StringComparison.Ordinal)}", [.. parameters]);
        }

        public override void ClearObjectStore(long objectStoreId) => DeleteRecords(objectStoreId, IndexedDBKeyRange.All);

        public override IReadOnlyList<IndexedDBIndexRecord> GetIndexRecords(long indexId, IndexedDBKeyRange range, bool reverse, IndexedDBIndexPosition? start, int limit)
        {
            var parameters = new List<(string, object?)> { ("@id", indexId), ("@limit", limit) };
            var condition = RangeCondition(range, "key", parameters);
            if (start is { } position)
            {
                var keyComparison = reverse ? "<" : ">";
                var primaryKeyComparison = keyComparison + (position.Inclusive ? "=" : "");
                condition += $" AND (key {keyComparison} @startKey OR (key = @startKey AND primary_key {primaryKeyComparison} @startPrimaryKey))";
                parameters.Add(("@startKey", position.Key));
                parameters.Add(("@startPrimaryKey", position.PrimaryKey));
            }

            var order = reverse ? "DESC" : "ASC";
            return Query($"SELECT key, primary_key FROM index_records WHERE index_id = @id{condition} ORDER BY key {order}, primary_key {order} LIMIT @limit", reader => new IndexedDBIndexRecord((byte[])reader.GetValue(0), (byte[])reader.GetValue(1)), [.. parameters]);
        }

        public override long CountIndexRecords(long indexId, IndexedDBKeyRange range)
        {
            var parameters = new List<(string, object?)> { ("@id", indexId) };
            var condition = RangeCondition(range, "key", parameters);
            return Query($"SELECT COUNT(*) FROM index_records WHERE index_id = @id{condition}", reader => reader.GetInt64(0), [.. parameters])[0];
        }

        public override void PutIndexRecords(long indexId, IReadOnlyList<IndexedDBIndexRecord> records)
        {
            foreach (var record in records)
            {
                Execute("INSERT OR IGNORE INTO index_records (index_id, key, primary_key) VALUES (@index, @key, @primaryKey)", ("@index", indexId), ("@key", record.Key), ("@primaryKey", record.PrimaryKey));
            }
        }

        public override void Commit()
        {
            lock (database.Lock)
            {
                transaction?.Commit();
                EndTransaction();
            }
        }

        public override void Abort()
        {
            lock (database.Lock)
            {
                transaction?.Rollback();
                EndTransaction();
            }

            // The database created by the version change transaction is deleted
            if (created is var (origin, name, file))
            {
                lock (backend._lock)
                {
                    SqliteIndexedDBBackend.Execute(backend._catalog, null, "DELETE FROM databases WHERE origin = @origin AND name = @name", ("@origin", ToBlob(origin)), ("@name", ToBlob(name)));
                    backend.DeleteFile(file);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (database.Lock)
                {
                    EndTransaction();
                    transaction?.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
