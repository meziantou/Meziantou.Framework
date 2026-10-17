namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>An index handle (https://w3c.github.io/IndexedDB/#idbindex).</summary>
[IdlInterface("IDBIndex", "https://w3c.github.io/IndexedDB/#idbindex")]
public sealed class IdbIndex
{
    private object? _keyPathValue;

    internal IdbIndex(IdbObjectStore objectStore, IndexInfo index)
    {
        ObjectStoreHandle = objectStore;
        Index = index;
    }

    internal IdbObjectStore ObjectStoreHandle { get; }

    internal IndexInfo Index { get; }

    private IdbTransaction TransactionObject => ObjectStoreHandle.TransactionObject;

    private IIndexedDBValueOperations Operations => TransactionObject.Operations;

    /// <summary>Gets or sets the name of the index (it can only be changed in an upgrade transaction).</summary>
    [IdlMember("name", "https://w3c.github.io/IndexedDB/#dom-idbindex-name")]
    public string Name
    {
        get => Index.Name;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var transaction = TransactionObject;
            if (transaction.Mode != IdbTransactionMode.VersionChange)
                throw DomException.InvalidState("The indexes can only be renamed in an upgrade transaction");

            if (!transaction.IsActive)
                throw IdbTransaction.InactiveError();

            if (Index.Deleted || Index.Store.Deleted)
                throw DomException.InvalidState("The index has been deleted");

            if (Index.Name == value)
                return;

            if (Index.Store.Indexes.Exists(index => index.Name == value))
                throw new DomException($"The index '{value}' already exists", "ConstraintError");

            Index.Name = value;
            var id = Index.Id;
            transaction.QueueInternalOperation(backend => backend.RenameIndex(id, value));
        }
    }

    /// <summary>Gets the object store handle of the index.</summary>
    [IdlMember("objectStore", "https://w3c.github.io/IndexedDB/#dom-idbindex-objectstore")]
    public IdbObjectStore ObjectStore => ObjectStoreHandle;

    /// <summary>Gets the key path of the index: a string or an array of strings (the same array each time).</summary>
    [IdlMember("keyPath", "https://w3c.github.io/IndexedDB/#dom-idbindex-keypath")]
    public object? KeyPath => _keyPathValue ??= IdbObjectStore.KeyPathToValue(Operations, Index.KeyPath);

    /// <summary>Gets whether an array key adds a record per item.</summary>
    [IdlMember("multiEntry", "https://w3c.github.io/IndexedDB/#dom-idbindex-multientry")]
    public bool MultiEntry => Index.MultiEntry;

    /// <summary>Gets whether the keys of the index are unique.</summary>
    [IdlMember("unique", "https://w3c.github.io/IndexedDB/#dom-idbindex-unique")]
    public bool Unique => Index.Unique;

    /// <summary>Retrieves the value of the record referenced by the first index record with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbindex-get).</summary>
    [IdlMember("get", "https://w3c.github.io/IndexedDB/#dom-idbindex-get")]
    public IdbRequest Get(object? query)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query, nullDisallowed: true);
        var operations = Operations;
        var index = Index;
        return TransactionObject.ExecuteRequest(this, backend =>
        {
            if (backend.GetIndexRecords(index.Id, range, reverse: false, start: null, limit: 1) is not [var record])
                return null;

            return IdbOperations.GetValue(backend, index.Store.Id, record.PrimaryKey) is { } value ? IdbOperations.DeserializeValue(operations, value) : null;
        });
    }

    /// <summary>Retrieves the primary key of the first index record with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbindex-getkey).</summary>
    [IdlMember("getKey", "https://w3c.github.io/IndexedDB/#dom-idbindex-getkey")]
    public IdbRequest GetKey(object? query)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query, nullDisallowed: true);
        var operations = Operations;
        var index = Index;
        return TransactionObject.ExecuteRequest(this, backend => backend.GetIndexRecords(index.Id, range, reverse: false, start: null, limit: 1) is [var record] ? operations.ConvertToValue(IdbKey.Decode(record.PrimaryKey)) : null);
    }

    /// <summary>Retrieves the values of the records (https://w3c.github.io/IndexedDB/#dom-idbindex-getall).</summary>
    [IdlMember("getAll", "https://w3c.github.io/IndexedDB/#dom-idbindex-getall")]
    public IdbRequest GetAll(object? queryOrOptions = null, uint? count = null) => RetrieveMultiple("value", queryOrOptions, count);

    /// <summary>Retrieves the primary keys of the records (https://w3c.github.io/IndexedDB/#dom-idbindex-getallkeys).</summary>
    [IdlMember("getAllKeys", "https://w3c.github.io/IndexedDB/#dom-idbindex-getallkeys")]
    public IdbRequest GetAllKeys(object? queryOrOptions = null, uint? count = null) => RetrieveMultiple("key", queryOrOptions, count);

    /// <summary>Retrieves the keys, the primary keys and the values of the records (https://w3c.github.io/IndexedDB/#dom-idbindex-getallrecords).</summary>
    [IdlMember("getAllRecords", "https://w3c.github.io/IndexedDB/#dom-idbindex-getallrecords")]
    public IdbRequest GetAllRecords(IdbGetAllOptions? options = null) => RetrieveMultiple("record", options ?? new IdbGetAllOptions(), count: null);

    /// <summary>Counts the index records with a key or in a key range (https://w3c.github.io/IndexedDB/#dom-idbindex-count).</summary>
    [IdlMember("count", "https://w3c.github.io/IndexedDB/#dom-idbindex-count")]
    public IdbRequest Count(object? query = null)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query);
        var id = Index.Id;
        return TransactionObject.ExecuteRequest(this, backend => (double)backend.CountIndexRecords(id, range));
    }

    /// <summary>Opens a cursor over the index records (https://w3c.github.io/IndexedDB/#dom-idbindex-opencursor).</summary>
    [IdlMember("openCursor", "https://w3c.github.io/IndexedDB/#dom-idbindex-opencursor")]
    public IdbRequest OpenCursor(object? query = null, IdbCursorDirection direction = IdbCursorDirection.Next) => OpenCursorCore(query, direction, keyOnly: false);

    /// <summary>Opens a cursor over the keys of the index records (https://w3c.github.io/IndexedDB/#dom-idbindex-openkeycursor).</summary>
    [IdlMember("openKeyCursor", "https://w3c.github.io/IndexedDB/#dom-idbindex-openkeycursor")]
    public IdbRequest OpenKeyCursor(object? query = null, IdbCursorDirection direction = IdbCursorDirection.Next) => OpenCursorCore(query, direction, keyOnly: true);

    private IdbRequest RetrieveMultiple(string kind, object? queryOrOptions, uint? count)
    {
        CheckReadable();
        var operations = Operations;
        var (range, actualCount, direction) = IdbOperations.ConvertGetAllArguments(operations, queryOrOptions, count);
        var index = Index;
        return TransactionObject.ExecuteRequest(this, backend => IdbOperations.RetrieveMultipleFromIndex(backend, operations, index, range, kind, direction, actualCount));
    }

    private IdbRequest OpenCursorCore(object? query, IdbCursorDirection direction, bool keyOnly)
    {
        CheckReadable();
        var range = IdbOperations.ConvertToKeyRange(Operations, query);
        var cursor = keyOnly ? new IdbCursor(ObjectStoreHandle, this, TransactionObject, direction, range) : new IdbCursorWithValue(ObjectStoreHandle, this, TransactionObject, direction, range);
        var request = TransactionObject.ExecuteRequest(this, backend => cursor.Iterate(backend, key: null, primaryKey: null, count: 1));
        cursor.RequestObject = request;
        return request;
    }

    private void CheckReadable()
    {
        if (Index.Deleted || Index.Store.Deleted)
            throw DomException.InvalidState("The index or its object store has been deleted");

        if (!TransactionObject.IsActive)
            throw IdbTransaction.InactiveError();
    }
}
