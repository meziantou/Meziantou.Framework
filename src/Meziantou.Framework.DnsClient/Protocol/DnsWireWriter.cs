using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DnsClient.Protocol;

[StructLayout(LayoutKind.Auto)]
internal ref struct DnsWireWriter
{
    private byte[] _buffer;
    private int _position;

    public DnsWireWriter()
        : this(512)
    {
    }

    public DnsWireWriter(int initialCapacity)
    {
        _buffer = new byte[initialCapacity];
        _position = 0;
    }

    public readonly int Position => _position;

    public readonly ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    public byte[] ToArray()
    {
        var result = new byte[_position];
        _buffer.AsSpan(0, _position).CopyTo(result);
        return result;
    }

    public void WriteUInt16(ushort value)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(_position), value);
        _position += 2;
    }

    public void WriteUInt32(uint value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32BigEndian(_buffer.AsSpan(_position), value);
        _position += 4;
    }

    public void WriteByte(byte value)
    {
        EnsureCapacity(1);
        _buffer[_position++] = value;
    }

    public void WriteBytes(scoped ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    public void WriteDomainName(string name)
    {
        if (string.IsNullOrEmpty(name) || name is ".")
        {
            WriteByte(0);
            return;
        }

        var span = name.AsSpan();
        if (DnsName.EndsWithUnescapedDot(span))
        {
            span = span[..^1];
        }

        var totalLength = 1; // the terminating root label
        Span<byte> labelBuffer = stackalloc byte[DnsName.MaxLabelLength];

        foreach (var label in DnsName.EnumerateLabels(span))
        {
            var byteCount = DnsName.DecodeLabel(label, labelBuffer);
            if (byteCount is 0)
                throw new DnsProtocolException("Invalid domain name: labels must be between 1 and 63 bytes.");

            totalLength += byteCount + 1;
            if (totalLength > DnsName.MaxLength)
                throw new DnsProtocolException($"Domain name exceeds the maximum length of {DnsName.MaxLength} bytes.");

            WriteByte((byte)byteCount);
            WriteBytes(labelBuffer[..byteCount]);
        }

        WriteByte(0); // Root label
    }

    public void WriteUInt16At(ushort value, int position)
    {
        BinaryPrimitives.WriteUInt16BigEndian(_buffer.AsSpan(position), value);
    }

    private void EnsureCapacity(int additionalBytes)
    {
        var required = _position + additionalBytes;
        if (required <= _buffer.Length)
            return;

        var newSize = Math.Max(_buffer.Length * 2, required);
        var newBuffer = new byte[newSize];
        _buffer.AsSpan(0, _position).CopyTo(newBuffer);
        _buffer = newBuffer;
    }
}
