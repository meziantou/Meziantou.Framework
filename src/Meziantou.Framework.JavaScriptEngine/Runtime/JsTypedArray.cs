namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A TypedArray exotic object (https://tc39.es/ecma262/#sec-typedarray-exotic-objects): [[ViewedArrayBuffer]],
/// [[TypedArrayName]], [[ContentType]], [[ByteLength]], [[ByteOffset]] and [[ArrayLength]]. The canonical numeric
/// string keys are the elements of the buffer, never ordinary properties. A length-tracking typed array (its
/// [[ArrayLength]] is auto) follows the byte length of a resizable buffer.
/// </summary>
internal sealed class JsTypedArray : JsObject, IArrayIteratorSource
{
    private const int Auto = -1;

    public JsTypedArray(JsObject? prototype, TypedArrayElementType elementType)
        : base(prototype, ObjectClass.TypedArray)
    {
        Flags |= ObjectFlags.Exotic;
        ElementType = elementType;
        ElementSize = BinaryDataOperations.GetElementSize(elementType);
    }

    /// <summary>Gets the element type (the [[TypedArrayName]] and the [[ContentType]]).</summary>
    public TypedArrayElementType ElementType { get; }

    /// <summary>Gets the size in bytes of an element.</summary>
    public int ElementSize { get; }

    /// <summary>Gets whether the [[ContentType]] is BigInt.</summary>
    public bool IsBigInt => BinaryDataOperations.IsBigIntElementType(ElementType);

    /// <summary>Gets the [[TypedArrayName]].</summary>
    public string TypedArrayName => BinaryDataOperations.GetTypedArrayName(ElementType);

    /// <summary>Gets the [[ViewedArrayBuffer]] (set by the initialization of the typed array).</summary>
    public JsArrayBuffer ViewedArrayBuffer { get; private set; } = null!;

    /// <summary>Gets the [[ByteOffset]].</summary>
    public int ByteOffset { get; private set; }

    /// <summary>Gets whether the [[ArrayLength]] is auto (the typed array tracks the length of a resizable buffer).</summary>
    public bool IsLengthTracking => FixedLength == Auto;

    /// <summary>Gets the [[ArrayLength]] of a typed array that is not length-tracking (-1 when it is auto).</summary>
    public int FixedLength { get; private set; }

    internal override string ClassName => TypedArrayName;

    /// <summary>Sets the internal slots: the length is null for a length-tracking typed array.</summary>
    public void Initialize(JsArrayBuffer buffer, int byteOffset, int? length)
    {
        ViewedArrayBuffer = buffer;
        ByteOffset = byteOffset;
        FixedLength = length ?? Auto;
    }

    /// <summary>
    /// Creates a typed array of the realm viewing a buffer (the byte offset and the length must be valid; the length is
    /// null for a length-tracking typed array).
    /// </summary>
    public static JsTypedArray Create(JsRealm realm, TypedArrayElementType elementType, JsArrayBuffer buffer, int byteOffset, int? length)
    {
        var typedArray = new JsTypedArray(realm.Intrinsics.GetTypedArrayPrototype(elementType), elementType);
        typedArray.Initialize(buffer, byteOffset, length);
        return typedArray;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Bounds (https://tc39.es/ecma262/#sec-typedarray-with-buffer-witness-records)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// MakeTypedArrayWithBufferWitnessRecord + IsTypedArrayOutOfBounds + TypedArrayLength: false when the typed array is
    /// out of bounds (or its buffer is detached); otherwise the length. The byte length of the buffer is read once.
    /// </summary>
    public bool TryGetLength(out int length)
    {
        var buffer = ViewedArrayBuffer;
        if (buffer.IsDetached)
        {
            length = 0;
            return false;
        }

        var bufferByteLength = buffer.ByteLength;
        var byteOffsetStart = ByteOffset;
        if (byteOffsetStart > bufferByteLength)
        {
            length = 0;
            return false;
        }

        var fixedLength = FixedLength;
        if (fixedLength == Auto)
        {
            length = (bufferByteLength - byteOffsetStart) / ElementSize;
            return true;
        }

        if (byteOffsetStart + ((long)fixedLength * ElementSize) > bufferByteLength)
        {
            length = 0;
            return false;
        }

        length = fixedLength;
        return true;
    }

    /// <summary>IsTypedArrayOutOfBounds(MakeTypedArrayWithBufferWitnessRecord(O))</summary>
    public bool IsOutOfBounds => !TryGetLength(out _);

    /// <summary>TypedArrayLength, or 0 when the typed array is out of bounds (the length getter).</summary>
    public int GetLengthOrZero() => TryGetLength(out var length) ? length : 0;

    /// <summary>TypedArrayByteLength(taRecord): 0 when the typed array is out of bounds.</summary>
    public int GetByteLengthOrZero() => TryGetLength(out var length) ? length * ElementSize : 0;

    /// <summary>ValidateTypedArray(O, order) once the internal slot is checked: the length, or a TypeError when the typed array is out of bounds.</summary>
    public int GetLengthOrThrow()
    {
        if (!TryGetLength(out var length))
            return ThrowHelper.ThrowTypeError<int>("Cannot perform the operation on a detached or out-of-bounds TypedArray");

        return length;
    }

    /// <summary>The length of the typed array for the next step of an Array Iterator: a TypeError when it is out of bounds.</summary>
    public long GetIteratorLength() => GetLengthOrThrow();

    /// <summary>Gets the bytes of the elements (empty when the typed array is out of bounds). It does not run JavaScript code.</summary>
    public Span<byte> GetSpan()
    {
        if (!TryGetLength(out var length))
            return [];

        return ViewedArrayBuffer.GetSpan().Slice(ByteOffset, length * ElementSize);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Elements
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>IsValidIntegerIndex(O, index)</summary>
    public bool IsValidIntegerIndex(double index, out int validIndex)
    {
        validIndex = 0;
        if (ViewedArrayBuffer.IsDetached)
            return false;

        if (index != Math.Floor(index) || double.IsInfinity(index))
            return false;

        if (index == 0 && double.IsNegative(index))
            return false;

        if (!TryGetLength(out var length))
            return false;

        if (index < 0 || index >= length)
            return false;

        validIndex = (int)index;
        return true;
    }

    /// <summary>IsValidIntegerIndex(O, index) for an array index.</summary>
    public bool IsValidIntegerIndex(uint index) => TryGetLength(out var length) && index < (uint)length;

    /// <summary>Reads the element at a valid index (GetValueFromBuffer with the byte order of the agent).</summary>
    public JsValue GetElement(int index)
    {
        var span = ViewedArrayBuffer.GetSpan();
        return BinaryDataOperations.RawBytesToNumeric(ElementType, span[(ByteOffset + (index * ElementSize))..], BinaryDataOperations.IsLittleEndian);
    }

    /// <summary>Writes a converted value (a number, or a BigInt) at a valid index.</summary>
    public void SetElement(int index, JsValue numericValue)
    {
        var span = ViewedArrayBuffer.GetSpan();
        BinaryDataOperations.NumericToRawBytes(ElementType, numericValue, span[(ByteOffset + (index * ElementSize))..], BinaryDataOperations.IsLittleEndian);
    }

    /// <summary>TypedArrayGetElement(O, index): undefined when the index is not a valid integer index.</summary>
    public JsValue GetElementOrUndefined(double index)
    {
        if (!IsValidIntegerIndex(index, out var validIndex))
            return JsValue.Undefined;

        return GetElement(validIndex);
    }

    /// <summary>TypedArraySetElement(O, index, value): converts the value, then writes it when the index is a valid integer index.</summary>
    public void SetElementFromValue(double index, JsValue value)
    {
        var numericValue = BinaryDataOperations.ToNumericForElementType(ElementType, value);
        if (IsValidIntegerIndex(index, out var validIndex))
        {
            SetElement(validIndex, numericValue);
        }
    }

    /// <summary>Gets the numeric index of a property key: CanonicalNumericIndexString for a string key.</summary>
    internal static bool TryGetNumericIndex(in PropertyKey key, out double index)
    {
        if (key.IsArrayIndex)
        {
            index = key.ArrayIndex;
            return true;
        }

        if (key.IsSymbol)
        {
            index = 0;
            return false;
        }

        return CanonicalNumericIndexString(key.AsString(), out index);
    }

    /// <summary>CanonicalNumericIndexString(argument): the number whose ToString is the string, or -0 for "-0".</summary>
    internal static bool CanonicalNumericIndexString(string value, out double index)
    {
        index = 0;
        if (value.Length == 0)
            return false;

        // The first character of a canonical numeric string is a digit, '-', 'I' (Infinity) or 'N' (NaN)
        var first = value[0];
        if (first is not ((>= '0' and <= '9') or '-' or 'I' or 'N'))
            return false;

        if (value == "-0")
        {
            index = -0.0;
            return true;
        }

        var number = NumberParsing.StringToNumber(value);
        if (!string.Equals(NumberFormatting.ToString(number), value, StringComparison.Ordinal))
            return false;

        index = number;
        return true;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Internal methods
    // ------------------------------------------------------------------------------------------------------------------

    internal override bool GetOwnProperty(in PropertyKey key, out PropertyDescriptor descriptor)
    {
        if (key.IsArrayIndex)
        {
            if (IsValidIntegerIndex(key.ArrayIndex))
            {
                descriptor = PropertyDescriptor.Data(GetElement((int)key.ArrayIndex), PropertyFlags.Default);
                return true;
            }

            descriptor = default;
            return false;
        }

        if (TryGetNumericIndex(key, out var index))
        {
            if (IsValidIntegerIndex(index, out var validIndex))
            {
                descriptor = PropertyDescriptor.Data(GetElement(validIndex), PropertyFlags.Default);
                return true;
            }

            descriptor = default;
            return false;
        }

        return OrdinaryGetOwnProperty(key, out descriptor);
    }

    internal override bool HasProperty(in PropertyKey key)
    {
        if (TryGetNumericIndex(key, out var index))
            return IsValidIntegerIndex(index, out _);

        return OrdinaryHasProperty(key);
    }

    internal override bool DefineOwnProperty(in PropertyKey key, in PropertyDescriptor descriptor)
    {
        if (TryGetNumericIndex(key, out var index))
        {
            if (!IsValidIntegerIndex(index, out _))
                return false;

            if (descriptor.HasConfigurable && !descriptor.Configurable)
                return false;

            if (descriptor.HasEnumerable && !descriptor.Enumerable)
                return false;

            if (descriptor.IsAccessorDescriptor)
                return false;

            if (descriptor.HasWritable && !descriptor.Writable)
                return false;

            if (descriptor.HasValue)
            {
                SetElementFromValue(index, descriptor.Value);
            }

            return true;
        }

        return OrdinaryDefineOwnProperty(key, descriptor);
    }

    internal override JsValue Get(in PropertyKey key, JsValue receiver)
    {
        if (key.IsArrayIndex)
            return IsValidIntegerIndex(key.ArrayIndex) ? GetElement((int)key.ArrayIndex) : JsValue.Undefined;

        if (TryGetNumericIndex(key, out var index))
            return GetElementOrUndefined(index);

        return OrdinaryGet(key, receiver);
    }

    internal override bool Set(in PropertyKey key, JsValue value, JsValue receiver)
    {
        if (key.IsArrayIndex && ReferenceEquals(receiver.Cell, this))
        {
            var numericValue = BinaryDataOperations.ToNumericForElementType(ElementType, value);
            if (IsValidIntegerIndex(key.ArrayIndex))
            {
                SetElement((int)key.ArrayIndex, numericValue);
            }

            return true;
        }

        if (TryGetNumericIndex(key, out var index))
        {
            if (ReferenceEquals(receiver.Cell, this))
            {
                SetElementFromValue(index, value);
                return true;
            }

            if (!IsValidIntegerIndex(index, out _))
                return true;
        }

        return OrdinarySet(key, value, receiver);
    }

    internal override bool Delete(in PropertyKey key)
    {
        if (TryGetNumericIndex(key, out var index))
            return !IsValidIntegerIndex(index, out _);

        return OrdinaryDelete(key);
    }

    internal override List<PropertyKey> OwnPropertyKeys()
    {
        var ordinaryKeys = OrdinaryOwnPropertyKeys();
        if (!TryGetLength(out var length) || length == 0)
            return ordinaryKeys;

        var keys = new List<PropertyKey>(length + ordinaryKeys.Count);
        for (var i = 0; i < length; i++)
        {
            keys.Add(new PropertyKey((uint)i));
        }

        keys.AddRange(ordinaryKeys);
        return keys;
    }
}
