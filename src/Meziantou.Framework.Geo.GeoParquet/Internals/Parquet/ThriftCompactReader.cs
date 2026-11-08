using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.Geo.Internals.Parquet;

/// <summary>
/// Reads the Thrift compact protocol, the encoding of the metadata of Parquet files: varints, zigzag integers, and fields
/// whose header holds the delta of their identifier and their type.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal ref struct ThriftCompactReader
{
    public const byte TypeStop = 0;
    public const byte TypeBooleanTrue = 1;
    public const byte TypeBooleanFalse = 2;
    public const byte TypeByte = 3;
    public const byte TypeI16 = 4;
    public const byte TypeI32 = 5;
    public const byte TypeI64 = 6;
    public const byte TypeDouble = 7;
    public const byte TypeBinary = 8;
    public const byte TypeList = 9;
    public const byte TypeSet = 10;
    public const byte TypeMap = 11;
    public const byte TypeStruct = 12;

    // The metadata of a file nests a few structures: deeper data is not Parquet metadata
    private const int MaxDepth = 32;

    private readonly ReadOnlySpan<byte> _data;
    private int _depth;

    public ThriftCompactReader(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    /// <summary>Gets the number of bytes read.</summary>
    public int Position { get; private set; }

    /// <summary>Starts reading a structure: the identifiers of its fields are deltas from 0.</summary>
    public void BeginStruct()
    {
        if (++_depth > MaxDepth)
            throw new FormatException("The metadata of the Parquet file is nested too deeply.");
    }

    public void EndStruct() => _depth--;

    /// <summary>Reads the header of the next field of a structure.</summary>
    /// <param name="fieldId">The identifier of the previous field, then of this field.</param>
    /// <param name="type">The type of the field.</param>
    /// <returns><see langword="false"/> at the end of the structure.</returns>
    public bool ReadFieldHeader(ref short fieldId, out byte type)
    {
        var header = ReadByte();
        type = (byte)(header & 0x0F);
        if (type is TypeStop)
            return false;

        var delta = header >> 4;
        fieldId = delta is 0 ? checked((short)ReadZigZag32()) : checked((short)(fieldId + delta));
        return true;
    }

    /// <summary>Reads a boolean field, whose value is its type.</summary>
    public static bool ReadBoolean(byte type) => type switch
    {
        TypeBooleanTrue => true,
        TypeBooleanFalse => false,
        _ => throw new FormatException("A boolean field of the metadata of the Parquet file has another type."),
    };

    public int ReadI32(byte type)
    {
        if (type is TypeByte)
            return (sbyte)ReadByte();

        if (type is not (TypeI16 or TypeI32))
            throw new FormatException("An integer field of the metadata of the Parquet file has another type.");

        return ReadZigZag32();
    }

    public long ReadI64(byte type)
    {
        if (type is TypeByte or TypeI16 or TypeI32)
            return ReadI32(type);

        if (type is not TypeI64)
            throw new FormatException("An integer field of the metadata of the Parquet file has another type.");

        var value = ReadVarint();
        return (long)(value >> 1) ^ -(long)(value & 1);
    }

    public ReadOnlySpan<byte> ReadBinary(byte type)
    {
        if (type is not TypeBinary)
            throw new FormatException("A binary field of the metadata of the Parquet file has another type.");

        return ReadBinary();
    }

    public string ReadString(byte type) => Encoding.UTF8.GetString(ReadBinary(type));

    /// <summary>Reads the header of a list or a set, and returns the number of elements.</summary>
    public int ReadListHeader(byte type, out byte elementType)
    {
        if (type is not (TypeList or TypeSet))
            throw new FormatException("A list field of the metadata of the Parquet file has another type.");

        var header = ReadByte();
        elementType = (byte)(header & 0x0F);
        var count = header >> 4;
        if (count is 15)
        {
            var size = ReadVarint();
            if (size > int.MaxValue)
                throw new FormatException("A list of the metadata of the Parquet file is too long.");

            count = (int)size;
        }

        // Each element takes at least a byte, except the booleans and the empty structures, which still take one
        if (count > _data.Length - Position)
            throw new FormatException("A list of the metadata of the Parquet file is truncated.");

        return count;
    }

    /// <summary>Reads an element of a list of booleans, a byte per element.</summary>
    public bool ReadBooleanElement() => ReadByte() is TypeBooleanTrue;

    /// <summary>Skips a value of a type.</summary>
    public void Skip(byte type)
    {
        switch (type)
        {
            case TypeBooleanTrue or TypeBooleanFalse:
                break;

            case TypeByte:
                ReadByte();
                break;

            case TypeI16 or TypeI32 or TypeI64:
                ReadVarint();
                break;

            case TypeDouble:
                Take(8);
                break;

            case TypeBinary:
                ReadBinary();
                break;

            case TypeList or TypeSet:
                var count = ReadListHeader(type, out var elementType);
                for (var i = 0; i < count; i++)
                {
                    SkipElement(elementType);
                }

                break;

            case TypeMap:
                var size = ReadVarint();
                if (size > (ulong)(_data.Length - Position))
                    throw new FormatException("A map of the metadata of the Parquet file is truncated.");

                if (size > 0)
                {
                    var types = ReadByte();
                    for (var i = 0UL; i < size; i++)
                    {
                        SkipElement((byte)(types >> 4));
                        SkipElement((byte)(types & 0x0F));
                    }
                }

                break;

            case TypeStruct:
                BeginStruct();
                short fieldId = 0;
                while (ReadFieldHeader(ref fieldId, out var fieldType))
                {
                    Skip(fieldType);
                }

                EndStruct();
                break;

            default:
                throw new FormatException($"The type {type} of a field of the metadata of the Parquet file is not valid.");
        }
    }

    // In a list, a boolean takes a byte
    private void SkipElement(byte type)
    {
        if (type is TypeBooleanTrue or TypeBooleanFalse)
        {
            ReadByte();
        }
        else
        {
            Skip(type);
        }
    }

    private ReadOnlySpan<byte> ReadBinary()
    {
        var length = ReadVarint();
        if (length > (ulong)(_data.Length - Position))
            throw new FormatException("A value of the metadata of the Parquet file is truncated.");

        return Take((int)length);
    }

    private int ReadZigZag32()
    {
        var value = ReadVarint();
        if (value > uint.MaxValue)
            throw new FormatException("An integer of the metadata of the Parquet file is too large.");

        return (int)((uint)value >> 1) ^ -(int)((uint)value & 1);
    }

    private ulong ReadVarint()
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var b = ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) is 0)
                return result;
        }

        throw new FormatException("An integer of the metadata of the Parquet file is too long.");
    }

    private byte ReadByte()
    {
        if (Position >= _data.Length)
            throw new FormatException("The metadata of the Parquet file is truncated.");

        return _data[Position++];
    }

    private ReadOnlySpan<byte> Take(int length)
    {
        if (length > _data.Length - Position)
            throw new FormatException("The metadata of the Parquet file is truncated.");

        var result = _data.Slice(Position, length);
        Position += length;
        return result;
    }
}
