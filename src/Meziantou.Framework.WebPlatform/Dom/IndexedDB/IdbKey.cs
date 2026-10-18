using System.Buffers.Binary;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The type of a key (https://w3c.github.io/IndexedDB/#key-type), in the order of the comparison of the keys.</summary>
internal enum IdbKeyType
{
    Number,
    Date,
    String,
    Binary,
    Array,
}

/// <summary>
/// A key of IndexedDB (https://w3c.github.io/IndexedDB/#key-construct): a number, a date, a string, a binary or an array of
/// keys. The keys are stored in their encoded form (<see cref="Encode"/>), whose bytes compare as the keys
/// (https://w3c.github.io/IndexedDB/#compare-two-keys): the backends order the keys with a byte comparison.
/// </summary>
/// <remarks>
/// <para>The encoding:</para>
/// <list type="bullet">
/// <item>a number: <c>0x10</c>, then the 8 bytes of the double, big-endian, with the sign bit flipped (all the bits for a negative number);</item>
/// <item>a date: <c>0x20</c>, then its time value encoded as a number;</item>
/// <item>a string: <c>0x30</c>, then its UTF-16 code units, big-endian, escaped, then <c>00 00</c>;</item>
/// <item>a binary: <c>0x40</c>, then its bytes, escaped, then <c>00 00</c>;</item>
/// <item>an array: <c>0x50</c>, then its items, then <c>0x01</c> (lower than the type bytes, so that a prefix sorts first).</item>
/// </list>
/// <para>The escaped bytes replace a <c>00</c> byte by <c>00 FF</c>, so that the terminator <c>00 00</c> sorts first.</para>
/// </remarks>
internal sealed class IdbKey : IComparable<IdbKey>, IEquatable<IdbKey>
{
    private const byte NumberTag = 0x10;
    private const byte DateTag = 0x20;
    private const byte StringTag = 0x30;
    private const byte BinaryTag = 0x40;
    private const byte ArrayTag = 0x50;
    private const byte ArrayEnd = 0x01;

    private byte[]? _encoded;

    private IdbKey(IdbKeyType type, double number = 0, string? text = null, byte[]? binary = null, IdbKey[]? items = null)
    {
        Type = type;
        Number = number;
        String = text;
        Binary = binary;
        Items = items;
    }

    public IdbKeyType Type { get; }

    /// <summary>The value of a number key, or the time value of a date key.</summary>
    public double Number { get; }

    public string? String { get; }

    public byte[]? Binary { get; }

    public IdbKey[]? Items { get; }

    // -0 is the same key as +0 (https://w3c.github.io/IndexedDB/#compare-two-keys compares the values)
    public static IdbKey FromNumber(double value) => double.IsNaN(value) ? throw new ArgumentOutOfRangeException(nameof(value)) : new(IdbKeyType.Number, number: value == 0 ? 0 : value);

    public static IdbKey FromDate(double timeValue) => double.IsNaN(timeValue) ? throw new ArgumentOutOfRangeException(nameof(timeValue)) : new(IdbKeyType.Date, number: timeValue == 0 ? 0 : timeValue);

    public static IdbKey FromString(string value) => new(IdbKeyType.String, text: value);

    public static IdbKey FromBinary(byte[] value) => new(IdbKeyType.Binary, binary: value);

    public static IdbKey FromArray(IdbKey[] items) => new(IdbKeyType.Array, items: items);

    /// <summary>Returns the encoded key (do not modify it).</summary>
    public byte[] Encode()
    {
        if (_encoded is null)
        {
            var buffer = new List<byte>();
            Write(buffer);
            _encoded = [.. buffer];
        }

        return _encoded;
    }

    /// <summary>Decodes an encoded key.</summary>
    /// <exception cref="FormatException">The bytes are not an encoded key.</exception>
    public static IdbKey Decode(ReadOnlySpan<byte> encoded)
    {
        var position = 0;
        var key = Read(encoded, ref position);
        if (position != encoded.Length)
            throw new FormatException("The key has trailing bytes");

        return key;
    }

    /// <summary>Compares two encoded keys (https://w3c.github.io/IndexedDB/#compare-two-keys).</summary>
    public static int Compare(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b) => Math.Sign(a.SequenceCompareTo(b));

    public int CompareTo(IdbKey? other) => other is null ? 1 : Compare(Encode(), other.Encode());

    public bool Equals([NotNullWhen(true)] IdbKey? other) => other is not null && Encode().AsSpan().SequenceEqual(other.Encode());

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as IdbKey);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(Encode());
        return hash.ToHashCode();
    }

    public static bool operator ==(IdbKey? left, IdbKey? right) => left is null ? right is null : left.Equals(right);

    public static bool operator !=(IdbKey? left, IdbKey? right) => !(left == right);

    public static bool operator <(IdbKey? left, IdbKey? right) => left is null ? right is not null : left.CompareTo(right) < 0;

    public static bool operator <=(IdbKey? left, IdbKey? right) => left is null || left.CompareTo(right) <= 0;

    public static bool operator >(IdbKey? left, IdbKey? right) => left is not null && left.CompareTo(right) > 0;

    public static bool operator >=(IdbKey? left, IdbKey? right) => left is null ? right is null : left.CompareTo(right) >= 0;

    public override string ToString() => Type switch
    {
        IdbKeyType.Number => Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IdbKeyType.Date => "Date(" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")",
        IdbKeyType.String => "\"" + String + "\"",
        IdbKeyType.Binary => "Binary(" + Convert.ToHexString(Binary!) + ")",
        _ => "[" + string.Join(", ", Items!.Select(item => item.ToString())) + "]",
    };

    private void Write(List<byte> buffer)
    {
        switch (Type)
        {
            case IdbKeyType.Number:
                buffer.Add(NumberTag);
                WriteDouble(buffer, Number);
                break;
            case IdbKeyType.Date:
                buffer.Add(DateTag);
                WriteDouble(buffer, Number);
                break;
            case IdbKeyType.String:
                buffer.Add(StringTag);
                foreach (var c in String!)
                {
                    WriteEscaped(buffer, (byte)(c >> 8));
                    WriteEscaped(buffer, (byte)c);
                }

                buffer.Add(0);
                buffer.Add(0);
                break;
            case IdbKeyType.Binary:
                buffer.Add(BinaryTag);
                foreach (var b in Binary!)
                {
                    WriteEscaped(buffer, b);
                }

                buffer.Add(0);
                buffer.Add(0);
                break;
            default:
                buffer.Add(ArrayTag);
                foreach (var item in Items!)
                {
                    item.Write(buffer);
                }

                buffer.Add(ArrayEnd);
                break;
        }

        static void WriteEscaped(List<byte> buffer, byte value)
        {
            buffer.Add(value);
            if (value == 0)
            {
                buffer.Add(0xFF);
            }
        }
    }

    private static void WriteDouble(List<byte> buffer, double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        var sortable = bits < 0 ? (ulong)~bits : (ulong)bits ^ 0x8000_0000_0000_0000;
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, sortable);
        foreach (var b in bytes)
        {
            buffer.Add(b);
        }
    }

    private static double ReadDouble(ReadOnlySpan<byte> encoded, ref int position)
    {
        if (position + 8 > encoded.Length)
            throw new FormatException("The key is truncated");

        var sortable = BinaryPrimitives.ReadUInt64BigEndian(encoded[position..]);
        position += 8;
        var bits = (sortable & 0x8000_0000_0000_0000) != 0 ? (long)(sortable ^ 0x8000_0000_0000_0000) : ~(long)sortable;
        return BitConverter.Int64BitsToDouble(bits);
    }

    private static IdbKey Read(ReadOnlySpan<byte> encoded, ref int position)
    {
        if (position >= encoded.Length)
            throw new FormatException("The key is truncated");

        switch (encoded[position++])
        {
            case NumberTag:
                return FromNumber(ReadDouble(encoded, ref position));
            case DateTag:
                return FromDate(ReadDouble(encoded, ref position));
            case StringTag:
            {
                var bytes = ReadEscaped(encoded, ref position);
                if (bytes.Count % 2 != 0)
                    throw new FormatException("The string key has an odd number of bytes");

                var chars = new char[bytes.Count / 2];
                for (var i = 0; i < chars.Length; i++)
                {
                    chars[i] = (char)((bytes[2 * i] << 8) | bytes[(2 * i) + 1]);
                }

                return FromString(new string(chars));
            }

            case BinaryTag:
                return FromBinary([.. ReadEscaped(encoded, ref position)]);
            case ArrayTag:
            {
                var items = new List<IdbKey>();
                while (true)
                {
                    if (position >= encoded.Length)
                        throw new FormatException("The array key is truncated");

                    if (encoded[position] == ArrayEnd)
                    {
                        position++;
                        return FromArray([.. items]);
                    }

                    items.Add(Read(encoded, ref position));
                }
            }

            default:
                throw new FormatException("The key has an invalid type");
        }
    }

    private static List<byte> ReadEscaped(ReadOnlySpan<byte> encoded, ref int position)
    {
        var bytes = new List<byte>();
        while (true)
        {
            if (position >= encoded.Length)
                throw new FormatException("The key is truncated");

            var b = encoded[position++];
            if (b != 0)
            {
                bytes.Add(b);
                continue;
            }

            if (position >= encoded.Length)
                throw new FormatException("The key is truncated");

            var next = encoded[position++];
            if (next == 0)
                return bytes;

            if (next != 0xFF)
                throw new FormatException("The key has an invalid escape sequence");

            bytes.Add(0);
        }
    }
}
