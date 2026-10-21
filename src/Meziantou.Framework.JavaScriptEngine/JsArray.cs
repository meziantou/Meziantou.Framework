namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>An Array exotic object (https://tc39.es/ecma262/#sec-array-exotic-objects): <c>length</c> follows the indices.</summary>
public sealed class JsArray : JsObject
{
    private uint _length;
    private bool _lengthWritable = true;

    internal JsArray(JsObject? prototype, uint length = 0)
        : base(prototype, ObjectClass.Array)
    {
        _length = length;
        Flags |= ObjectFlags.Exotic;
    }

    /// <summary>Gets the value of the <c>length</c> property.</summary>
    public uint Length => _length;

    internal bool IsLengthWritable => _lengthWritable;

    private static readonly PropertyKey LengthKey = new("length");

    /// <summary>Creates an array whose elements are the values (CreateArrayFromList).</summary>
    internal static JsArray Create(JsRealm realm, ReadOnlySpan<JsValue> values)
    {
        var array = new JsArray(realm.Intrinsics.ArrayPrototype);
        for (var i = 0; i < values.Length; i++)
        {
            array.SetOwnSlot(new PropertyKey((uint)i), values[i], PropertyFlags.Default);
        }

        array._length = (uint)values.Length;
        return array;
    }

    /// <summary>
    /// CreateDataProperty(A, index, value) when the array is extensible and all its indices are in the dense elements
    /// (they are writable, enumerable and configurable): false when the general algorithm must be used.
    /// </summary>
    internal bool TryCreateDenseElement(long index, JsValue value)
    {
        if ((ulong)index >= uint.MaxValue || !IsExtensibleSlot || !HasOnlyDenseElements || (index >= _length && !_lengthWritable))
            return false;

        SetOwnSlot(new PropertyKey((uint)index), value, PropertyFlags.Default);
        if (index >= _length)
        {
            _length = (uint)index + 1;
        }

        return true;
    }

    /// <summary>Appends a value at the end of the array, as CreateDataProperty(A, length, value) does on a fresh array.</summary>
    internal void Push(JsValue value)
    {
        SetOwnSlot(new PropertyKey(_length), value, PropertyFlags.Default);
        _length++;
    }

    internal override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (key == LengthKey)
        {
            descriptor = PropertyDescriptor.Data(_length, _lengthWritable ? PropertyFlags.Writable : PropertyFlags.None);
            return true;
        }

        return OrdinaryGetOwnProperty(key, out descriptor);
    }

    internal override JsValue Get(in PropertyKey key, JsValue receiver)
    {
        if (key.IsArrayIndex && TryGetDenseElement(key.ArrayIndex, out var element))
            return element;

        if (key == LengthKey)
            return _length;

        return OrdinaryGet(key, receiver);
    }

    internal override bool Set(in PropertyKey key, JsValue value, JsValue receiver)
    {
        if (key.IsArrayIndex && receiver.Cell == this)
        {
            // Fast path: an existing element of the dense storage
            var index = key.ArrayIndex;
            if (TryReplaceDenseElement(index, value))
                return true;

            // A hole or a new element (a[a.length] = x, fill): when no prototype has an element (no setter, no read-only
            // element), [[Set]] creates an ordinary data property
            if (ArrayOperations.PrototypesHaveNoElements(this) && TryCreateDenseElement(index, value))
                return true;
        }

        return OrdinarySet(key, value, receiver);
    }

    internal override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        if (key == LengthKey)
            return ArraySetLength(descriptor);

        if (key.IsArrayIndex)
        {
            var index = key.ArrayIndex;
            if (index >= _length && !_lengthWritable)
                return false;

            if (!OrdinaryDefineOwnProperty(key, descriptor))
                return false;

            if (index >= _length)
            {
                _length = index + 1;
            }

            return true;
        }

        return OrdinaryDefineOwnProperty(key, descriptor);
    }

    internal override bool Delete(in PropertyKey key)
    {
        if (key == LengthKey)
            return false;

        return OrdinaryDelete(key);
    }

    internal override List<PropertyKey> OwnPropertyKeys()
    {
        var keys = OrdinaryOwnPropertyKeys();
        var position = 0;
        while (position < keys.Count && keys[position].IsArrayIndex)
        {
            position++;
        }

        keys.Insert(position, LengthKey);
        return keys;
    }

    /// <summary>ArraySetLength(A, Desc) (https://tc39.es/ecma262/#sec-arraysetlength).</summary>
    private bool ArraySetLength(in PropertyDescriptor descriptor)
    {
        if (!descriptor.HasValue)
            return ValidateAndApplyLength(descriptor, _length);

        var newLength = JsOperations.ToUint32(descriptor.Value);
        var numberLength = JsOperations.ToNumber(descriptor.Value);
        if (newLength != numberLength)
        {
            ThrowHelper.ThrowRangeError("Invalid array length");
        }

        // The conversions may have changed the array (e.g. valueOf)
        if (newLength >= _length)
            return ValidateAndApplyLength(descriptor, newLength);

        if (!_lengthWritable)
            return false;

        var newWritable = !descriptor.HasWritable || descriptor.Writable;
        if (!ValidateLengthDescriptor(descriptor))
            return false;

        // Delete the elements from the end; stop at a non-configurable element. The dense elements are all configurable.
        if (HasOnlyDenseElements)
        {
            TruncateElements(newLength);
            _length = newLength;
            if (!newWritable)
            {
                _lengthWritable = false;
            }

            return true;
        }

        var keys = OrdinaryOwnPropertyKeys();
        for (var i = keys.Count - 1; i >= 0; i--)
        {
            var key = keys[i];
            if (!key.IsArrayIndex || key.ArrayIndex < newLength)
                continue;

            if (!OrdinaryDelete(key))
            {
                _length = key.ArrayIndex + 1;
                if (!newWritable)
                {
                    _lengthWritable = false;
                }

                return false;
            }
        }

        TruncateElements(newLength);
        _length = newLength;
        if (!newWritable)
        {
            _lengthWritable = false;
        }

        return true;
    }

    private bool ValidateLengthDescriptor(in PropertyDescriptor descriptor)
    {
        if (descriptor.IsAccessorDescriptor)
            return false;

        if (descriptor.HasConfigurable && descriptor.Configurable)
            return false;

        if (descriptor.HasEnumerable && descriptor.Enumerable)
            return false;

        if (!_lengthWritable && descriptor.HasWritable && descriptor.Writable)
            return false;

        return true;
    }

    private bool ValidateAndApplyLength(in PropertyDescriptor descriptor, uint newLength)
    {
        if (!ValidateLengthDescriptor(descriptor))
            return false;

        if (!_lengthWritable && newLength != _length)
            return false;

        _length = newLength;
        if (descriptor.HasWritable && !descriptor.Writable)
        {
            _lengthWritable = false;
        }

        return true;
    }
}
