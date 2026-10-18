namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The storage of the IndexedDB databases (https://w3c.github.io/IndexedDB/) of the browsing contexts, e.g. a SQLite
/// database or <see cref="InMemoryIndexedDBBackend"/> (see <see cref="BrowsingContextOptions.IndexedDB"/>). The backend
/// stores the schemas and the records of the databases of each origin, and runs the queries of the library: the library
/// implements the API (the requests, the events, the transaction scheduling, the key generators, the key paths and the
/// indexes) and only reads and writes bytes.
/// </summary>
/// <remarks>
/// <para>
/// The keys and the values are opaque byte arrays. The keys are encoded so that their byte order
/// (<see cref="MemoryExtensions.SequenceCompareTo{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>, i.e. <c>memcmp</c>) is the order of
/// the IndexedDB keys (https://w3c.github.io/IndexedDB/#compare-two-keys): a backend sorts and compares the keys as byte
/// strings (e.g. the BLOB columns of SQLite), and never decodes them. The values are the serialized JavaScript values,
/// including the bytes of their blobs and files.
/// </para>
/// <para>
/// The data of an origin is changed in transactions (<see cref="BeginTransaction"/>) that the library commits or aborts.
/// The library guarantees that:
/// </para>
/// <list type="bullet">
/// <item>the calls for a database (of an origin) are never concurrent (but the calls for different databases can be);</item>
/// <item>at most one <see cref="IdbTransactionMode.ReadWrite"/> or <see cref="IdbTransactionMode.VersionChange"/> transaction of a database is open at a time;</item>
/// <item>the <see cref="IdbTransactionMode.ReadOnly"/> transactions that are open at the same time as a read-write transaction do not read its object stores;</item>
/// <item>no transaction of a database is open while it is deleted.</item>
/// </list>
/// <para>
/// So a backend can run a read-write transaction as a transaction of its storage (e.g. <c>BEGIN</c>, <c>COMMIT</c> and
/// <c>ROLLBACK</c> on a SQLite connection), and the read-only transactions without isolation. The methods are called on the
/// threads of the event loops of the windows and the workers that use the databases: an implementation must be
/// thread-safe, and its exceptions make the requests fail with an <c>UnknownError</c> (or the transaction abort).
/// </para>
/// <para>
/// The database names, the object store names and the index names are arbitrary strings (possibly empty, with lone
/// surrogates). The object stores and the indexes are identified by ids assigned by the library, which are unique in a
/// database and never reused while the database exists.
/// </para>
/// </remarks>
public abstract class IndexedDBBackend
{
    internal IndexedDBCoordinator Coordinator => field ??= new IndexedDBCoordinator(this);

    /// <summary>Returns the name and the version of the databases of an origin (https://w3c.github.io/IndexedDB/#dom-idbfactory-databases).</summary>
    /// <param name="origin">The serialization of the origin (e.g. <c>https://example.com</c>).</param>
    public abstract IReadOnlyList<IndexedDBDatabaseInfo> GetDatabases(string origin);

    /// <summary>Returns the schema of a database, or null when the database does not exist.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the database.</param>
    public abstract IndexedDBDatabaseSchema? GetDatabase(string origin, string name);

    /// <summary>Deletes a database and its data (https://w3c.github.io/IndexedDB/#delete-a-database). Does nothing when it does not exist.</summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the database.</param>
    public abstract void DeleteDatabase(string origin, string name);

    /// <summary>
    /// Begins a transaction on a database. A <see cref="IdbTransactionMode.VersionChange"/> transaction creates the
    /// database (with the version 0 and no object store) when it does not exist; aborting this transaction deletes it.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    /// <param name="name">The name of the database.</param>
    /// <param name="mode">The mode of the transaction.</param>
    /// <param name="objectStoreIds">The ids of the object stores of the scope of the transaction (all the object stores for a version change transaction).</param>
    public abstract IndexedDBBackendTransaction BeginTransaction(string origin, string name, IdbTransactionMode mode, IReadOnlyList<long> objectStoreIds);

    /// <summary>
    /// Returns an estimation of the number of bytes used by the databases of an origin (<c>navigator.storage.estimate()</c>).
    /// The default is 0: the usage is not reported.
    /// </summary>
    /// <param name="origin">The serialization of the origin.</param>
    public virtual long GetUsage(string origin) => 0;
}

/// <summary>
/// A transaction of an <see cref="IndexedDBBackend"/>: the reads see the writes of the transaction, the writes are applied
/// by <see cref="Commit"/>, and <see cref="Abort"/> reverts all the changes (including the changes of the schema, the
/// version and the key generators). The library calls exactly one of them, then <see cref="IDisposable.Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// The key ranges and the order are the byte order of the encoded keys. The schema methods are only called in a
/// <see cref="IdbTransactionMode.VersionChange"/> transaction, and the write methods in a
/// <see cref="IdbTransactionMode.ReadWrite"/> or <see cref="IdbTransactionMode.VersionChange"/> transaction.
/// </para>
/// <para>
/// The library computes the keys of the indexes (including the <c>multiEntry</c> indexes) and checks the uniqueness
/// constraints: an index record is a key of the index and the primary key of a record of its object store, and the index
/// records are ordered by key, then by primary key. The records of an index are removed with the record of the object store.
/// </para>
/// </remarks>
public abstract class IndexedDBBackendTransaction : IDisposable
{
    /// <summary>Sets the version of the database (a version change transaction).</summary>
    public abstract void SetVersion(long version);

    /// <summary>Creates an object store (a version change transaction). Its key generator's current number is 1.</summary>
    public abstract void CreateObjectStore(IndexedDBObjectStoreSchema objectStore);

    /// <summary>Deletes an object store, its records and its indexes (a version change transaction).</summary>
    public abstract void DeleteObjectStore(long objectStoreId);

    /// <summary>Renames an object store (a version change transaction).</summary>
    public abstract void RenameObjectStore(long objectStoreId, string name);

    /// <summary>Creates an empty index of an object store (a version change transaction). The library then adds its records with <see cref="PutIndexRecords"/>.</summary>
    public abstract void CreateIndex(long objectStoreId, IndexedDBIndexSchema index);

    /// <summary>Deletes an index and its records (a version change transaction).</summary>
    public abstract void DeleteIndex(long indexId);

    /// <summary>Renames an index (a version change transaction).</summary>
    public abstract void RenameIndex(long indexId, string name);

    /// <summary>Returns the current number of the key generator of an object store (https://w3c.github.io/IndexedDB/#key-generator-current-number).</summary>
    public abstract long GetKeyGeneratorCurrentNumber(long objectStoreId);

    /// <summary>Sets the current number of the key generator of an object store.</summary>
    public abstract void SetKeyGeneratorCurrentNumber(long objectStoreId, long value);

    /// <summary>Returns the records of an object store in a key range, in the order of the keys (or the reverse order), at most <paramref name="limit"/> records.</summary>
    public abstract IReadOnlyList<IndexedDBRecord> GetRecords(long objectStoreId, IndexedDBKeyRange range, bool reverse, int limit);

    /// <summary>Returns the number of records of an object store in a key range.</summary>
    public abstract long CountRecords(long objectStoreId, IndexedDBKeyRange range);

    /// <summary>
    /// Stores a record in an object store: it replaces the record with the same key, if any, and its index records. The index
    /// records of the new record are <paramref name="indexKeys"/> (with the key of the record as primary key).
    /// </summary>
    public abstract void PutRecord(long objectStoreId, byte[] key, byte[] value, IReadOnlyList<IndexedDBIndexKey> indexKeys);

    /// <summary>Deletes the records of an object store in a key range, and their index records.</summary>
    public abstract void DeleteRecords(long objectStoreId, IndexedDBKeyRange range);

    /// <summary>Deletes all the records of an object store, and their index records.</summary>
    public abstract void ClearObjectStore(long objectStoreId);

    /// <summary>
    /// Returns the records of an index whose key is in a key range, ordered by key then by primary key (or the reverse
    /// order), at most <paramref name="limit"/> records. When <paramref name="start"/> is given, only the records at or
    /// after this position in this order are returned: the records whose key is greater (in the reverse order, lower), or
    /// equal with a greater (lower) primary key, or equal with the same primary key when the position is inclusive.
    /// </summary>
    public abstract IReadOnlyList<IndexedDBIndexRecord> GetIndexRecords(long indexId, IndexedDBKeyRange range, bool reverse, IndexedDBIndexPosition? start, int limit);

    /// <summary>Returns the number of records of an index whose key is in a key range.</summary>
    public abstract long CountIndexRecords(long indexId, IndexedDBKeyRange range);

    /// <summary>Adds records to an index (the records of a new index, computed from the existing records of its object store).</summary>
    public abstract void PutIndexRecords(long indexId, IReadOnlyList<IndexedDBIndexRecord> records);

    /// <summary>Applies the changes of the transaction. An exception aborts the transaction (the changes must then be reverted).</summary>
    public abstract void Commit();

    /// <summary>Reverts the changes of the transaction.</summary>
    public abstract void Abort();

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the resources of the transaction.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }
}

/// <summary>The name and the version of a database.</summary>
/// <param name="Name">The name of the database.</param>
/// <param name="Version">The version of the database.</param>
public readonly record struct IndexedDBDatabaseInfo(string Name, long Version);

/// <summary>The schema of a database: its version, its object stores and their indexes.</summary>
public sealed class IndexedDBDatabaseSchema
{
    /// <summary>Gets the name of the database.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the version of the database.</summary>
    public required long Version { get; init; }

    /// <summary>Gets the object stores of the database.</summary>
    public IReadOnlyList<IndexedDBObjectStoreSchema> ObjectStores { get; init; } = [];
}

/// <summary>The schema of an object store (https://w3c.github.io/IndexedDB/#object-store-construct).</summary>
public sealed class IndexedDBObjectStoreSchema
{
    /// <summary>Gets the id of the object store, unique in the database.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the name of the object store.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the key path of the object store, or null when the object store uses out-of-line keys.</summary>
    public IndexedDBKeyPath? KeyPath { get; init; }

    /// <summary>Gets whether the object store has a key generator.</summary>
    public bool AutoIncrement { get; init; }

    /// <summary>Gets the indexes of the object store.</summary>
    public IReadOnlyList<IndexedDBIndexSchema> Indexes { get; init; } = [];
}

/// <summary>The schema of an index (https://w3c.github.io/IndexedDB/#index-construct).</summary>
public sealed class IndexedDBIndexSchema
{
    /// <summary>Gets the id of the index, unique in the database.</summary>
    public required long Id { get; init; }

    /// <summary>Gets the name of the index.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the key path of the index.</summary>
    public required IndexedDBKeyPath KeyPath { get; init; }

    /// <summary>Gets whether the keys of the index are unique.</summary>
    public bool Unique { get; init; }

    /// <summary>Gets whether an array key adds a record per item.</summary>
    public bool MultiEntry { get; init; }
}

/// <summary>
/// A key path (https://w3c.github.io/IndexedDB/#key-path-construct): a string (identifiers separated by periods, or the
/// empty string) or a list of strings.
/// </summary>
public sealed class IndexedDBKeyPath : IEquatable<IndexedDBKeyPath>
{
    private IndexedDBKeyPath(string? path, IReadOnlyList<string>? paths)
    {
        Path = path;
        Paths = paths;
    }

    /// <summary>Gets the string key path, or null when the key path is a list.</summary>
    public string? Path { get; }

    /// <summary>Gets the strings of a list key path, or null when the key path is a string.</summary>
    public IReadOnlyList<string>? Paths { get; }

    /// <summary>Gets whether the key path is a list.</summary>
    public bool IsList => Paths is not null;

    /// <summary>Creates a string key path.</summary>
    public static IndexedDBKeyPath FromString(string path) => new(path ?? throw new ArgumentNullException(nameof(path)), paths: null);

    /// <summary>Creates a list key path.</summary>
    public static IndexedDBKeyPath FromList(IEnumerable<string> paths) => new(path: null, [.. paths ?? throw new ArgumentNullException(nameof(paths))]);

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] IndexedDBKeyPath? other) => other is not null && Path == other.Path && (Paths is null ? other.Paths is null : other.Paths is not null && Paths.SequenceEqual(other.Paths));

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as IndexedDBKeyPath);

    /// <inheritdoc/>
    public override int GetHashCode() => Path?.GetHashCode(StringComparison.Ordinal) ?? Paths!.Count;

    /// <summary>Returns the string key path, or the strings of the list separated by commas in brackets.</summary>
    public override string ToString() => Path ?? "[" + string.Join(",", Paths!) + "]";
}

#pragma warning disable CA1819 // The keys and the values are byte arrays, given as is to the backends (e.g. the BLOB parameters of SQLite)
/// <summary>
/// A range of encoded keys (https://w3c.github.io/IndexedDB/#range-construct). A null bound is unbounded, and an open
/// bound is excluded from the range.
/// </summary>
/// <param name="Lower">The lower bound, or null.</param>
/// <param name="Upper">The upper bound, or null.</param>
/// <param name="LowerOpen">Whether the lower bound is excluded.</param>
/// <param name="UpperOpen">Whether the upper bound is excluded.</param>
public sealed record IndexedDBKeyRange(byte[]? Lower, byte[]? Upper, bool LowerOpen = false, bool UpperOpen = false)
{
    /// <summary>Gets the range of all the keys.</summary>
    public static IndexedDBKeyRange All { get; } = new(Lower: null, Upper: null);

    /// <summary>Creates the range that contains only one key.</summary>
    public static IndexedDBKeyRange Only(byte[] key) => new(key, key);

    /// <summary>Returns whether an encoded key is in the range.</summary>
    public bool Contains(ReadOnlySpan<byte> key)
    {
        if (Lower is not null)
        {
            var comparison = key.SequenceCompareTo(Lower);
            if (comparison < 0 || (comparison == 0 && LowerOpen))
                return false;
        }

        if (Upper is not null)
        {
            var comparison = key.SequenceCompareTo(Upper);
            if (comparison > 0 || (comparison == 0 && UpperOpen))
                return false;
        }

        return true;
    }
}

/// <summary>A record of an object store: its encoded key and its serialized value.</summary>
/// <param name="Key">The encoded key.</param>
/// <param name="Value">The serialized value.</param>
public readonly record struct IndexedDBRecord(byte[] Key, byte[] Value);

/// <summary>A record of an index: its encoded key and the encoded key of the record of the object store (the primary key).</summary>
/// <param name="Key">The encoded key of the index.</param>
/// <param name="PrimaryKey">The encoded key of the record of the object store.</param>
public readonly record struct IndexedDBIndexRecord(byte[] Key, byte[] PrimaryKey);

/// <summary>A position in the records of an index (see <see cref="IndexedDBBackendTransaction.GetIndexRecords"/>).</summary>
/// <param name="Key">The encoded key of the index.</param>
/// <param name="PrimaryKey">The encoded primary key.</param>
/// <param name="Inclusive">Whether the record at the position is included.</param>
public readonly record struct IndexedDBIndexPosition(byte[] Key, byte[] PrimaryKey, bool Inclusive);

/// <summary>A key of an index for a record that is stored (see <see cref="IndexedDBBackendTransaction.PutRecord"/>).</summary>
/// <param name="IndexId">The id of the index.</param>
/// <param name="Key">The encoded key of the index.</param>
public readonly record struct IndexedDBIndexKey(long IndexId, byte[] Key);
#pragma warning restore CA1819
