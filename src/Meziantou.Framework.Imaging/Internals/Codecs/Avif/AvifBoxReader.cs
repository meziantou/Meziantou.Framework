using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the big-endian fields and the child boxes of a box of the ISO base media file format (ISO/IEC 14496-12, section 4.2)
/// that is entirely in memory. Reading past the end of the box is <see cref="InvalidImageContentException"/>.
/// </summary>
internal ref struct AvifBoxReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public AvifBoxReader(ReadOnlySpan<byte> data)
    {
        _data = data;
    }

    /// <summary>Gets the position of the next byte in the box.</summary>
    public readonly int Position => _position;

    /// <summary>Gets the number of bytes that are not read yet.</summary>
    public readonly int Remaining => _data.Length - _position;

    public readonly bool IsEnd => _position >= _data.Length;

    public byte ReadByte() => ReadBytes(1)[0];

    public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));

    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));

    public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));

    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(ReadBytes(8));

    /// <summary>Reads an unsigned field of <paramref name="size"/> bytes: 0 (no field, the value is 0), 2, 4 or 8.</summary>
    public ulong ReadUInt(int size) => size switch
    {
        0 => 0,
        2 => ReadUInt16(),
        4 => ReadUInt32(),
        8 => ReadUInt64(),
        _ => throw AvifFormat.Invalid("a field has a size that is not allowed."),
    };

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
            throw AvifFormat.Invalid("a box is too short for its content.");

        var result = _data.Slice(_position, count);
        _position += count;
        return result;
    }

    /// <summary>Reads the bytes of a null-terminated string, without its terminator.</summary>
    public ReadOnlySpan<byte> ReadCString()
    {
        var remaining = _data[_position..];
        var length = remaining.IndexOf((byte)0);
        if (length < 0)
            throw AvifFormat.Invalid("a string is not terminated.");

        _position += length + 1;
        return remaining[..length];
    }

    /// <summary>Reads the version and the flags of a full box.</summary>
    public (int Version, uint Flags) ReadFullBoxHeader()
    {
        var value = ReadUInt32();
        return ((int)(value >> 24), value & 0xFFFFFF);
    }

    /// <summary>
    /// Reads the next child box: its type, the offset of its payload in this box and a reader of its payload. The reader is
    /// then positioned after the child.
    /// </summary>
    /// <returns><see langword="false"/> at the end of the box.</returns>
    public bool TryReadBox(out uint type, out int payloadOffset, out AvifBoxReader payload)
    {
        if (IsEnd)
        {
            type = 0;
            payloadOffset = 0;
            payload = default;
            return false;
        }

        var start = _position;
        ulong size = ReadUInt32();
        type = ReadUInt32();
        if (size is 1)
        {
            size = ReadUInt64();
        }
        else if (size is 0)
        {
            // The box extends to the end of its container
            size = (ulong)(_data.Length - start);
        }

        if (type is AvifFormat.Uuid)
            ReadBytes(16);

        var headerLength = _position - start;
        if (size < (ulong)headerLength || size > (ulong)(_data.Length - start))
            throw AvifFormat.Invalid($"the box '{AvifFormat.ToText(type)}' has a size that is not valid.");

        payloadOffset = _position;
        var payloadLength = (int)size - headerLength;
        payload = new AvifBoxReader(_data.Slice(_position, payloadLength));
        _position += payloadLength;
        return true;
    }
}
