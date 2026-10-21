namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A Map instance: an ordinary object with a [[MapData]] internal slot.</summary>
internal sealed class JsMap(JsObject prototype) : JsObject(prototype, ObjectClass.Map)
{
    /// <summary>[[MapData]]</summary>
    public OrderedHashTable Data { get; } = new(hasValues: true);
}

/// <summary>A Set instance: an ordinary object with a [[SetData]] internal slot.</summary>
internal sealed class JsSet : JsObject
{
    public JsSet(JsObject prototype, OrderedHashTable? data = null)
        : base(prototype, ObjectClass.Set)
    {
        Data = data ?? new OrderedHashTable(hasValues: false);
    }

    /// <summary>[[SetData]]</summary>
    public OrderedHashTable Data { get; }
}

/// <summary>The kind of a Map or Set iterator.</summary>
internal enum CollectionIteratorKind : byte
{
    Keys,
    Values,
    Entries,
}

/// <summary>
/// A Map Iterator or a Set Iterator (https://tc39.es/ecma262/#sec-map-iterator-objects): the generator of
/// CreateMapIterator / CreateSetIterator, whose closure is a cursor in the entries of the collection.
/// </summary>
internal sealed class JsCollectionIterator : JsObject
{
    private OrderedHashTable.Cursor _cursor;
    private bool _done;

    public JsCollectionIterator(JsObject prototype, OrderedHashTable table, CollectionIteratorKind kind, bool isMapIterator)
        : base(prototype)
    {
        _cursor = table.GetCursor();
        IterationKind = kind;
        IsMapIterator = isMapIterator;
    }

    public CollectionIteratorKind IterationKind { get; }

    /// <summary>Gets whether the [[GeneratorBrand]] is "%MapIteratorPrototype%" (else "%SetIteratorPrototype%").</summary>
    public bool IsMapIterator { get; }

    /// <summary>Resumes the closure: the next entry, or false once the iteration is complete (it stays complete).</summary>
    public bool MoveNext(out JsValue key, out JsValue value)
    {
        if (!_done)
        {
            if (_cursor.MoveNext(out key, out value))
                return true;

            _done = true;
            _cursor.Close();
        }

        key = default;
        value = default;
        return false;
    }
}
