namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A key range (https://w3c.github.io/IndexedDB/#idbkeyrange).</summary>
[IdlInterface("IDBKeyRange", "https://w3c.github.io/IndexedDB/#idbkeyrange")]
public sealed class IdbKeyRange
{
    private readonly IIndexedDBValueOperations _operations;

    internal IdbKeyRange(IIndexedDBValueOperations operations, IdbKey? lower, IdbKey? upper, bool lowerOpen, bool upperOpen)
    {
        _operations = operations;
        LowerKey = lower;
        UpperKey = upper;
        LowerOpen = lowerOpen;
        UpperOpen = upperOpen;
    }

    internal IdbKey? LowerKey { get; }

    internal IdbKey? UpperKey { get; }

    /// <summary>Gets the lower bound (a new value each time), or null (undefined) when the range has no lower bound.</summary>
    [IdlMember("lower", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-lower")]
    public object? Lower => LowerKey is null ? null : _operations.ConvertToValue(LowerKey);

    /// <summary>Gets the upper bound (a new value each time), or null (undefined) when the range has no upper bound.</summary>
    [IdlMember("upper", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-upper")]
    public object? Upper => UpperKey is null ? null : _operations.ConvertToValue(UpperKey);

    /// <summary>Gets whether the lower bound is excluded.</summary>
    [IdlMember("lowerOpen", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-loweropen")]
    public bool LowerOpen { get; }

    /// <summary>Gets whether the upper bound is excluded.</summary>
    [IdlMember("upperOpen", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-upperopen")]
    public bool UpperOpen { get; }

    /// <summary>Creates a range containing only a key (https://w3c.github.io/IndexedDB/#dom-idbkeyrange-only).</summary>
    [IdlMember("only", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-only")]
    public static IdbKeyRange Only(object? value)
    {
        var operations = IndexedDBStorage.GetOperations(GlobalScope.Current);
        var key = IdbOperations.ConvertToKey(operations, value);
        return new IdbKeyRange(operations, key, key, lowerOpen: false, upperOpen: false);
    }

    /// <summary>Creates a range with a lower bound (https://w3c.github.io/IndexedDB/#dom-idbkeyrange-lowerbound).</summary>
    [IdlMember("lowerBound", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-lowerbound")]
    public static IdbKeyRange LowerBound(object? lower, bool open = false)
    {
        var operations = IndexedDBStorage.GetOperations(GlobalScope.Current);
        var key = IdbOperations.ConvertToKey(operations, lower);
        return new IdbKeyRange(operations, key, upper: null, open, upperOpen: true);
    }

    /// <summary>Creates a range with an upper bound (https://w3c.github.io/IndexedDB/#dom-idbkeyrange-upperbound).</summary>
    [IdlMember("upperBound", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-upperbound")]
    public static IdbKeyRange UpperBound(object? upper, bool open = false)
    {
        var operations = IndexedDBStorage.GetOperations(GlobalScope.Current);
        var key = IdbOperations.ConvertToKey(operations, upper);
        return new IdbKeyRange(operations, lower: null, key, lowerOpen: true, open);
    }

    /// <summary>Creates a range with a lower and an upper bound (https://w3c.github.io/IndexedDB/#dom-idbkeyrange-bound).</summary>
    [IdlMember("bound", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-bound")]
    public static IdbKeyRange Bound(object? lower, object? upper, bool lowerOpen = false, bool upperOpen = false)
    {
        var operations = IndexedDBStorage.GetOperations(GlobalScope.Current);
        var lowerKey = IdbOperations.ConvertToKey(operations, lower);
        var upperKey = IdbOperations.ConvertToKey(operations, upper);
        if (lowerKey.CompareTo(upperKey) > 0)
            throw new DomException("The lower bound is greater than the upper bound", "DataError");

        return new IdbKeyRange(operations, lowerKey, upperKey, lowerOpen, upperOpen);
    }

    /// <summary>Returns whether a key is in the range (https://w3c.github.io/IndexedDB/#dom-idbkeyrange-includes).</summary>
    [IdlMember("includes", "https://w3c.github.io/IndexedDB/#dom-idbkeyrange-includes")]
    public bool Includes(object? key)
    {
        var k = IdbOperations.ConvertToKey(_operations, key);
        return ToBackendRange().Contains(k.Encode());
    }

    /// <summary>The range of encoded keys.</summary>
    internal IndexedDBKeyRange ToBackendRange() => new(LowerKey?.Encode(), UpperKey?.Encode(), LowerOpen, UpperOpen);
}
