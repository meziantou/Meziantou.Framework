using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The abstract operations on the binary data shared by ArrayBuffer, SharedArrayBuffer, DataView, the TypedArrays and
/// Atomics: the element types, RawBytesToNumeric, NumericToRawBytes, GetValueFromBuffer, SetValueInBuffer and the
/// conversions to the element types (https://tc39.es/ecma262/#sec-arraybuffer-objects).
/// </summary>
internal static class BinaryDataOperations
{
    /// <summary>The maximum byte length of a buffer (the maximum length of a .NET byte array); a larger allocation throws a RangeError.</summary>
    public const int MaxByteLength = 0x7FFFFFC7;

    /// <summary>The maximum array index plus one (2^53-1): the upper bound of ToIndex.</summary>
    public const double MaxSafeInteger = 9007199254740991;

    /// <summary>The [[LittleEndian]] field of the agent: the byte order of the TypedArrays and of Atomics.</summary>
    public static bool IsLittleEndian => BitConverter.IsLittleEndian;

    private static readonly string[] TypedArrayNames =
    [
        "Int8Array",
        "Uint8Array",
        "Uint8ClampedArray",
        "Int16Array",
        "Uint16Array",
        "Int32Array",
        "Uint32Array",
        "BigInt64Array",
        "BigUint64Array",
        "Float16Array",
        "Float32Array",
        "Float64Array",
    ];

    /// <summary>The number of element types.</summary>
    public const int ElementTypeCount = 12;

    /// <summary>The Element Size of the element type (https://tc39.es/ecma262/#table-the-typedarray-constructors).</summary>
    public static int GetElementSize(TypedArrayElementType type)
    {
        return type switch
        {
            TypedArrayElementType.Int8 or TypedArrayElementType.Uint8 or TypedArrayElementType.Uint8Clamped => 1,
            TypedArrayElementType.Int16 or TypedArrayElementType.Uint16 or TypedArrayElementType.Float16 => 2,
            TypedArrayElementType.Int32 or TypedArrayElementType.Uint32 or TypedArrayElementType.Float32 => 4,
            _ => 8,
        };
    }

    /// <summary>The [[TypedArrayName]] of the element type (e.g. "Int8Array").</summary>
    public static string GetTypedArrayName(TypedArrayElementType type) => TypedArrayNames[(int)type];

    /// <summary>IsBigIntElementType(type)</summary>
    public static bool IsBigIntElementType(TypedArrayElementType type) => type is TypedArrayElementType.BigInt64 or TypedArrayElementType.BigUint64;

    /// <summary>IsUnclampedIntegerElementType(type)</summary>
    public static bool IsUnclampedIntegerElementType(TypedArrayElementType type) => type is TypedArrayElementType.Int8 or TypedArrayElementType.Uint8 or TypedArrayElementType.Int16 or TypedArrayElementType.Uint16 or TypedArrayElementType.Int32 or TypedArrayElementType.Uint32;

    // ------------------------------------------------------------------------------------------------------------------
    // Conversions
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>The BigInt value of ToBigInt(argument).</summary>
    public static JsValue ToBigIntValue(JsValue value) => value.Cell is JsBigInt ? value : JsValue.FromBigInt(BigIntOperations.ToBigInt(value));

    /// <summary>The integer modulo 2^64, as a signed integer (BigInt.asIntN(64, n)).</summary>
    public static long BigIntToInt64(BigInteger value) => unchecked((long)BigIntToUInt64(value));

    /// <summary>The integer modulo 2^64 (BigInt.asUintN(64, n)).</summary>
    public static ulong BigIntToUInt64(BigInteger value)
    {
        if (value >= long.MinValue && value <= ulong.MaxValue)
        {
            return value.Sign < 0 ? unchecked((ulong)(long)value) : (ulong)value;
        }

        return (ulong)(value & ulong.MaxValue);
    }

    /// <summary>ToNumber or ToBigInt, depending on the content type of the element type.</summary>
    public static JsValue ToNumericForElementType(TypedArrayElementType type, JsValue value)
    {
        if (IsBigIntElementType(type))
            return ToBigIntValue(value);

        return value.IsNumber ? value : new JsValue(JsOperations.ToNumber(value));
    }

    /// <summary>ToUint8Clamp(argument), for a number.</summary>
    public static byte ToUint8Clamp(double value)
    {
        if (double.IsNaN(value) || value <= 0)
            return 0;

        if (value >= 255)
            return 255;

        return (byte)Math.Round(value, MidpointRounding.ToEven);
    }

    /// <summary>Converts a number to a binary16 value, rounding directly from the double (roundTiesToEven, no double rounding through a float).</summary>
    public static Half ToHalf(double value) => (Half)value;

    // ------------------------------------------------------------------------------------------------------------------
    // Raw bytes
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>RawBytesToNumeric(type, rawBytes, isLittleEndian): the bytes are at the start of the span.</summary>
    public static JsValue RawBytesToNumeric(TypedArrayElementType type, ReadOnlySpan<byte> bytes, bool isLittleEndian)
    {
        switch (type)
        {
            case TypedArrayElementType.Int8:
                return (sbyte)bytes[0];
            case TypedArrayElementType.Uint8:
            case TypedArrayElementType.Uint8Clamped:
                return bytes[0];
            case TypedArrayElementType.Int16:
                return isLittleEndian ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes);
            case TypedArrayElementType.Uint16:
                return isLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);
            case TypedArrayElementType.Int32:
                return isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes);
            case TypedArrayElementType.Uint32:
                return isLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);
            case TypedArrayElementType.BigInt64:
                return JsValue.FromBigInt(isLittleEndian ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes));
            case TypedArrayElementType.BigUint64:
                return JsValue.FromBigInt(isLittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes));
            case TypedArrayElementType.Float16:
                return (double)(isLittleEndian ? BinaryPrimitives.ReadHalfLittleEndian(bytes) : BinaryPrimitives.ReadHalfBigEndian(bytes));
            case TypedArrayElementType.Float32:
                return isLittleEndian ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : BinaryPrimitives.ReadSingleBigEndian(bytes);
            default:
                return isLittleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(bytes) : BinaryPrimitives.ReadDoubleBigEndian(bytes);
        }
    }

    /// <summary>
    /// NumericToRawBytes(type, value, isLittleEndian): writes the bytes at the start of the span. The value is a number
    /// for the Number element types and a BigInt for the BigInt element types.
    /// </summary>
    public static void NumericToRawBytes(TypedArrayElementType type, JsValue value, Span<byte> bytes, bool isLittleEndian)
    {
        switch (type)
        {
            case TypedArrayElementType.BigInt64:
            case TypedArrayElementType.BigUint64:
                var bits = BigIntToUInt64(value.AsBigInt());
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes, bits);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, bits);
                }

                break;
            default:
                NumberToRawBytes(type, value.NumberValue, bytes, isLittleEndian);
                break;
        }
    }

    /// <summary>NumericToRawBytes for the Number element types.</summary>
    public static void NumberToRawBytes(TypedArrayElementType type, double value, Span<byte> bytes, bool isLittleEndian)
    {
        switch (type)
        {
            case TypedArrayElementType.Int8:
            case TypedArrayElementType.Uint8:
                bytes[0] = unchecked((byte)JsOperations.DoubleToInt32(value));
                break;
            case TypedArrayElementType.Uint8Clamped:
                bytes[0] = ToUint8Clamp(value);
                break;
            case TypedArrayElementType.Int16:
            case TypedArrayElementType.Uint16:
                var int16 = unchecked((ushort)JsOperations.DoubleToInt32(value));
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes, int16);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, int16);
                }

                break;
            case TypedArrayElementType.Int32:
            case TypedArrayElementType.Uint32:
                var int32 = JsOperations.DoubleToInt32(value);
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(bytes, int32);
                }
                else
                {
                    BinaryPrimitives.WriteInt32BigEndian(bytes, int32);
                }

                break;
            case TypedArrayElementType.Float16:
                var half = ToHalf(value);
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteHalfLittleEndian(bytes, half);
                }
                else
                {
                    BinaryPrimitives.WriteHalfBigEndian(bytes, half);
                }

                break;
            case TypedArrayElementType.Float32:
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes, (float)value);
                }
                else
                {
                    BinaryPrimitives.WriteSingleBigEndian(bytes, (float)value);
                }

                break;
            case TypedArrayElementType.Float64:
                if (isLittleEndian)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
                }
                else
                {
                    BinaryPrimitives.WriteDoubleBigEndian(bytes, value);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Not a Number element type");
        }
    }

    /// <summary>GetValueFromBuffer(arrayBuffer, byteIndex, type, isTypedArray, unordered, isLittleEndian)</summary>
    public static JsValue GetValueFromBuffer(JsArrayBuffer buffer, int byteIndex, TypedArrayElementType type, bool isLittleEndian)
    {
        return RawBytesToNumeric(type, buffer.GetSpan()[byteIndex..], isLittleEndian);
    }

    /// <summary>SetValueInBuffer(arrayBuffer, byteIndex, type, value, isTypedArray, unordered, isLittleEndian): the value is already converted (a number or a BigInt).</summary>
    public static void SetValueInBuffer(JsArrayBuffer buffer, int byteIndex, TypedArrayElementType type, JsValue value, bool isLittleEndian)
    {
        NumericToRawBytes(type, value, buffer.GetSpan()[byteIndex..], isLittleEndian);
    }

    /// <summary>
    /// Gets the bytes of a BufferSource (an ArrayBuffer, a SharedArrayBuffer, a TypedArray or a DataView) without
    /// running JavaScript code: false when the value is not a BufferSource. The span is empty when the buffer is
    /// detached or the view is out of bounds.
    /// </summary>
    public static bool TryGetBufferSourceSpan(JsValue value, out Span<byte> bytes)
    {
        switch (value.Cell)
        {
            case JsArrayBuffer buffer:
                bytes = buffer.GetSpan();
                return true;
            case JsTypedArray typedArray:
                bytes = typedArray.GetSpan();
                return true;
            case JsDataView view:
                bytes = view.GetSpan();
                return true;
            default:
                bytes = default;
                return false;
        }
    }

    /// <summary>Clamps a relative index (the result of ToIntegerOrInfinity) to [0, length] (the start and end arguments of slice, fill, copyWithin...).</summary>
    public static long ClampRelativeIndex(double relative, long length)
    {
        if (double.IsNegativeInfinity(relative))
            return 0;

        if (relative < 0)
            return (long)Math.Max(length + relative, 0);

        return (long)Math.Min(relative, length);
    }

    /// <summary>Clamps a relative index argument (undefined is <paramref name="defaultValue"/>) to [0, length].</summary>
    public static long ToClampedIndex(JsValue value, long length, long defaultValue)
    {
        if (value.IsUndefined)
            return defaultValue;

        return ClampRelativeIndex(JsOperations.ToIntegerOrInfinity(value), length);
    }
}
