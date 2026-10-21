namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An Iterator Record (https://tc39.es/ecma262/#sec-iterator-records).</summary>
/// <remarks>
/// The iteration of an array whose iteration is not observable (<see cref="Intrinsics.IsArrayIterationIntact"/>) does not
/// create the array iterator: the record reads the elements itself. The iterator object is created when it is needed
/// (<see cref="Iterator"/>), at the position of the iteration.
/// </remarks>
internal sealed class IteratorRecord
{
    private JsObject? _iterator;
    private JsValue _nextMethod;

    // The iteration of an array without its iterator object
    private readonly JsArray? _array;
    private readonly JsRealm? _realm;
    private long _index;

    public IteratorRecord(JsObject iterator, JsValue nextMethod)
    {
        _iterator = iterator;
        _nextMethod = nextMethod;
    }

    /// <summary>Creates the record of the iteration of an array by %Array.prototype.values% of a realm (no iterator object yet).</summary>
    public IteratorRecord(JsArray array, JsRealm realm)
    {
        _array = array;
        _realm = realm;
    }

    /// <summary>Gets the iterator object (created at the position of the iteration of an array).</summary>
    public JsObject Iterator => _iterator ?? CreateArrayIterator();

    public JsValue NextMethod
    {
        get
        {
            if (_iterator is null)
            {
                CreateArrayIterator();
            }

            return _nextMethod;
        }
    }

    public bool Done { get; set; }

    /// <summary>Gets whether the record iterates an array without an iterator object.</summary>
    public bool IsArrayIteration => _iterator is null;

    /// <summary>Gets the realm of the iteration of an array.</summary>
    public JsRealm? ArrayRealm => _realm;

    /// <summary>The step of %ArrayIteratorPrototype%.next for an array iteration: false when the array is done.</summary>
    public bool StepArray(out JsValue value)
    {
        var array = _array!;
        if (_index >= array.Length)
        {
            value = default;
            return false;
        }

        value = ArrayOperations.GetElement(array, _index);
        _index++;
        return true;
    }

    private JsArrayIterator CreateArrayIterator()
    {
        var iterator = JsArrayIterator.Create(_realm!, _array!, ArrayIteratorKind.Values);
        iterator.NextIndex = _index;
        if (Done)
        {
            iterator.Iterated = null;
        }

        _iterator = iterator;
        _nextMethod = _realm!.Intrinsics.ArrayIteratorNext;
        return iterator;
    }
}
