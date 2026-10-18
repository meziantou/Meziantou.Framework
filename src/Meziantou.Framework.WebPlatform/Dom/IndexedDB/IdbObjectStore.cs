using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>An object store handle (https://w3c.github.io/IndexedDB/#idbobjectstore).</summary>
[IdlInterface("IDBObjectStore", "https://w3c.github.io/IndexedDB/#idbobjectstore")]
public sealed class IdbObjectStore
{
    private readonly Dictionary<IndexInfo, IdbIndex> _indexHandles = [];
    private object? _keyPathValue;

    internal IdbObjectStore(IdbTransaction transaction, ObjectStoreInfo store)
    {
        TransactionObject = transaction;
        Store = store;
    }

    internal IdbTransaction TransactionObject { get; }

    internal ObjectStoreInfo Store { get; }

    private IIndexedDBValueOperations Operations => TransactionObject.Operations;

    /// <summary>Gets or sets the name of the object store (it can only be changed in an upgrade transaction).</summary>
    [IdlMember("name", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-name")]
    public string Name
    {
        get => Store.Name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var transaction = TransactionObject;
            if (Store.Deleted)
                throw DomException.InvalidState("The object store has been deleted");

            if (transaction.Mode != IdbTransactionMode.VersionChange)
                throw DomException.InvalidState("The object stores can only be renamed in an upgrade transaction");

            if (!transaction.IsActive)
                throw IdbTransaction.InactiveError();

            if (Store.Name == value)
                return;

            if (transaction.Connection.ObjectStoreSet.Exists(store => store.Name == value))
                throw new DomException($"The object store '{value}' already exists", "ConstraintError");

            Store.Name = value;
            var id = Store.Id;
            transaction.QueueInternalOperation(backend => backend.RenameObjectStore(id, value));
        }
    }

    /// <summary>Gets the key path of the object store: a string, an array of strings (the same array each time), or null.</summary>
    [IdlMember("keyPath", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-keypath")]
    public object? KeyPath => _keyPathValue ??= KeyPathToValue(Operations, Store.KeyPath);

    /// <summary>Gets the sorted names of the indexes of the object store.</summary>
    [IdlMember("indexNames", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-indexnames")]
    public DomStringList IndexNames => IdbDatabase.CreateSortedNameList(Store.Indexes.Select(index => index.Name));

    /// <summary>Gets the transaction of the handle.</summary>
    [IdlMember("transaction", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-transaction")]
    public IdbTransaction Transaction => TransactionObject;

    /// <summary>Gets whether the object store has a key generator.</summary>
    [IdlMember("autoIncrement", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-autoincrement")]
    public bool AutoIncrement => Store.AutoIncrement;

    /// <summary>Adds or replaces a record (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-put).</summary>
    [IdlMember("put", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-put")]
    public IdbRequest Put(object? value, object? key = null) => AddOrPut(value, key, keyGiven: key is not null, noOverwrite: false);

    /// <summary>Adds a record, which fails when a record with the same key exists (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-add).</summary>
    [IdlMember("add", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-add")]
    public IdbRequest Add(object? value, object? key = null) => AddOrPut(value, key, keyGiven: key is not null, noOverwrite: true);

    /// <summary>Deletes the records with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-delete).</summary>
    [IdlMember("delete", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-delete")]
    public IdbRequest Delete(object? query)
    {
        CheckWritable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query, nullDisallowed: true);
        var id = Store.Id;
        return TransactionObject.ExecuteRequest(this, backend =>
        {
            backend.DeleteRecords(id, range);
            return null;
        });
    }

    /// <summary>Deletes all the records (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-clear).</summary>
    [IdlMember("clear", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-clear")]
    public IdbRequest Clear()
    {
        CheckWritable();
        var id = Store.Id;
        return TransactionObject.ExecuteRequest(this, backend =>
        {
            backend.ClearObjectStore(id);
            return null;
        });
    }

    /// <summary>Retrieves the value of the first record with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-get).</summary>
    [IdlMember("get", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-get")]
    public IdbRequest Get(object? query)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query, nullDisallowed: true);
        var operations = Operations;
        var id = Store.Id;
        return TransactionObject.ExecuteRequest(this, backend => backend.GetRecords(id, range, reverse: false, limit: 1) is [var record] ? IdbOperations.DeserializeValue(operations, record.Value) : null);
    }

    /// <summary>Retrieves the key of the first record with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getkey).</summary>
    [IdlMember("getKey", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getkey")]
    public IdbRequest GetKey(object? query)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query, nullDisallowed: true);
        var operations = Operations;
        var id = Store.Id;
        return TransactionObject.ExecuteRequest(this, backend => backend.GetRecords(id, range, reverse: false, limit: 1) is [var record] ? operations.ConvertToValue(IdbKey.Decode(record.Key)) : null);
    }

    /// <summary>Retrieves the values of the records (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getall).</summary>
    [IdlMember("getAll", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getall")]
    public IdbRequest GetAll(object? queryOrOptions = null, uint? count = null) => RetrieveMultiple("value", queryOrOptions, count);

    /// <summary>Retrieves the keys of the records (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getallkeys).</summary>
    [IdlMember("getAllKeys", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getallkeys")]
    public IdbRequest GetAllKeys(object? queryOrOptions = null, uint? count = null) => RetrieveMultiple("key", queryOrOptions, count);

    /// <summary>Retrieves the keys and the values of the records (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getallrecords).</summary>
    [IdlMember("getAllRecords", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-getallrecords")]
    public IdbRequest GetAllRecords(IdbGetAllOptions? options = null) => RetrieveMultiple("record", options ?? new IdbGetAllOptions(), count: null);

    /// <summary>Counts the records with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-count).</summary>
    [IdlMember("count", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-count")]
    public IdbRequest Count(object? query = null)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query);
        var id = Store.Id;
        return TransactionObject.ExecuteRequest(this, backend => (double)backend.CountRecords(id, range));
    }

    /// <summary>Opens a cursor over the records (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-opencursor).</summary>
    [IdlMember("openCursor", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-opencursor")]
    public IdbRequest OpenCursor(object? query = null, IdbCursorDirection direction = IdbCursorDirection.Next) => OpenCursorCore(query, direction, keyOnly: false);

    /// <summary>Opens a cursor over the keys of the records (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-openkeycursor).</summary>
    [IdlMember("openKeyCursor", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-openkeycursor")]
    public IdbRequest OpenKeyCursor(object? query = null, IdbCursorDirection direction = IdbCursorDirection.Next) => OpenCursorCore(query, direction, keyOnly: true);

    /// <summary>Returns the handle of an index (the same handle for the same name) (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-index).</summary>
    [IdlMember("index", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-index")]
    public IdbIndex Index(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Store.Deleted)
            throw DomException.InvalidState("The object store has been deleted");

        if (TransactionObject.IsFinished)
            throw DomException.InvalidState("The transaction is finished");

        var index = Store.Indexes.Find(index => index.Name == name) ?? throw DomException.NotFound($"The index '{name}' does not exist");
        return GetIndexHandle(index);
    }

    /// <summary>Creates an index in an upgrade transaction (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-createindex).</summary>
    [IdlMember("createIndex", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-createindex")]
    public IdbIndex CreateIndex(string name, object? keyPath, IdbIndexParameters? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(keyPath);
        var transaction = TransactionObject;
        if (transaction.Mode != IdbTransactionMode.VersionChange)
            throw DomException.InvalidState("The indexes can only be created in an upgrade transaction");

        if (Store.Deleted)
            throw DomException.InvalidState("The object store has been deleted");

        if (!transaction.IsActive)
            throw IdbTransaction.InactiveError();

        if (Store.Indexes.Exists(index => index.Name == name))
            throw new DomException($"The index '{name}' already exists", "ConstraintError");

        var path = IdbDatabase.ToKeyPath(keyPath);
        if (!IdbDatabase.IsValidKeyPath(path))
            throw DomException.Syntax("The key path is not valid");

        var unique = options?.Unique ?? false;
        var multiEntry = options?.MultiEntry ?? false;
        if (path.IsList && multiEntry)
            throw new DomException("A multiEntry index cannot have an array key path", "InvalidAccessError");

        var index = new IndexInfo(transaction.Connection.AllocateId(), name, path, unique, multiEntry, Store) { CreatedInUpgrade = true };
        Store.Indexes.Add(index);
        var schema = new IndexedDBIndexSchema { Id = index.Id, Name = name, KeyPath = path, Unique = unique, MultiEntry = multiEntry };
        var operations = Operations;
        var store = Store;

        // The index is populated asynchronously with the records of the object store: a uniqueness constraint aborts the transaction
        transaction.QueueInternalOperation(backend =>
        {
            backend.CreateIndex(store.Id, schema);
            var records = new List<IndexedDBIndexRecord>();
            var seen = unique ? new HashSet<string>(StringComparer.Ordinal) : null;
            foreach (var record in backend.GetRecords(store.Id, IndexedDBKeyRange.All, reverse: false, int.MaxValue))
            {
                var value = IdbOperations.DeserializeValue(operations, record.Value);
                IdbKey? indexKey;
                try
                {
                    indexKey = IdbOperations.ExtractKey(operations, value, path, multiEntry, out _);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    continue;
                }

                if (indexKey is null)
                    continue;

                foreach (var subkey in multiEntry && indexKey.Type == IdbKeyType.Array ? indexKey.Items! : [indexKey])
                {
                    var encoded = subkey.Encode();
                    if (seen is not null && !seen.Add(Convert.ToHexString(encoded)))
                        throw new DomException($"The records of the object store do not satisfy the uniqueness constraint of the index '{name}'", "ConstraintError");

                    records.Add(new IndexedDBIndexRecord(encoded, record.Key));
                }
            }

            backend.PutIndexRecords(index.Id, records);
            store.BackendIndexes.Add(index);
        });

        return GetIndexHandle(index);
    }

    /// <summary>Deletes an index in an upgrade transaction (https://w3c.github.io/IndexedDB/#dom-idbobjectstore-deleteindex).</summary>
    [IdlMember("deleteIndex", "https://w3c.github.io/IndexedDB/#dom-idbobjectstore-deleteindex")]
    public void DeleteIndex(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var transaction = TransactionObject;
        if (transaction.Mode != IdbTransactionMode.VersionChange)
            throw DomException.InvalidState("The indexes can only be deleted in an upgrade transaction");

        if (Store.Deleted)
            throw DomException.InvalidState("The object store has been deleted");

        if (!transaction.IsActive)
            throw IdbTransaction.InactiveError();

        var index = Store.Indexes.Find(index => index.Name == name) ?? throw DomException.NotFound($"The index '{name}' does not exist");
        Store.Indexes.Remove(index);
        index.Deleted = true;
        var store = Store;
        transaction.QueueInternalOperation(backend =>
        {
            backend.DeleteIndex(index.Id);
            store.BackendIndexes.Remove(index);
        });
    }

    internal IdbIndex GetIndexHandle(IndexInfo index)
    {
        if (!_indexHandles.TryGetValue(index, out var handle))
        {
            handle = new IdbIndex(this, index);
            _indexHandles.Add(index, handle);
        }

        return handle;
    }

    /// <summary>The key path converted to a value: a string, an array of strings, or null.</summary>
    internal static object? KeyPathToValue(IIndexedDBValueOperations operations, IndexedDBKeyPath? keyPath) => keyPath switch
    {
        null => NullValue.Instance,
        { Paths: { } paths } => operations.CreateArray([.. paths]),
        _ => keyPath.Path,
    };

    // https://w3c.github.io/IndexedDB/#add-or-put
    private IdbRequest AddOrPut(object? value, object? key, bool keyGiven, bool noOverwrite)
    {
        var transaction = TransactionObject;
        var store = Store;
        CheckWritable();
        if (store.KeyPath is not null && keyGiven)
            throw new DomException("The object store uses in-line keys and the key parameter was provided", "DataError");

        if (store.KeyPath is null && !store.AutoIncrement && !keyGiven)
            throw new DomException("The object store uses out-of-line keys and has no key generator, and the key parameter was not provided", "DataError");

        var operations = Operations;
        IdbKey? recordKey = null;
        if (keyGiven)
        {
            recordKey = IdbOperations.ConvertToKey(operations, key);
        }

        var clone = IdbOperations.Clone(transaction, value);
        if (store.KeyPath is { } keyPath)
        {
            var kpk = IdbOperations.ExtractKey(operations, clone, keyPath, multiEntry: false, out var failure);
            if (!failure && kpk is null)
                throw new DomException("The key path of the object store did not yield a valid key", "DataError");

            if (kpk is not null)
            {
                recordKey = kpk;
            }
            else if (!store.AutoIncrement)
            {
                throw new DomException("The key path of the object store did not yield a value", "DataError");
            }
            else if (!operations.CanInjectKey(clone, keyPath.Path!))
            {
                throw new DomException("The generated key could not be injected into the value", "DataError");
            }
        }

        return transaction.ExecuteRequest(this, backend => operations.ConvertToValue(IdbOperations.StoreRecord(backend, operations, store, clone, recordKey, noOverwrite)));
    }

    private IdbRequest RetrieveMultiple(string kind, object? queryOrOptions, uint? count)
    {
        CheckReadable();
        var operations = Operations;
        var (range, actualCount, direction) = IdbOperations.ConvertGetAllArguments(operations, queryOrOptions, count);
        var store = Store;
        return TransactionObject.ExecuteRequest(this, backend => IdbOperations.RetrieveMultipleFromObjectStore(backend, operations, store, range, kind, direction, actualCount));
    }

    private IdbRequest OpenCursorCore(object? query, IdbCursorDirection direction, bool keyOnly)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query);
        var cursor = keyOnly ? new IdbCursor(this, null, TransactionObject, direction, range) : new IdbCursorWithValue(this, null, TransactionObject, direction, range);
        var request = TransactionObject.ExecuteRequest(this, backend => cursor.Iterate(backend, key: null, primaryKey: null, count: 1));
        cursor.RequestObject = request;
        return request;
    }

    private void CheckReadable()
    {
        if (Store.Deleted)
            throw DomException.InvalidState("The object store has been deleted");

        if (!TransactionObject.IsActive)
            throw IdbTransaction.InactiveError();
    }

    private void CheckWritable()
    {
        CheckReadable();
        if (TransactionObject.Mode == IdbTransactionMode.ReadOnly)
            throw IdbTransaction.ReadOnlyError();
    }
}
