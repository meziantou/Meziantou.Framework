using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A cursor over the records of an object store or an index (https://w3c.github.io/IndexedDB/#idbcursor).</summary>
[IdlInterface("IDBCursor", "https://w3c.github.io/IndexedDB/#idbcursor")]
public class IdbCursor : IRealmBoundPlatformObject
{
    private readonly IndexedDBKeyRange _range;
    private IdbKey? _position;
    private IdbKey? _objectStorePosition;
    private object? _keyValue;
    private object? _primaryKeyValue;

    internal IdbCursor(IdbObjectStore objectStore, IdbIndex? index, IdbTransaction transaction, IdbCursorDirection direction, IndexedDBKeyRange range)
    {
        ObjectStoreHandle = objectStore;
        IndexHandle = index;
        TransactionObject = transaction;
        Direction = direction;
        _range = range;
    }

    internal IdbObjectStore ObjectStoreHandle { get; }

    internal IdbIndex? IndexHandle { get; }

    internal IdbTransaction TransactionObject { get; }

    internal IdbRequest RequestObject { get; set; } = null!;

    /// <summary>https://w3c.github.io/IndexedDB/#cursor-got-value-flag</summary>
    internal bool GotValue { get; private set; }

    /// <summary>The key of the cursor: the key of the index record or of the record, or null (undefined).</summary>
    internal IdbKey? CurrentKey { get; private set; }

    /// <summary>https://w3c.github.io/IndexedDB/#cursor-effective-key</summary>
    internal IdbKey? EffectiveKey => IndexHandle is null ? _position : _objectStorePosition;

    /// <summary>The value of the cursor (the deserialized value of the record).</summary>
    private protected object? CurrentValue { get; private set; }

    private ObjectStoreInfo EffectiveObjectStore => ObjectStoreHandle.Store;

    private IIndexedDBValueOperations Operations => TransactionObject.Operations;

    private bool KeyOnly => this is not IdbCursorWithValue;

    // The cursor is created in the realm of its source, not in the current realm (e.g. a method of another realm)
    Window? IRealmBoundPlatformObject.RealmWindow => TransactionObject.Global as Window;

    /// <summary>Gets the object store or the index of the cursor.</summary>
    [IdlMember("source", "https://w3c.github.io/IndexedDB/#dom-idbcursor-source")]
    public object Source => (object?)IndexHandle ?? ObjectStoreHandle;

    /// <summary>Gets the direction of the cursor.</summary>
    [IdlMember("direction", "https://w3c.github.io/IndexedDB/#dom-idbcursor-direction")]
    public IdbCursorDirection Direction { get; }

    /// <summary>Gets the key of the cursor (the same value until the cursor moves), or null (undefined).</summary>
    [IdlMember("key", "https://w3c.github.io/IndexedDB/#dom-idbcursor-key")]
    public object? Key => CurrentKey is null ? null : _keyValue ??= Operations.ConvertToValue(CurrentKey);

    /// <summary>Gets the effective key of the cursor (the same value until the cursor moves), or null (undefined).</summary>
    [IdlMember("primaryKey", "https://w3c.github.io/IndexedDB/#dom-idbcursor-primarykey")]
    public object? PrimaryKey => EffectiveKey is not { } key ? null : _primaryKeyValue ??= Operations.ConvertToValue(key);

    /// <summary>Gets the request of the cursor.</summary>
    [IdlMember("request", "https://w3c.github.io/IndexedDB/#dom-idbcursor-request")]
    public IdbRequest Request => RequestObject;

    /// <summary>Advances the cursor by a number of records (https://w3c.github.io/IndexedDB/#dom-idbcursor-advance).</summary>
    [IdlMember("advance", "https://w3c.github.io/IndexedDB/#dom-idbcursor-advance")]
    public void Advance(uint count)
    {
        if (count == 0)
            throw new TypeErrorException("The count must be greater than 0");

        CheckActive();
        CheckNotDeleted();
        CheckGotValue();
        Iterate(key: null, primaryKey: null, count);
    }

    /// <summary>Advances the cursor to the next record, or to the next record at or after a key (https://w3c.github.io/IndexedDB/#dom-idbcursor-continue).</summary>
    [IdlMember("continue", "https://w3c.github.io/IndexedDB/#dom-idbcursor-continue")]
    public void Continue(object? key = null)
    {
        CheckActive();
        CheckNotDeleted();
        CheckGotValue();
        IdbKey? target = null;
        if (key is not null)
        {
            target = IdbOperations.ConvertToKey(Operations, key);
            var comparison = target.CompareTo(_position);
            if (comparison <= 0 && Direction is IdbCursorDirection.Next or IdbCursorDirection.NextUnique)
                throw new DomException("The key is less than or equal to the position of the cursor", "DataError");

            if (comparison >= 0 && Direction is IdbCursorDirection.Prev or IdbCursorDirection.PrevUnique)
                throw new DomException("The key is greater than or equal to the position of the cursor", "DataError");
        }

        Iterate(target, primaryKey: null, count: 1);
    }

    /// <summary>Advances the cursor of an index to a key and a primary key (https://w3c.github.io/IndexedDB/#dom-idbcursor-continueprimarykey).</summary>
    [IdlMember("continuePrimaryKey", "https://w3c.github.io/IndexedDB/#dom-idbcursor-continueprimarykey")]
    public void ContinuePrimaryKey(object? key, object? primaryKey)
    {
        CheckActive();
        CheckNotDeleted();
        if (IndexHandle is null)
            throw new DomException("The source of the cursor is not an index", "InvalidAccessError");

        if (Direction is not (IdbCursorDirection.Next or IdbCursorDirection.Prev))
            throw new DomException("The direction of the cursor is not 'next' or 'prev'", "InvalidAccessError");

        CheckGotValue();
        var targetKey = IdbOperations.ConvertToKey(Operations, key);
        var targetPrimaryKey = IdbOperations.ConvertToKey(Operations, primaryKey);
        var comparison = targetKey.CompareTo(_position);
        if (comparison < 0 && Direction == IdbCursorDirection.Next)
            throw new DomException("The key is less than the position of the cursor", "DataError");

        if (comparison > 0 && Direction == IdbCursorDirection.Prev)
            throw new DomException("The key is greater than the position of the cursor", "DataError");

        var primaryComparison = targetPrimaryKey.CompareTo(_objectStorePosition);
        if (comparison == 0 && primaryComparison <= 0 && Direction == IdbCursorDirection.Next)
            throw new DomException("The primary key is less than or equal to the object store position of the cursor", "DataError");

        if (comparison == 0 && primaryComparison >= 0 && Direction == IdbCursorDirection.Prev)
            throw new DomException("The primary key is greater than or equal to the object store position of the cursor", "DataError");

        Iterate(targetKey, targetPrimaryKey, count: 1);
    }

    /// <summary>Replaces the value of the record of the cursor (https://w3c.github.io/IndexedDB/#dom-idbcursor-update).</summary>
    [IdlMember("update", "https://w3c.github.io/IndexedDB/#dom-idbcursor-update")]
    public IdbRequest Update(object? value)
    {
        var transaction = TransactionObject;
        CheckActive();
        if (transaction.Mode == IdbTransactionMode.ReadOnly)
            throw IdbTransaction.ReadOnlyError();

        CheckNotDeleted();
        CheckGotValue();
        if (KeyOnly)
            throw DomException.InvalidState("The cursor is a key cursor");

        var operations = Operations;
        var clone = IdbOperations.Clone(transaction, value);
        var store = EffectiveObjectStore;
        var effectiveKey = EffectiveKey;
        if (store.KeyPath is { } keyPath)
        {
            var kpk = IdbOperations.ExtractKey(operations, clone, keyPath, multiEntry: false, out _);
            if (kpk is null || !kpk.Equals(effectiveKey))
                throw new DomException("The key of the value is not the key of the record of the cursor", "DataError");
        }

        return transaction.ExecuteRequest(this, backend => operations.ConvertToValue(IdbOperations.StoreRecord(backend, operations, store, clone, effectiveKey, noOverwrite: false)));
    }

    /// <summary>Deletes the record of the cursor (https://w3c.github.io/IndexedDB/#dom-idbcursor-delete).</summary>
    [IdlMember("delete", "https://w3c.github.io/IndexedDB/#dom-idbcursor-delete")]
    public IdbRequest Delete()
    {
        var transaction = TransactionObject;
        CheckActive();
        if (transaction.Mode == IdbTransactionMode.ReadOnly)
            throw IdbTransaction.ReadOnlyError();

        CheckNotDeleted();
        CheckGotValue();
        if (KeyOnly)
            throw DomException.InvalidState("The cursor is a key cursor");

        var id = EffectiveObjectStore.Id;
        var range = IndexedDBKeyRange.Only(EffectiveKey!.Encode());
        return transaction.ExecuteRequest(this, backend =>
        {
            backend.DeleteRecords(id, range);
            return null;
        });
    }

    /// <summary>
    /// https://w3c.github.io/IndexedDB/#iterate-a-cursor: moves the cursor to the next record (or to the record after
    /// <paramref name="count"/> records), and returns the cursor, or null (a JavaScript null) when there is no such record.
    /// </summary>
    internal object Iterate(IndexedDBBackendTransaction backend, IdbKey? key, IdbKey? primaryKey, uint count)
    {
        var position = _position;
        var objectStorePosition = _objectStorePosition;
        byte[]? foundKey;
        byte[]? foundPrimaryKey = null;
        byte[]? foundValue = null;
        while (count > 0)
        {
            if (!FindRecord(backend, key, primaryKey, position, objectStorePosition, out foundKey, out foundPrimaryKey, out foundValue))
            {
                SetKey(null, null);
                if (IndexHandle is not null)
                {
                    _objectStorePosition = null;
                    _primaryKeyValue = null;
                }

                if (!KeyOnly)
                {
                    CurrentValue = null;
                }

                return NullValue.Instance;
            }

            position = IdbKey.Decode(foundKey);
            if (IndexHandle is not null)
            {
                objectStorePosition = IdbKey.Decode(foundPrimaryKey);
            }

            key = null;
            primaryKey = null;
            count--;
        }

        _position = position;
        _primaryKeyValue = null;
        if (IndexHandle is not null)
        {
            _objectStorePosition = objectStorePosition;
        }

        SetKey(position, null);
        if (!KeyOnly)
        {
            var value = IndexHandle is null ? foundValue : IdbOperations.GetValue(backend, EffectiveObjectStore.Id, foundPrimaryKey!);
            CurrentValue = value is null ? null : IdbOperations.DeserializeValue(Operations, value);
        }

        GotValue = true;
        return this;
    }

    private void SetKey(IdbKey? key, object? value)
    {
        CurrentKey = key;
        _keyValue = value;
        if (key is null && IndexHandle is null)
        {
            _primaryKeyValue = null;
        }
    }

    // Finds the next record from the position (https://w3c.github.io/IndexedDB/#iterate-a-cursor)
    private bool FindRecord(IndexedDBBackendTransaction backend, IdbKey? key, IdbKey? primaryKey, IdbKey? position, IdbKey? objectStorePosition, out byte[] foundKey, out byte[]? foundPrimaryKey, out byte[]? foundValue)
    {
        foundKey = [];
        foundPrimaryKey = null;
        foundValue = null;
        var reverse = Direction is IdbCursorDirection.Prev or IdbCursorDirection.PrevUnique;
        var range = _range;
        if (key is not null)
        {
            range = IdbOperations.Restrict(range, key.Encode(), open: false, isUpper: reverse);
        }

        if (IndexHandle is not { } index)
        {
            if (position is not null)
            {
                range = IdbOperations.Restrict(range, position.Encode(), open: true, isUpper: reverse);
            }

            if (backend.GetRecords(EffectiveObjectStore.Id, range, reverse, limit: 1) is not [var record])
                return false;

            foundKey = record.Key;
            foundPrimaryKey = record.Key;
            foundValue = record.Value;
            return true;
        }

        IndexedDBIndexRecord? found;
        if (Direction is IdbCursorDirection.NextUnique or IdbCursorDirection.PrevUnique)
        {
            if (position is not null)
            {
                range = IdbOperations.Restrict(range, position.Encode(), open: true, isUpper: reverse);
            }

            found = IdbOperations.FindUnique(backend, index.Index.Id, range, reverse, range);
        }
        else
        {
            IndexedDBIndexPosition? start = null;
            if (key is not null)
            {
                if (primaryKey is not null)
                {
                    start = new IndexedDBIndexPosition(key.Encode(), primaryKey.Encode(), Inclusive: true);
                }
            }
            else if (position is not null)
            {
                range = IdbOperations.Restrict(range, position.Encode(), open: false, isUpper: reverse);
                start = new IndexedDBIndexPosition(position.Encode(), objectStorePosition!.Encode(), Inclusive: false);
            }

            found = backend.GetIndexRecords(index.Index.Id, range, reverse, start, limit: 1) is [var record] ? record : null;
        }

        if (found is not { } indexRecord)
            return false;

        foundKey = indexRecord.Key;
        foundPrimaryKey = indexRecord.PrimaryKey;
        return true;
    }

    // Moves the cursor asynchronously: the request is used again
    private void Iterate(IdbKey? key, IdbKey? primaryKey, uint count)
    {
        GotValue = false;
        var request = RequestObject;
        request.Processed = false;
        request.IsDone = false;
        TransactionObject.ExecuteRequest(Source, backend => Iterate(backend, key, primaryKey, count), request);
    }

    private void CheckActive()
    {
        if (!TransactionObject.IsActive)
            throw IdbTransaction.InactiveError();
    }

    private void CheckNotDeleted()
    {
        if (EffectiveObjectStore.Deleted || (IndexHandle is { } index && index.Index.Deleted))
            throw DomException.InvalidState("The source of the cursor has been deleted");
    }

    private void CheckGotValue()
    {
        if (!GotValue)
            throw DomException.InvalidState("The cursor is being iterated or has iterated past its end");
    }
}

/// <summary>A cursor that exposes the values of the records (https://w3c.github.io/IndexedDB/#idbcursorwithvalue).</summary>
[IdlInterface("IDBCursorWithValue", "https://w3c.github.io/IndexedDB/#idbcursorwithvalue")]
public sealed class IdbCursorWithValue : IdbCursor
{
    internal IdbCursorWithValue(IdbObjectStore objectStore, IdbIndex? index, IdbTransaction transaction, IdbCursorDirection direction, IndexedDBKeyRange range)
        : base(objectStore, index, transaction, direction, range)
    {
    }

    /// <summary>Gets the value of the record of the cursor, or null (undefined).</summary>
    [IdlMember("value", "https://w3c.github.io/IndexedDB/#dom-idbcursorwithvalue-value")]
    public object? Value => CurrentValue;
}
