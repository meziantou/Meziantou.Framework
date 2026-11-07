using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// Writes a size-prefixed FlatBuffers buffer from the front: its size, the offset of the root table, then each table
/// after its vtable, then the strings, vectors and tables it refers to, so that the unsigned offsets point forward.
/// Every value is aligned on its size from the start of the size, and the buffer is padded to a multiple of 8 bytes, as
/// FlatGeobuf 3.0.1 writes them: the values of a file that starts with the 8 bytes of the signature are aligned.
/// </summary>
internal sealed class FlatBufferBuilder
{
    private byte[] _buffer = new byte[256];

    public int Length { get; private set; }

    /// <summary>Writes a size-prefixed buffer whose root is a table.</summary>
    public static byte[] BuildSizePrefixed(FlatBufferTableBuilder root)
    {
        var builder = new FlatBufferBuilder();
        builder.Allocate(8);
        var table = root.Write(builder);
        builder.PatchUInt32(4, (uint)(table - 4));
        builder.Align(8);
        builder.PatchUInt32(0, (uint)(builder.Length - 4));
        return builder._buffer.AsSpan(0, builder.Length).ToArray();
    }

    /// <summary>Adds zeros until the length is a multiple of an alignment.</summary>
    public void Align(int alignment)
    {
        var padding = (alignment - (Length % alignment)) % alignment;
        Allocate(padding);
    }

    /// <summary>Adds bytes set to zero, and returns them.</summary>
    public Span<byte> Allocate(int size)
    {
        if (Length + size > _buffer.Length)
        {
            Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, Length + size));
        }

        var span = _buffer.AsSpan(Length, size);
        Length += size;
        return span;
    }

    public void PatchUInt32(int position, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(position), value);

    public void PatchInt32(int position, int value) => BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(position), value);

    public Span<byte> GetSpan(int position, int length) => _buffer.AsSpan(position, length);

    /// <summary>Writes a string: its length, its UTF-8 bytes and a zero.</summary>
    public int WriteString(string value)
    {
        Align(4);
        var position = Length;
        var count = Encoding.UTF8.GetByteCount(value);
        var span = Allocate(4 + count + 1);
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)count);
        Encoding.UTF8.GetBytes(value, span[4..]);
        return position;
    }

    /// <summary>Writes a vector of scalars: its length, then its elements, aligned on their size.</summary>
    public int WriteVector(ReadOnlySpan<byte> elements, int elementSize, int count)
    {
        Align(4);
        while ((Length + 4) % elementSize != 0)
        {
            Allocate(4);
        }

        var position = Length;
        var span = Allocate(4 + elements.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)count);
        elements.CopyTo(span[4..]);
        return position;
    }
}
