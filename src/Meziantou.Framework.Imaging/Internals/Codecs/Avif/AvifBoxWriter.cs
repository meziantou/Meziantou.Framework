using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the boxes of an ISO base media file (ISO/IEC 14496-12, section 4.2) to a growable buffer: big-endian fields, and
/// boxes whose 32-bit size is filled in when they end. The counterpart of <see cref="AvifBoxReader"/>.
/// </summary>
internal readonly struct AvifBoxWriter
{
    private readonly PooledPayloadWriter _output;

    public AvifBoxWriter(PooledPayloadWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Gets the number of bytes written.</summary>
    public int Length => _output.Length;

    /// <summary>Starts a box: its size is written by <see cref="EndBox"/>.</summary>
    /// <returns>The position of the box, to pass to <see cref="EndBox"/>.</returns>
    public int BeginBox(uint type)
    {
        var start = _output.Length;
        WriteUInt32(0);
        WriteUInt32(type);
        return start;
    }

    /// <summary>Starts a box that has a version and flags (a <c>FullBox</c>).</summary>
    public int BeginFullBox(uint type, int version, int flags)
    {
        var start = BeginBox(type);
        WriteUInt32(((uint)version << 24) | (uint)flags);
        return start;
    }

    /// <summary>Ends the box that <see cref="BeginBox"/> started at <paramref name="start"/>.</summary>
    public void EndBox(int start) => PatchUInt32(start, (uint)(_output.Length - start));

    public void WriteByte(byte value) => _output.Write(value);

    public void WriteUInt16(int value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, checked((ushort)value));
        _output.Write(bytes);
    }

    public void WriteUInt32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        _output.Write(bytes);
    }

    public void WriteBytes(ReadOnlySpan<byte> value) => _output.Write(value);

    /// <summary>Writes a string and the zero byte that ends it.</summary>
    public void WriteCString(ReadOnlySpan<byte> value)
    {
        _output.Write(value);
        _output.Write(0);
    }

    /// <summary>Replaces the 32-bit field at <paramref name="offset"/>.</summary>
    public void PatchUInt32(int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(_output.WrittenSpan[offset..], value);
}
