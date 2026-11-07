namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An <see cref="IndexedDBBackend"/> that keeps the databases in memory: the default backend of the browsing contexts (see
/// <see cref="BrowsingContextOptions.IndexedDB"/>). It is thread-safe, and can be shared by several browsing contexts.
/// </summary>
public sealed class InMemoryIndexedDBBackend : IndexedDBBackend
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Origin, string Name), Database> _databases = [];

    /// <inheritdoc/>
    public override IReadOnlyList<IndexedDBDatabaseInfo> GetDatabases(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return [.. _databases.Where(entry => entry.Key.Origin == origin).Select(entry => new IndexedDBDatabaseInfo(entry.Key.Name, entry.Value.Version))];
        }
    }

    /// <inheritdoc/>
    public override IndexedDBDatabaseSchema? GetDatabase(string origin, string name)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        Database? database;
        lock (_lock)
        {
            if (!_databases.TryGetValue((origin, name), out database))
                return null;
        }

        lock (database.Lock)
        {
            return database.GetSchema(name);
        }
    }

    /// <inheritdoc/>
    public override void DeleteDatabase(string origin, string name)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
        {
            _databases.Remove((origin, name));
        }
    }

    /// <inheritdoc/>
    public override long GetUsage(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        // The size of the keys and of the values of the records, and a fixed size per database, object store and index
        const int SchemaOverhead = 128;
        List<(string Name, Database Database)> databases = [];
        lock (_lock)
        {
            foreach (var (key, database) in _databases)
            {
                if (key.Origin == origin)
                {
                    databases.Add((key.Name, database));
                }
            }
        }

        long usage = 0;
        foreach (var (name, database) in databases)
        {
            lock (database.Lock)
            {
                usage += SchemaOverhead + (name.Length * sizeof(char));
                foreach (var store in database.ObjectStores.Values)
                {
                    usage += SchemaOverhead + (store.Name.Length * sizeof(char)) + store.Records.ByteSize;
                    foreach (var index in store.Indexes)
                    {
                        usage += SchemaOverhead + (index.Name.Length * sizeof(char)) + index.Records.ByteSize;
                    }
                }
            }
        }

        return usage;
    }

    /// <inheritdoc/>
    public override IndexedDBBackendTransaction BeginTransaction(string origin, string name, IdbTransactionMode mode, IReadOnlyList<long> objectStoreIds)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        Database? database;
        var created = false;
        lock (_lock)
        {
            if (!_databases.TryGetValue((origin, name), out database))
            {
                if (mode != IdbTransactionMode.VersionChange)
                    throw new InvalidOperationException($"The database '{name}' does not exist");

                database = new Database();
                _databases.Add((origin, name), database);
                created = true;
            }
        }

        return new Transaction(this, (origin, name), database, created);
    }

    private sealed class Transaction(InMemoryIndexedDBBackend backend, (string Origin, string Name) key, Database database, bool created) : IndexedDBBackendTransaction
    {
        private readonly List<Action> _undo = [];
        private bool _completed;

        public override void SetVersion(long version)
        {
            lock (database.Lock)
            {
                var previous = database.Version;
                database.Version = version;
                _undo.Add(() => database.Version = previous);
            }
        }

        public override void CreateObjectStore(IndexedDBObjectStoreSchema objectStore)
        {
            ArgumentNullException.ThrowIfNull(objectStore);
            lock (database.Lock)
            {
                var store = new ObjectStore(objectStore.Id, objectStore.Name, objectStore.KeyPath, objectStore.AutoIncrement);
                database.ObjectStores.Add(store.Id, store);
                _undo.Add(() => database.ObjectStores.Remove(store.Id));
            }
        }

        public override void DeleteObjectStore(long objectStoreId)
        {
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                database.ObjectStores.Remove(objectStoreId);
                _undo.Add(() => database.ObjectStores.Add(objectStoreId, store));
            }
        }

        public override void RenameObjectStore(long objectStoreId, string name)
        {
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                var previous = store.Name;
                store.Name = name;
                _undo.Add(() => store.Name = previous);
            }
        }

        public override void CreateIndex(long objectStoreId, IndexedDBIndexSchema index)
        {
            ArgumentNullException.ThrowIfNull(index);
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                var newIndex = new Index(index.Id, index.Name, index.KeyPath, index.Unique, index.MultiEntry, store);
                store.Indexes.Add(newIndex);
                _undo.Add(() => store.Indexes.Remove(newIndex));
            }
        }

        public override void DeleteIndex(long indexId)
        {
            lock (database.Lock)
            {
                var index = GetIndex(indexId);
                var position = index.Store.Indexes.IndexOf(index);
                index.Store.Indexes.RemoveAt(position);
                _undo.Add(() => index.Store.Indexes.Insert(position, index));
            }
        }

        public override void RenameIndex(long indexId, string name)
        {
            lock (database.Lock)
            {
                var index = GetIndex(indexId);
                var previous = index.Name;
                index.Name = name;
                _undo.Add(() => index.Name = previous);
            }
        }

        public override long GetKeyGeneratorCurrentNumber(long objectStoreId)
        {
            lock (database.Lock)
            {
                return GetObjectStore(objectStoreId).CurrentNumber;
            }
        }

        public override void SetKeyGeneratorCurrentNumber(long objectStoreId, long value)
        {
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                var previous = store.CurrentNumber;
                store.CurrentNumber = value;
                _undo.Add(() => store.CurrentNumber = previous);
            }
        }

        public override IReadOnlyList<IndexedDBRecord> GetRecords(long objectStoreId, IndexedDBKeyRange range, bool reverse, int limit)
        {
            ArgumentNullException.ThrowIfNull(range);
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                var result = new List<IndexedDBRecord>();
                foreach (var record in store.Records.Enumerate(range, reverse, start: null))
                {
                    if (result.Count >= limit)
                        break;

                    result.Add(new IndexedDBRecord(record.Key, record.Value));
                }

                return result;
            }
        }

        public override long CountRecords(long objectStoreId, IndexedDBKeyRange range)
        {
            ArgumentNullException.ThrowIfNull(range);
            lock (database.Lock)
            {
                return GetObjectStore(objectStoreId).Records.Enumerate(range, reverse: false, start: null).LongCount();
            }
        }

        public override void PutRecord(long objectStoreId, byte[] key, byte[] value, IReadOnlyList<IndexedDBIndexKey> indexKeys)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(indexKeys);
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                DeleteRecord(store, key);
                var record = new StoredRecord(key, value);
                store.Records.Add(record);
                _undo.Add(() => store.Records.Remove(record));
                foreach (var indexKey in indexKeys)
                {
                    var index = store.Indexes.Find(index => index.Id == indexKey.IndexId) ?? throw new ArgumentException($"The index {indexKey.IndexId} does not exist", nameof(indexKeys));
                    var indexRecord = new StoredRecord(indexKey.Key, key);
                    index.Records.Add(indexRecord);
                    _undo.Add(() => index.Records.Remove(indexRecord));
                }
            }
        }

        public override void DeleteRecords(long objectStoreId, IndexedDBKeyRange range)
        {
            ArgumentNullException.ThrowIfNull(range);
            lock (database.Lock)
            {
                var store = GetObjectStore(objectStoreId);
                foreach (var record in store.Records.Enumerate(range, reverse: false, start: null).ToList())
                {
                    DeleteRecord(store, record.Key);
                }
            }
        }

        public override void ClearObjectStore(long objectStoreId) => DeleteRecords(objectStoreId, IndexedDBKeyRange.All);

        public override IReadOnlyList<IndexedDBIndexRecord> GetIndexRecords(long indexId, IndexedDBKeyRange range, bool reverse, IndexedDBIndexPosition? start, int limit)
        {
            ArgumentNullException.ThrowIfNull(range);
            lock (database.Lock)
            {
                var index = GetIndex(indexId);
                var result = new List<IndexedDBIndexRecord>();
                foreach (var indexRecord in index.Records.Enumerate(range, reverse, start))
                {
                    if (result.Count >= limit)
                        break;

                    result.Add(new IndexedDBIndexRecord(indexRecord.Key, indexRecord.Value));
                }

                return result;
            }
        }

        public override long CountIndexRecords(long indexId, IndexedDBKeyRange range)
        {
            ArgumentNullException.ThrowIfNull(range);
            lock (database.Lock)
            {
                return GetIndex(indexId).Records.Enumerate(range, reverse: false, start: null).LongCount();
            }
        }

        public override void PutIndexRecords(long indexId, IReadOnlyList<IndexedDBIndexRecord> records)
        {
            ArgumentNullException.ThrowIfNull(records);
            lock (database.Lock)
            {
                var index = GetIndex(indexId);
                foreach (var record in records)
                {
                    var indexRecord = new StoredRecord(record.Key, record.PrimaryKey);
                    index.Records.Add(indexRecord);
                    _undo.Add(() => index.Records.Remove(indexRecord));
                }
            }
        }

        public override void Commit()
        {
            _completed = true;
            _undo.Clear();
        }

        public override void Abort()
        {
            if (_completed)
                return;

            _completed = true;
            lock (database.Lock)
            {
                for (var i = _undo.Count - 1; i >= 0; i--)
                {
                    _undo[i]();
                }

                _undo.Clear();
            }

            if (created)
            {
                lock (backend._lock)
                {
                    if (backend._databases.TryGetValue(key, out var current) && ReferenceEquals(current, database))
                    {
                        backend._databases.Remove(key);
                    }
                }
            }
        }

        private void DeleteRecord(ObjectStore store, byte[] key)
        {
            if (store.Records.Find(key) is not { } existing)
                return;

            store.Records.Remove(existing);
            _undo.Add(() => store.Records.Add(existing));
            foreach (var index in store.Indexes)
            {
                foreach (var indexRecord in index.Records.FindByValue(key))
                {
                    index.Records.Remove(indexRecord);
                    _undo.Add(() => index.Records.Add(indexRecord));
                }
            }
        }

        private ObjectStore GetObjectStore(long objectStoreId) => database.ObjectStores.TryGetValue(objectStoreId, out var store) ? store : throw new ArgumentException($"The object store {objectStoreId} does not exist", nameof(objectStoreId));

        private Index GetIndex(long indexId)
        {
            foreach (var store in database.ObjectStores.Values)
            {
                if (store.Indexes.Find(index => index.Id == indexId) is { } index)
                    return index;
            }

            throw new ArgumentException($"The index {indexId} does not exist", nameof(indexId));
        }
    }

    private sealed class Database
    {
        public Lock Lock { get; } = new();

        public long Version { get; set; }

        public SortedDictionary<long, ObjectStore> ObjectStores { get; } = [];

        public IndexedDBDatabaseSchema GetSchema(string name) => new()
        {
            Name = name,
            Version = Version,
            ObjectStores = [.. ObjectStores.Values.Select(store => new IndexedDBObjectStoreSchema
            {
                Id = store.Id,
                Name = store.Name,
                KeyPath = store.KeyPath,
                AutoIncrement = store.AutoIncrement,
                Indexes = [.. store.Indexes.Select(index => new IndexedDBIndexSchema { Id = index.Id, Name = index.Name, KeyPath = index.KeyPath, Unique = index.Unique, MultiEntry = index.MultiEntry })],
            })],
        };
    }

    private sealed class ObjectStore(long id, string name, IndexedDBKeyPath? keyPath, bool autoIncrement)
    {
        public long Id { get; } = id;

        public string Name { get; set; } = name;

        public IndexedDBKeyPath? KeyPath { get; } = keyPath;

        public bool AutoIncrement { get; } = autoIncrement;

        public long CurrentNumber { get; set; } = 1;

        public SortedRecords Records { get; } = new(compareValues: false);

        public List<Index> Indexes { get; } = [];
    }

    private sealed class Index(long id, string name, IndexedDBKeyPath keyPath, bool unique, bool multiEntry, ObjectStore store)
    {
        public long Id { get; } = id;

        public string Name { get; set; } = name;

        public IndexedDBKeyPath KeyPath { get; } = keyPath;

        public bool Unique { get; } = unique;

        public bool MultiEntry { get; } = multiEntry;

        public ObjectStore Store { get; } = store;

        // The index records: the key of the index, and the primary key as value
        public SortedRecords Records { get; } = new(compareValues: true);
    }

    private sealed record StoredRecord(byte[] Key, byte[] Value);

    // The records sorted by key (and by value for the index records, whose keys are not unique)
    private sealed class SortedRecords(bool compareValues)
    {
        private readonly List<StoredRecord> _records = [];

        // The index records of each primary key, to remove them with the record of the object store
        private readonly Dictionary<string, List<StoredRecord>>? _byValue = compareValues ? new(StringComparer.Ordinal) : null;

        /// <summary>The number of bytes of the keys and of the values of the records.</summary>
        public long ByteSize { get; private set; }

        public void Add(StoredRecord record)
        {
            var position = LowerBound(record.Key, record.Value);
            _records.Insert(position, record);
            ByteSize += record.Key.Length + record.Value.Length;
            if (_byValue is not null)
            {
                var valueKey = Convert.ToHexString(record.Value);
                if (!_byValue.TryGetValue(valueKey, out var list))
                {
                    list = [];
                    _byValue.Add(valueKey, list);
                }

                list.Add(record);
            }
        }

        public void Remove(StoredRecord record)
        {
            var position = LowerBound(record.Key, record.Value);
            if (position < _records.Count && ReferenceEquals(_records[position], record))
            {
                _records.RemoveAt(position);
                ByteSize -= record.Key.Length + record.Value.Length;
            }
            else if (_records.Remove(record))
            {
                ByteSize -= record.Key.Length + record.Value.Length;
            }

            if (_byValue is not null && _byValue.TryGetValue(Convert.ToHexString(record.Value), out var list))
            {
                list.Remove(record);
            }
        }

        public StoredRecord? Find(byte[] key)
        {
            var position = LowerBound(key, value: null);
            return position < _records.Count && _records[position].Key.AsSpan().SequenceEqual(key) ? _records[position] : null;
        }

        public IEnumerable<StoredRecord> FindByValue(byte[] value)
            => _byValue is not null && _byValue.TryGetValue(Convert.ToHexString(value), out var list) ? [.. list] : [];

        public IEnumerable<StoredRecord> Enumerate(IndexedDBKeyRange range, bool reverse, IndexedDBIndexPosition? start)
        {
            if (!reverse)
            {
                var first = range.Lower is null ? 0 : LowerBound(range.Lower, value: null);
                if (start is { } position)
                {
                    first = Math.Max(first, position.Inclusive ? LowerBound(position.Key, position.PrimaryKey) : UpperBound(position.Key, position.PrimaryKey));
                }

                for (var i = first; i < _records.Count; i++)
                {
                    var record = _records[i];
                    if (!range.Contains(record.Key))
                    {
                        if (range.Upper is not null && record.Key.AsSpan().SequenceCompareTo(range.Upper) > 0)
                            yield break;

                        continue;
                    }

                    yield return record;
                }
            }
            else
            {
                var end = range.Upper is null ? _records.Count : UpperBound(range.Upper, value: null);
                if (start is { } position)
                {
                    end = Math.Min(end, position.Inclusive ? UpperBound(position.Key, position.PrimaryKey) : LowerBound(position.Key, position.PrimaryKey));
                }

                for (var i = end - 1; i >= 0; i--)
                {
                    var record = _records[i];
                    if (!range.Contains(record.Key))
                    {
                        if (range.Lower is not null && record.Key.AsSpan().SequenceCompareTo(range.Lower) < 0)
                            yield break;

                        continue;
                    }

                    yield return record;
                }
            }
        }

        // The position of the first record that is not lower than (key, value); a null value is lower than all the values
        private int LowerBound(byte[] key, byte[]? value)
        {
            int low = 0, high = _records.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (Compare(_records[middle], key, value) < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        // The position of the first record that is greater than (key, value); a null value is greater than all the values
        private int UpperBound(byte[] key, byte[]? value)
        {
            int low = 0, high = _records.Count;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (Compare(_records[middle], key, value) <= 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private int Compare(StoredRecord record, byte[] key, byte[]? value)
        {
            var comparison = record.Key.AsSpan().SequenceCompareTo(key);
            if (comparison != 0 || !compareValues || value is null)
                return comparison;

            return record.Value.AsSpan().SequenceCompareTo(value);
        }
    }
}
