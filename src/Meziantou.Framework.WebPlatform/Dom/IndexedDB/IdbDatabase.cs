using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A connection to a database (https://w3c.github.io/IndexedDB/#idbdatabase).</summary>
/// <seealso href="https://w3c.github.io/IndexedDB/#idbdatabase"/>
[IdlInterface("IDBDatabase", "https://w3c.github.io/IndexedDB/#idbdatabase")]
public sealed class IdbDatabase : EventTarget
{
    private readonly List<IdbTransaction> _transactions = [];
    private long _nextId;

    internal IdbDatabase(GlobalScope global, IndexedDBDatabaseState state, IIndexedDBValueOperations operations, string name, ulong version, IndexedDBDatabaseSchema? schema)
    {
        Global = global;
        State = state;
        Operations = operations;
        Name = name;
        VersionValue = version;
        ObjectStoreSet = schema is null ? [] : [.. schema.ObjectStores.Select(ObjectStoreInfo.FromSchema)];
        _nextId = ObjectStoreSet.Count == 0 ? 1 : ObjectStoreSet.Max(store => Math.Max(store.Id, store.Indexes.Count == 0 ? 0 : store.Indexes.Max(index => index.Id))) + 1;
    }

    internal GlobalScope Global { get; }

    internal override GlobalScope? RelevantGlobal => Global;

    internal IndexedDBDatabaseState State { get; }

    internal IIndexedDBValueOperations Operations { get; }

    /// <summary>The version of the connection (https://w3c.github.io/IndexedDB/#connection-version).</summary>
    internal ulong VersionValue { get; set; }

    /// <summary>The object store set of the connection (https://w3c.github.io/IndexedDB/#connection-object-store-set).</summary>
    internal List<ObjectStoreInfo> ObjectStoreSet { get; set; }

    /// <summary>https://w3c.github.io/IndexedDB/#connection-close-pending-flag</summary>
    internal bool ClosePending { get; private set; }

    /// <summary>Whether the connection is closed (its close pending flag is set and its transactions are finished).</summary>
    internal bool IsClosed { get; private set; }

    /// <summary>The live upgrade transaction of the connection, or null.</summary>
    internal IdbTransaction? UpgradeTransaction { get; set; }

    /// <summary>Whether the upgrade transaction of the connection was aborted.</summary>
    internal bool UpgradeAborted { get; set; }

    /// <summary>Gets the name of the database.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-name"/>
    [IdlMember("name", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-name")]
    public string Name { get; }

    /// <summary>Gets the version of the connection.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-version"/>
    [IdlMember("version", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-version")]
    public ulong Version => VersionValue;

    /// <summary>Gets the sorted names of the object stores of the connection.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-objectstorenames"/>
    [IdlMember("objectStoreNames", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-objectstorenames")]
    public DomStringList ObjectStoreNames => CreateSortedNameList(ObjectStoreSet.Select(store => store.Name));

    /// <summary>The event handler of the abort events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-onabort"/>
    [IdlMember("onabort", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-onabort")]
    public Action<Event>? OnAbort
    {
        get => GetEventHandler("abort");
        set => SetEventHandler("abort", value);
    }

    /// <summary>The event handler of the close events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-onclose"/>
    [IdlMember("onclose", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-onclose")]
    public Action<Event>? OnClose
    {
        get => GetEventHandler("close");
        set => SetEventHandler("close", value);
    }

    /// <summary>The event handler of the error events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-onerror"/>
    [IdlMember("onerror", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-onerror")]
    public Action<Event>? OnError
    {
        get => GetEventHandler("error");
        set => SetEventHandler("error", value);
    }

    /// <summary>The event handler of the versionchange events.</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-onversionchange"/>
    [IdlMember("onversionchange", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-onversionchange")]
    public Action<Event>? OnVersionChange
    {
        get => GetEventHandler("versionchange");
        set => SetEventHandler("versionchange", value);
    }

    /// <summary>Creates a transaction (https://w3c.github.io/IndexedDB/#dom-idbdatabase-transaction).</summary>
    /// <param name="storeNames">The name of an object store, or a list of names.</param>
    /// <param name="mode">The mode of the transaction: <see cref="IdbTransactionMode.ReadOnly"/> or <see cref="IdbTransactionMode.ReadWrite"/>.</param>
    /// <param name="options">The options of the transaction.</param>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-transaction"/>
    [IdlMember("transaction", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-transaction")]
    public IdbTransaction Transaction(object? storeNames, IdbTransactionMode mode = IdbTransactionMode.ReadOnly, IdbTransactionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(storeNames);
        if (UpgradeTransaction is { IsFinished: false })
            throw DomException.InvalidState("A transaction cannot be created during an upgrade transaction");

        if (ClosePending)
            throw DomException.InvalidState("The connection is closed");

        var names = storeNames switch
        {
            string name => [name],
            IEnumerable<string> list => list.Distinct(StringComparer.Ordinal).ToList(),
            _ => throw new TypeErrorException("The store names must be a string or a sequence of strings"),
        };

        var scope = new List<ObjectStoreInfo>();
        foreach (var name in names)
        {
            var store = ObjectStoreSet.Find(store => store.Name == name) ?? throw DomException.NotFound($"The object store '{name}' does not exist");
            scope.Add(store);
        }

        if (scope.Count == 0)
            throw new DomException("The scope of the transaction is empty", "InvalidAccessError");

        if (mode is not (IdbTransactionMode.ReadOnly or IdbTransactionMode.ReadWrite))
            throw new TypeErrorException("The mode of the transaction must be 'readonly' or 'readwrite'");

        var transaction = new IdbTransaction(this, mode, options?.Durability ?? IdbTransactionDurability.Default, scope, upgradeRequest: null);
        AddTransaction(transaction);

        // https://w3c.github.io/IndexedDB/#cleanup-indexed-database-transactions: the transaction is inactive after the task
        Global.EventLoop.AddIndexedDBTransaction(transaction);
        State.AddTransaction(transaction);
        return transaction;
    }

    /// <summary>Closes the connection once its transactions are finished (https://w3c.github.io/IndexedDB/#dom-idbdatabase-close).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-close"/>
    [IdlMember("close", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-close")]
    public void Close() => CloseConnection(forced: false);

    /// <summary>Creates an object store in the upgrade transaction (https://w3c.github.io/IndexedDB/#dom-idbdatabase-createobjectstore).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-createobjectstore"/>
    [IdlMember("createObjectStore", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-createobjectstore")]
    public IdbObjectStore CreateObjectStore(string name, IdbObjectStoreParameters? options = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var transaction = UpgradeTransaction ?? throw DomException.InvalidState("The object stores can only be created in an upgrade transaction");
        if (!transaction.IsActive)
            throw IdbTransaction.InactiveError();

        var keyPath = options?.KeyPath is { } keyPathValue and not NullValue ? ToKeyPath(keyPathValue) : null;
        if (keyPath is not null && !IsValidKeyPath(keyPath))
            throw DomException.Syntax("The key path is not valid");

        if (ObjectStoreSet.Exists(store => store.Name == name))
            throw new DomException($"The object store '{name}' already exists", "ConstraintError");

        var autoIncrement = options?.AutoIncrement ?? false;
        if (autoIncrement && keyPath is not null && (keyPath.IsList || keyPath.Path!.Length == 0))
            throw new DomException("An object store with a key generator cannot have an empty or array key path", "InvalidAccessError");

        var store = new ObjectStoreInfo(_nextId++, name, keyPath, autoIncrement) { CreatedInUpgrade = true };
        ObjectStoreSet.Add(store);
        transaction.Scope.Add(store);
        var schema = new IndexedDBObjectStoreSchema { Id = store.Id, Name = name, KeyPath = keyPath, AutoIncrement = autoIncrement };
        transaction.QueueInternalOperation(backend => backend.CreateObjectStore(schema));
        return transaction.GetObjectStoreHandle(store);
    }

    /// <summary>Deletes an object store in the upgrade transaction (https://w3c.github.io/IndexedDB/#dom-idbdatabase-deleteobjectstore).</summary>
    /// <seealso href="https://w3c.github.io/IndexedDB/#dom-idbdatabase-deleteobjectstore"/>
    [IdlMember("deleteObjectStore", "https://w3c.github.io/IndexedDB/#dom-idbdatabase-deleteobjectstore")]
    public void DeleteObjectStore(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var transaction = UpgradeTransaction ?? throw DomException.InvalidState("The object stores can only be deleted in an upgrade transaction");
        if (!transaction.IsActive)
            throw IdbTransaction.InactiveError();

        var store = ObjectStoreSet.Find(store => store.Name == name) ?? throw DomException.NotFound($"The object store '{name}' does not exist");
        ObjectStoreSet.Remove(store);
        transaction.Scope.Remove(store);
        store.Deleted = true;
        foreach (var index in store.Indexes)
        {
            index.Deleted = true;
        }

        store.Indexes = [];
        transaction.QueueInternalOperation(backend => backend.DeleteObjectStore(store.Id));
    }

    internal long AllocateId() => _nextId++;

    internal void AddTransaction(IdbTransaction transaction)
    {
        lock (State.Lock)
        {
            _transactions.Add(transaction);
        }
    }

    /// <summary>Called when a transaction of the connection is finished: a connection whose close is pending is closed when its transactions are finished.</summary>
    internal void OnTransactionFinished(IdbTransaction transaction)
    {
        lock (State.Lock)
        {
            _transactions.Remove(transaction);
        }

        TryFinishClose();
    }

    /// <summary>https://w3c.github.io/IndexedDB/#close-a-database-connection</summary>
    internal void CloseConnection(bool forced, bool fireCloseEvent = true)
    {
        ClosePending = true;
        if (forced)
        {
            List<IdbTransaction> transactions;
            lock (State.Lock)
            {
                transactions = [.. _transactions];
            }

            foreach (var transaction in transactions)
            {
                transaction.Abort(new DomException("The connection was closed", "AbortError"));
            }
        }

        if (TryFinishClose() && forced && fireCloseEvent)
        {
            FireEvent(new Event("close"));
        }
    }

    private bool TryFinishClose()
    {
        lock (State.Lock)
        {
            if (!ClosePending || IsClosed || _transactions.Count > 0)
                return false;

            IsClosed = true;
        }

        State.OnConnectionClosed(this);
        Global.IndexedDBFactory?.RemoveConnection(this);
        return true;
    }

    /// <summary>https://w3c.github.io/IndexedDB/#upgrade-a-database: creates and starts the upgrade transaction.</summary>
    internal IdbTransaction CreateUpgradeTransaction(IdbOpenDBRequest request, ulong oldVersion, ulong newVersion, Action onFinished)
    {
        var transaction = new IdbTransaction(this, IdbTransactionMode.VersionChange, IdbTransactionDurability.Default, [.. ObjectStoreSet], request)
        {
            OldVersion = oldVersion,
            NewVersion = newVersion,
            UpgradeFinished = onFinished,
        };
        transaction.SetInactive();
        UpgradeTransaction = transaction;
        AddTransaction(transaction);
        transaction.QueueInternalOperation(backend => backend.SetVersion((long)newVersion));
        State.AddTransaction(transaction);
        return transaction;
    }

    /// <summary>https://w3c.github.io/IndexedDB/#fire-a-version-change-event</summary>
    internal bool FireVersionChangeEvent(string type, ulong oldVersion, ulong? newVersion)
    {
        var @event = new IdbVersionChangeEvent(type, new IdbVersionChangeEventInit { OldVersion = oldVersion, NewVersion = newVersion });
        FireEvent(@event);
        return @event.LegacyOutputDidListenersThrowFlag;
    }

    /// <summary>https://w3c.github.io/IndexedDB/#create-a-sorted-name-list: the names sorted by code unit.</summary>
    internal static DomStringList CreateSortedNameList(IEnumerable<string> names) => new(names.Order(StringComparer.Ordinal));

    /// <summary>Converts the IDL key path (a string or a sequence of strings).</summary>
    internal static IndexedDBKeyPath ToKeyPath(object value) => value switch
    {
        string path => IndexedDBKeyPath.FromString(path),
        IEnumerable<string> paths => IndexedDBKeyPath.FromList(paths),
        IEnumerable<object?> paths => IndexedDBKeyPath.FromList(paths.Select(path => path as string ?? throw new TypeErrorException("The key path must be a string or a sequence of strings"))),
        _ => throw new TypeErrorException("The key path must be a string or a sequence of strings"),
    };

    /// <summary>https://w3c.github.io/IndexedDB/#valid-key-path</summary>
    internal static bool IsValidKeyPath(IndexedDBKeyPath keyPath)
    {
        if (keyPath.Paths is { } paths)
            return paths.Count > 0 && paths.All(IsValidKeyPathString);

        return IsValidKeyPathString(keyPath.Path!);
    }

    private static bool IsValidKeyPathString(string path)
    {
        if (path.Length == 0)
            return true;

        foreach (var identifier in path.Split('.'))
        {
            if (!IsIdentifierName(identifier))
                return false;
        }

        return true;
    }

    // https://tc39.es/ecma262/#prod-IdentifierName (without the escape sequences)
    private static bool IsIdentifierName(string identifier)
    {
        if (identifier.Length == 0)
            return false;

        var first = true;
        for (var i = 0; i < identifier.Length; i++)
        {
            int codePoint = identifier[i];
            if (char.IsHighSurrogate(identifier[i]) && i + 1 < identifier.Length && char.IsLowSurrogate(identifier[i + 1]))
            {
                codePoint = char.ConvertToUtf32(identifier[i], identifier[i + 1]);
                i++;
            }

            var valid = codePoint is '$' or '_'
                || (first ? UnicodeProperties.IsIdStart(codePoint) : UnicodeProperties.IsIdContinue(codePoint) || codePoint is 0x200C or 0x200D);
            if (!valid)
                return false;

            first = false;
        }

        return true;
    }
}

/// <summary>An object store of a connection (https://w3c.github.io/IndexedDB/#object-store-construct).</summary>
internal sealed class ObjectStoreInfo(long id, string name, IndexedDBKeyPath? keyPath, bool autoIncrement)
{
    public long Id { get; } = id;

    public string Name { get; set; } = name;

    public IndexedDBKeyPath? KeyPath { get; } = keyPath;

    public bool AutoIncrement { get; } = autoIncrement;

    public List<IndexInfo> Indexes { get; set; } = [];

    /// <summary>
    /// The indexes that exist in the backend: the creation and the deletion of an index are asynchronous operations of the
    /// upgrade transaction, so the requests made before them see the previous indexes.
    /// </summary>
    public List<IndexInfo> BackendIndexes { get; set; } = [];

    public bool Deleted { get; set; }

    /// <summary>Whether the object store was created by the live upgrade transaction.</summary>
    public bool CreatedInUpgrade { get; set; }

    public static ObjectStoreInfo FromSchema(IndexedDBObjectStoreSchema schema)
    {
        var store = new ObjectStoreInfo(schema.Id, schema.Name, schema.KeyPath, schema.AutoIncrement);
        store.Indexes = [.. schema.Indexes.Select(index => new IndexInfo(index.Id, index.Name, index.KeyPath, index.Unique, index.MultiEntry, store))];
        store.BackendIndexes = [.. store.Indexes];
        return store;
    }
}

/// <summary>An index of a connection (https://w3c.github.io/IndexedDB/#index-construct).</summary>
internal sealed class IndexInfo(long id, string name, IndexedDBKeyPath keyPath, bool unique, bool multiEntry, ObjectStoreInfo store)
{
    public long Id { get; } = id;

    public string Name { get; set; } = name;

    public IndexedDBKeyPath KeyPath { get; } = keyPath;

    public bool Unique { get; } = unique;

    public bool MultiEntry { get; } = multiEntry;

    public ObjectStoreInfo Store { get; } = store;

    public bool Deleted { get; set; }

    /// <summary>Whether the index was created by the live upgrade transaction.</summary>
    public bool CreatedInUpgrade { get; set; }
}
