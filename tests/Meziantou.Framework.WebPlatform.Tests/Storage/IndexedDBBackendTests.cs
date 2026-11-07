using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Storage;

/// <summary>The contract of <see cref="IndexedDBBackend"/>, checked for each implementation.</summary>
public abstract class IndexedDBBackendTests : IDisposable
{
    private const string Origin = "https://example.com";

    private IndexedDBBackend? _backend;

    protected IndexedDBBackend Backend => _backend ??= CreateBackend();

    /// <summary>Creates a backend on the storage of the test.</summary>
    protected abstract IndexedDBBackend CreateBackend();

    /// <summary>Releases the backend (e.g. closes its files).</summary>
    protected virtual void DisposeBackend(IndexedDBBackend backend)
    {
    }

    /// <summary>Returns a backend that reads the storage of <see cref="Backend"/> (a new instance for a persistent storage).</summary>
    protected virtual IndexedDBBackend Reopen()
    {
        DisposeBackend(Backend);
        _backend = CreateBackend();
        return _backend;
    }

    public void Dispose()
    {
        if (_backend is not null)
        {
            DisposeBackend(_backend);
        }

        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private static byte[] Key(object value) => (value switch
    {
        double number => IdbKey.FromNumber(number),
        int number => IdbKey.FromNumber(number),
        string text => IdbKey.FromString(text),
        DateTimeOffset date => IdbKey.FromDate(date.ToUnixTimeMilliseconds()),
        byte[] bytes => IdbKey.FromBinary(bytes),
        object[] items => IdbKey.FromArray([.. items.Select(item => IdbKey.Decode(Key(item)))]),
        _ => throw new ArgumentException("Unsupported key", nameof(value)),
    }).Encode();

    private static string Show(byte[] key) => IdbKey.Decode(key).ToString();

    private IndexedDBBackendTransaction CreateDatabase(string name = "db")
    {
        var transaction = Backend.BeginTransaction(Origin, name, IdbTransactionMode.VersionChange, []);
        transaction.SetVersion(1);
        transaction.CreateObjectStore(new IndexedDBObjectStoreSchema { Id = 1, Name = "store", KeyPath = IndexedDBKeyPath.FromString("id"), AutoIncrement = true });
        transaction.CreateIndex(1, new IndexedDBIndexSchema { Id = 2, Name = "index", KeyPath = IndexedDBKeyPath.FromList(["a", "b"]), Unique = false });
        return transaction;
    }

    [Fact]
    public void Records_AreOrderedAsTheKeys()
    {
        using (var transaction = CreateDatabase())
        {
            object[] keys = [new object[] { 1 }, "b", -1.5, new byte[] { 0, 1 }, double.NegativeInfinity, "a\0b", "a", new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), new byte[] { 0 }, 3, "", new object[] { "x", 1 }, new object[] { 1, 2 }];
            foreach (var key in keys)
            {
                transaction.PutRecord(1, Key(key), [1], []);
            }

            transaction.Commit();
        }

        using var read = Backend.BeginTransaction(Origin, "db", IdbTransactionMode.ReadOnly, [1]);
        var records = read.GetRecords(1, IndexedDBKeyRange.All, reverse: false, int.MaxValue);
        Assert.Equal(
            ["-Infinity", "-1.5", "3", "Date(1577836800000)", "\"\"", "\"a\"", "\"a\0b\"", "\"b\"", "Binary(00)", "Binary(0001)", "[1]", "[1, 2]", "[\"x\", 1]"],
            records.Select(record => Show(record.Key)));
        Assert.Equal(13, read.CountRecords(1, IndexedDBKeyRange.All));
        read.Commit();
    }

    [Fact]
    public void GetRecords_RangeReverseAndLimit()
    {
        using (var transaction = CreateDatabase())
        {
            for (var i = 1; i <= 10; i++)
            {
                transaction.PutRecord(1, Key(i), [(byte)i], []);
            }

            transaction.Commit();
        }

        using var read = Backend.BeginTransaction(Origin, "db", IdbTransactionMode.ReadOnly, [1]);
        var range = new IndexedDBKeyRange(Key(3), Key(8), LowerOpen: true, UpperOpen: false);
        Assert.Equal(["4", "5", "6", "7", "8"], read.GetRecords(1, range, reverse: false, int.MaxValue).Select(record => Show(record.Key)));
        Assert.Equal(["8", "7"], read.GetRecords(1, range, reverse: true, limit: 2).Select(record => Show(record.Key)));
        Assert.Equal(5, read.CountRecords(1, range));
        Assert.Equal([5], read.GetRecords(1, IndexedDBKeyRange.Only(Key(5)), reverse: false, limit: 1)[0].Value);
        read.Commit();
    }

    [Fact]
    public void IndexRecords_AreOrderedByKeyThenPrimaryKey()
    {
        using (var transaction = CreateDatabase())
        {
            transaction.PutRecord(1, Key(3), [3], [new IndexedDBIndexKey(2, Key("b"))]);
            transaction.PutRecord(1, Key(1), [1], [new IndexedDBIndexKey(2, Key("b"))]);
            transaction.PutRecord(1, Key(2), [2], [new IndexedDBIndexKey(2, Key("a"))]);
            transaction.PutRecord(1, Key(4), [4], [new IndexedDBIndexKey(2, Key("c"))]);
            transaction.Commit();
        }

        using var read = Backend.BeginTransaction(Origin, "db", IdbTransactionMode.ReadOnly, [1]);
        string Records(IReadOnlyList<IndexedDBIndexRecord> records) => string.Join(" ", records.Select(record => Show(record.Key) + ":" + Show(record.PrimaryKey)));
        Assert.Equal("\"a\":2 \"b\":1 \"b\":3 \"c\":4", Records(read.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: false, start: null, int.MaxValue)));
        Assert.Equal("\"c\":4 \"b\":3 \"b\":1 \"a\":2", Records(read.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: true, start: null, int.MaxValue)));
        Assert.Equal("\"b\":3 \"c\":4", Records(read.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: false, new IndexedDBIndexPosition(Key("b"), Key(1), Inclusive: false), int.MaxValue)));
        Assert.Equal("\"b\":1 \"b\":3", Records(read.GetIndexRecords(2, new IndexedDBKeyRange(null, Key("b")), reverse: false, new IndexedDBIndexPosition(Key("b"), Key(1), Inclusive: true), int.MaxValue)));
        Assert.Equal("\"b\":1 \"a\":2", Records(read.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: true, new IndexedDBIndexPosition(Key("b"), Key(3), Inclusive: false), int.MaxValue)));
        Assert.Equal(2, read.CountIndexRecords(2, IndexedDBKeyRange.Only(Key("b"))));
        read.Commit();
    }

    [Fact]
    public void PutAndDelete_ReplaceAndRemoveTheIndexRecords()
    {
        using var transaction = CreateDatabase();
        transaction.PutRecord(1, Key(1), [1], [new IndexedDBIndexKey(2, Key("a"))]);
        transaction.PutRecord(1, Key(1), [2], [new IndexedDBIndexKey(2, Key("b"))]);
        transaction.PutRecord(1, Key(2), [3], [new IndexedDBIndexKey(2, Key("c"))]);
        Assert.Equal(["\"b\"", "\"c\""], transaction.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: false, start: null, int.MaxValue).Select(record => Show(record.Key)));

        transaction.DeleteRecords(1, IndexedDBKeyRange.Only(Key(1)));
        Assert.Equal(["\"c\""], transaction.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: false, start: null, int.MaxValue).Select(record => Show(record.Key)));
        Assert.Equal(["2"], transaction.GetRecords(1, IndexedDBKeyRange.All, reverse: false, int.MaxValue).Select(record => Show(record.Key)));

        transaction.ClearObjectStore(1);
        Assert.Equal(0, transaction.CountRecords(1, IndexedDBKeyRange.All));
        Assert.Equal(0, transaction.CountIndexRecords(2, IndexedDBKeyRange.All));
        transaction.Commit();
    }

    [Fact]
    public void Abort_RevertsTheChanges()
    {
        using (var transaction = CreateDatabase())
        {
            transaction.PutRecord(1, Key(1), [1], [new IndexedDBIndexKey(2, Key("a"))]);
            transaction.SetKeyGeneratorCurrentNumber(1, 2);
            transaction.Commit();
        }

        using (var transaction = Backend.BeginTransaction(Origin, "db", IdbTransactionMode.VersionChange, [1]))
        {
            transaction.SetVersion(2);
            transaction.RenameObjectStore(1, "renamed");
            transaction.CreateObjectStore(new IndexedDBObjectStoreSchema { Id = 3, Name = "other" });
            transaction.DeleteIndex(2);
            transaction.PutRecord(1, Key(2), [2], []);
            transaction.DeleteRecords(1, IndexedDBKeyRange.Only(Key(1)));
            transaction.SetKeyGeneratorCurrentNumber(1, 10);
            transaction.Abort();
        }

        var schema = Backend.GetDatabase(Origin, "db")!;
        Assert.Equal(1, schema.Version);
        var store = Assert.Single(schema.ObjectStores);
        Assert.Equal("store", store.Name);
        Assert.Equal(IndexedDBKeyPath.FromString("id"), store.KeyPath);
        Assert.True(store.AutoIncrement);
        var index = Assert.Single(store.Indexes);
        Assert.Equal(IndexedDBKeyPath.FromList(["a", "b"]), index.KeyPath);

        using var read = Backend.BeginTransaction(Origin, "db", IdbTransactionMode.ReadOnly, [1]);
        Assert.Equal(["1"], read.GetRecords(1, IndexedDBKeyRange.All, reverse: false, int.MaxValue).Select(record => Show(record.Key)));
        Assert.Equal(1, read.CountIndexRecords(2, IndexedDBKeyRange.All));
        Assert.Equal(2, read.GetKeyGeneratorCurrentNumber(1));
        read.Commit();
    }

    [Fact]
    public void Abort_OfTheTransactionThatCreatedTheDatabase_DeletesIt()
    {
        using (var transaction = CreateDatabase("new"))
        {
            Assert.NotNull(Backend.GetDatabase(Origin, "new"));
            transaction.Abort();
        }

        Assert.Null(Backend.GetDatabase(Origin, "new"));
        Assert.Empty(Backend.GetDatabases(Origin));
    }

    [Fact]
    public void Databases_AreListedByOriginAndDeleted()
    {
        using (var transaction = CreateDatabase("a\uD800"))
        {
            transaction.Commit();
        }

        using (var transaction = Backend.BeginTransaction("https://other.example", "b", IdbTransactionMode.VersionChange, []))
        {
            transaction.SetVersion(5);
            transaction.Commit();
        }

        Assert.Equal([new IndexedDBDatabaseInfo("a\uD800", 1)], Backend.GetDatabases(Origin));
        Assert.Equal([new IndexedDBDatabaseInfo("b", 5)], Backend.GetDatabases("https://other.example"));
        Backend.DeleteDatabase(Origin, "a\uD800");
        Assert.Empty(Backend.GetDatabases(Origin));
        Assert.Null(Backend.GetDatabase(Origin, "a\uD800"));
    }

    [Fact]
    public void CommittedData_IsKeptByTheStorage()
    {
        using (var transaction = CreateDatabase())
        {
            transaction.PutRecord(1, Key("key"), [42], [new IndexedDBIndexKey(2, Key(new object[] { 1, 2 }))]);
            transaction.SetKeyGeneratorCurrentNumber(1, 7);
            transaction.Commit();
        }

        var backend = Reopen();
        var schema = backend.GetDatabase(Origin, "db")!;
        Assert.Equal("index", Assert.Single(Assert.Single(schema.ObjectStores).Indexes).Name);
        using var read = backend.BeginTransaction(Origin, "db", IdbTransactionMode.ReadOnly, [1]);
        Assert.Equal([42], Assert.Single(read.GetRecords(1, IndexedDBKeyRange.All, reverse: false, int.MaxValue)).Value);
        Assert.Equal("[1, 2]", Show(Assert.Single(read.GetIndexRecords(2, IndexedDBKeyRange.All, reverse: false, start: null, int.MaxValue)).Key));
        Assert.Equal(7, read.GetKeyGeneratorCurrentNumber(1));
        read.Commit();
    }
}

public sealed class InMemoryIndexedDBBackendTests : IndexedDBBackendTests
{
    private readonly InMemoryIndexedDBBackend _backend = new();

    protected override IndexedDBBackend CreateBackend() => _backend;

    // The databases are kept by the instance
    protected override IndexedDBBackend Reopen() => Backend;
}

public sealed class SqliteIndexedDBBackendTests : IndexedDBBackendTests
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "idb-" + Guid.NewGuid().ToString("N"));

    protected override IndexedDBBackend CreateBackend() => new SqliteIndexedDBBackend(_directory);

    protected override void DisposeBackend(IndexedDBBackend backend) => ((SqliteIndexedDBBackend)backend).Dispose();

    protected override void Dispose(bool disposing)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        base.Dispose(disposing);
    }
}
