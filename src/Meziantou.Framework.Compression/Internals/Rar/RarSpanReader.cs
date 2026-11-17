using System.Buffers.Binary;

namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>Reads the fields of a RAR header. Reading past the end throws an <see cref="InvalidDataException"/>.</summary>
internal ref struct RarSpanReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public RarSpanReader(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    public readonly int Position => _position;

    public readonly int Remaining => _data.Length - _position;

    public byte ReadByte() => ReadBytes(1)[0];

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(4));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64LittleEndian(ReadBytes(8));

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
            throw new InvalidDataException("A header of the RAR archive is truncated.");

        var result = _data.Slice(_position, count);
        _position += count;
        return result;
    }

    /// <summary>Reads a variable-length integer of RAR5: 7 bits per byte, the high bit set when another byte follows.</summary>
    public ulong ReadVInt()
    {
        ulong value = 0;
        for (var shift = 0; shift < 70; shift += 7)
        {
            var b = ReadByte();
            if (shift < 64)
            {
                value |= (ulong)(b & 0x7F) << shift;
            }

            if ((b & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("A header of the RAR archive has an invalid number.");
    }
}
