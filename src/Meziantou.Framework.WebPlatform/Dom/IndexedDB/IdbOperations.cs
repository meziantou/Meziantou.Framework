using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The conversions (https://w3c.github.io/IndexedDB/#binding) and the database operations (https://w3c.github.io/IndexedDB/#database-operations).</summary>
internal static class IdbOperations
{
    /// <summary>The maximum key of a key generator: 2^53.</summary>
    private const long MaxGeneratedKey = 9007199254740992;

    /// <summary>Converts a value to a key, or throws a <c>DataError</c> when it is not a valid key.</summary>
    public static IdbKey ConvertToKey(IIndexedDBValueOperations operations, object? value)
        => operations.ConvertToKey(value) ?? throw new DomException("The value is not a valid key", "DataError");

    /// <summary>https://w3c.github.io/IndexedDB/#convert-a-value-to-a-key-range</summary>
    public static IndexedDBKeyRange ConvertToKeyRange(IIndexedDBValueOperations operations, object? value, bool nullDisallowed = false)
    {
        if (value is IdbKeyRange range)
            return range.ToBackendRange();

        if (operations.IsNullOrUndefined(value))
        {
            if (nullDisallowed)
                throw new DomException("The key or key range is null", "DataError");

            return IndexedDBKeyRange.All;
        }

        return IndexedDBKeyRange.Only(ConvertToKey(operations, value).Encode());
    }

    /// <summary>https://w3c.github.io/IndexedDB/#is-a-potentially-valid-key-range</summary>
    public static bool IsPotentiallyValidKeyRange(IIndexedDBValueOperations operations, object? value) => value is IdbKeyRange || operations.IsPotentiallyValidKey(value);

    /// <summary>
    /// https://w3c.github.io/IndexedDB/#extract-a-key-from-a-value-using-a-key-path: returns the key, or null with
    /// <paramref name="failure"/> true when the key path does not match, or null when the key is invalid.
    /// </summary>
    public static IdbKey? ExtractKey(IIndexedDBValueOperations operations, object? value, IndexedDBKeyPath keyPath, bool multiEntry, out bool failure)
    {
        failure = false;
        if (keyPath.Paths is { } paths)
        {
            var keys = new IdbKey[paths.Count];
            var values = new object?[paths.Count];
            for (var i = 0; i < paths.Count; i++)
            {
                if (!operations.TryEvaluateKeyPath(value, paths[i], out values[i]))
                {
                    failure = true;
                    return null;
                }
            }

            for (var i = 0; i < paths.Count; i++)
            {
                if (operations.ConvertToKey(values[i]) is not { } key)
                    return null;

                keys[i] = key;
            }

            return IdbKey.FromArray(keys);
        }

        if (!operations.TryEvaluateKeyPath(value, keyPath.Path!, out var result))
        {
            failure = true;
            return null;
        }

        return operations.ConvertToKey(result, multiEntry);
    }

    /// <summary>https://w3c.github.io/IndexedDB/#clone-a-value</summary>
    public static object? Clone(IdbTransaction transaction, object? value)
        => transaction.RunInactive(() => transaction.Operations.Deserialize(transaction.Operations.SerializeForStorage(value)));

    /// <summary>https://w3c.github.io/IndexedDB/#store-a-record-into-an-object-store</summary>
    public static IdbKey StoreRecord(IndexedDBBackendTransaction backend, IIndexedDBValueOperations operations, ObjectStoreInfo store, object? value, IdbKey? key, bool noOverwrite)
    {
        long? newCurrentNumber = null;
        if (store.AutoIncrement)
        {
            var current = backend.GetKeyGeneratorCurrentNumber(store.Id);
            if (key is null)
            {
                // https://w3c.github.io/IndexedDB/#generate-a-key
                if (current > MaxGeneratedKey)
                    throw new DomException("The key generator has reached its maximum value", "ConstraintError");

                key = IdbKey.FromNumber(current);
                newCurrentNumber = current + 1;
                if (store.KeyPath is { Path: { } path })
                {
                    operations.InjectKey(value, key, path);
                }
            }
            else if (key.Type == IdbKeyType.Number)
            {
                // https://w3c.github.io/IndexedDB/#possibly-update-the-key-generator
                var number = Math.Floor(Math.Min(key.Number, MaxGeneratedKey));
                if (number >= current)
                {
                    newCurrentNumber = (long)number + 1;
                }
            }
        }

        var encodedKey = key!.Encode();
        if (noOverwrite && backend.CountRecords(store.Id, IndexedDBKeyRange.Only(encodedKey)) > 0)
            throw new DomException("A record with the same key already exists", "ConstraintError");

        var indexKeys = new List<IndexedDBIndexKey>();
        foreach (var index in store.BackendIndexes)
        {
            IdbKey? indexKey;
            try
            {
                indexKey = ExtractKey(operations, value, index.KeyPath, index.MultiEntry, out _);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The exceptions are not rethrown: the record has no key in the index
                continue;
            }

            if (indexKey is null)
                continue;

            var subkeys = index.MultiEntry && indexKey.Type == IdbKeyType.Array ? indexKey.Items! : [indexKey];
            foreach (var subkey in subkeys)
            {
                var encodedIndexKey = subkey.Encode();
                if (index.Unique && HasIndexRecord(backend, index, encodedIndexKey, exceptPrimaryKey: encodedKey))
                    throw new DomException($"The index '{index.Name}' already has a record with the same key", "ConstraintError");

                indexKeys.Add(new IndexedDBIndexKey(index.Id, encodedIndexKey));
            }
        }

        var serialized = StructuredValueEncoding.Encode(operations.SerializeForStorage(value));
        if (newCurrentNumber is { } number2)
        {
            backend.SetKeyGeneratorCurrentNumber(store.Id, number2);
        }

        backend.PutRecord(store.Id, encodedKey, serialized, indexKeys);
        return key;
    }

    /// <summary>Whether an index has a record with a key, other than the records of the record <paramref name="exceptPrimaryKey"/> (which is replaced).</summary>
    private static bool HasIndexRecord(IndexedDBBackendTransaction backend, IndexInfo index, byte[] key, byte[] exceptPrimaryKey)
    {
        foreach (var record in backend.GetIndexRecords(index.Id, IndexedDBKeyRange.Only(key), reverse: false, start: null, limit: 2))
        {
            if (!record.PrimaryKey.AsSpan().SequenceEqual(exceptPrimaryKey))
                return true;
        }

        return false;
    }

    /// <summary>Deserializes the value of a record in the realm of the request.</summary>
    public static object? DeserializeValue(IIndexedDBValueOperations operations, byte[] value)
    {
        AgentSerializedValue serialized;
        try
        {
            serialized = StructuredValueEncoding.Decode(value);
        }
        catch (FormatException ex)
        {
            throw new DomException("The value could not be read: " + ex.Message, "NotReadableError");
        }

        return operations.Deserialize(serialized);
    }

    /// <summary>Returns the value of the record of an object store with a primary key, or null when the record does not exist.</summary>
    public static byte[]? GetValue(IndexedDBBackendTransaction backend, long objectStoreId, byte[] primaryKey)
        => backend.GetRecords(objectStoreId, IndexedDBKeyRange.Only(primaryKey), reverse: false, limit: 1) is [var record] ? record.Value : null;

    /// <summary>Restricts a range to the keys greater (or lower with <paramref name="isUpper"/>) than a bound.</summary>
    public static IndexedDBKeyRange Restrict(IndexedDBKeyRange range, byte[] bound, bool open, bool isUpper)
    {
        if (!isUpper)
        {
            if (range.Lower is not null)
            {
                var comparison = IdbKey.Compare(range.Lower, bound);
                if (comparison > 0 || (comparison == 0 && range.LowerOpen))
                    return range;
            }

            return range with { Lower = bound, LowerOpen = open };
        }

        if (range.Upper is not null)
        {
            var comparison = IdbKey.Compare(range.Upper, bound);
            if (comparison < 0 || (comparison == 0 && range.UpperOpen))
                return range;
        }

        return range with { Upper = bound, UpperOpen = open };
    }

    /// <summary>Converts the queryOrOptions argument of getAll() and getAllKeys() (https://w3c.github.io/IndexedDB/#create-a-request-to-retrieve-multiple-items).</summary>
    public static (IndexedDBKeyRange Range, uint? Count, IdbCursorDirection Direction) ConvertGetAllArguments(IIndexedDBValueOperations operations, object? queryOrOptions, uint? count)
    {
        // undefined and null are the unbounded key range, with the count argument (as in the browsers)
        if (operations.IsNullOrUndefined(queryOrOptions) || IsPotentiallyValidKeyRange(operations, queryOrOptions))
            return (ConvertToKeyRange(operations, queryOrOptions), count, IdbCursorDirection.Next);

        var options = queryOrOptions as IdbGetAllOptions ?? operations.ConvertGetAllOptions(queryOrOptions);
        return (ConvertToKeyRange(operations, options.Query), options.Count, options.Direction);
    }

    /// <summary>Converts the records to a JavaScript array (https://w3c.github.io/IndexedDB/#retrieve-multiple-items-from-an-object-store).</summary>
    public static object? RetrieveMultipleFromObjectStore(IndexedDBBackendTransaction backend, IIndexedDBValueOperations operations, ObjectStoreInfo store, IndexedDBKeyRange range, string kind, IdbCursorDirection direction, uint? count)
    {
        var limit = count is null or 0 ? int.MaxValue : (int)Math.Min(count.Value, int.MaxValue);
        var reverse = direction is IdbCursorDirection.Prev or IdbCursorDirection.PrevUnique;
        var records = backend.GetRecords(store.Id, range, reverse, limit);
        var list = new List<object?>(records.Count);
        foreach (var record in records)
        {
            switch (kind)
            {
                case "key":
                    list.Add(operations.ConvertToValue(IdbKey.Decode(record.Key)));
                    break;
                case "value":
                    list.Add(DeserializeValue(operations, record.Value));
                    break;
                default:
                    var key = IdbKey.Decode(record.Key);
                    list.Add(new IdbRecord(operations, key, key, DeserializeValue(operations, record.Value)));
                    break;
            }
        }

        return operations.CreateArray(list);
    }

    /// <summary>https://w3c.github.io/IndexedDB/#retrieve-multiple-items-from-an-index</summary>
    public static object? RetrieveMultipleFromIndex(IndexedDBBackendTransaction backend, IIndexedDBValueOperations operations, IndexInfo index, IndexedDBKeyRange range, string kind, IdbCursorDirection direction, uint? count)
    {
        var limit = count is null or 0 ? int.MaxValue : (int)Math.Min(count.Value, int.MaxValue);
        IReadOnlyList<IndexedDBIndexRecord> records;
        switch (direction)
        {
            case IdbCursorDirection.Next:
            case IdbCursorDirection.Prev:
                records = backend.GetIndexRecords(index.Id, range, direction == IdbCursorDirection.Prev, start: null, limit);
                break;
            default:
            {
                var unique = new List<IndexedDBIndexRecord>();
                var current = range;
                while (unique.Count < limit && FindUnique(backend, index.Id, current, direction == IdbCursorDirection.PrevUnique, range) is { } record)
                {
                    unique.Add(record);
                    current = Restrict(current, record.Key, open: true, isUpper: direction == IdbCursorDirection.PrevUnique);
                }

                records = unique;
                break;
            }
        }

        var list = new List<object?>(records.Count);
        foreach (var record in records)
        {
            switch (kind)
            {
                case "key":
                    list.Add(operations.ConvertToValue(IdbKey.Decode(record.PrimaryKey)));
                    break;
                case "value":
                    list.Add(GetValue(backend, index.Store.Id, record.PrimaryKey) is { } value ? DeserializeValue(operations, value) : null);
                    break;
                default:
                    var referenced = GetValue(backend, index.Store.Id, record.PrimaryKey);
                    list.Add(new IdbRecord(operations, IdbKey.Decode(record.Key), IdbKey.Decode(record.PrimaryKey), referenced is null ? null : DeserializeValue(operations, referenced)));
                    break;
            }
        }

        return operations.CreateArray(list);
    }

    /// <summary>
    /// Returns the first record of the first key of an index in a range (or of the last key, with <paramref name="reverse"/>):
    /// the record with the lowest primary key of the key.
    /// </summary>
    public static IndexedDBIndexRecord? FindUnique(IndexedDBBackendTransaction backend, long indexId, IndexedDBKeyRange range, bool reverse, IndexedDBKeyRange fullRange)
    {
        if (backend.GetIndexRecords(indexId, range, reverse, start: null, limit: 1) is not [var record])
            return null;

        if (!reverse)
            return record;

        // The first record of the key in the order of the primary keys
        return backend.GetIndexRecords(indexId, IndexedDBKeyRange.Only(record.Key), reverse: false, start: null, limit: 1) is [var first] && fullRange.Contains(first.Key) ? first : record;
    }
}
