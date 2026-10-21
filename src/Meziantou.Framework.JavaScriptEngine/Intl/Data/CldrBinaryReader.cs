using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>Reads the primitives of the CLDR data files: LEB128 varints and UTF-8 strings prefixed by their byte length.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct CldrBinaryReader(byte[] data, int position)
{
    public int Position { readonly get; set; } = position;

    public readonly bool IsAtEnd => Position >= data.Length;

    public byte ReadByte() => data[Position++];

    public ReadOnlySpan<byte> ReadBytes(int length)
    {
        var result = data.AsSpan(Position, length);
        Position += length;
        return result;
    }

    public void Skip(int length) => Position += length;

    public int ReadVarInt32() => checked((int)ReadVarUInt64());

    public ulong ReadVarUInt64()
    {
        ulong result = 0;
        var shift = 0;
        while (true)
        {
            var b = data[Position++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;

            shift += 7;
        }
    }

    /// <summary>Reads a zigzag-encoded signed integer.</summary>
    public long ReadVarInt64()
    {
        var value = ReadVarUInt64();
        return (long)(value >> 1) ^ -(long)(value & 1);
    }

    public string ReadString()
    {
        var length = ReadVarInt32();
        return Encoding.UTF8.GetString(ReadBytes(length));
    }

    public void SkipString() => Skip(ReadVarInt32());
}
