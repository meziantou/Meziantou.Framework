using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DnsServer.Protocol.Wire;

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

    public void WriteInt32(int value)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteInt32BigEndian(_buffer.AsSpan(_position), value);
        _position += 4;
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

    public void WriteBytes(ReadOnlySpan<byte> data)
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
        if (span[^1] == '.')
        {
            span = span[..^1];
        }

        foreach (var label in new LabelEnumerator(span))
        {
            var byteCount = Encoding.ASCII.GetByteCount(label);
            if (byteCount is 0 or > 63)
            {
                throw new DnsProtocolException($"Invalid domain name label length: {byteCount}. Labels must be between 1 and 63 bytes.");
            }

            WriteByte((byte)byteCount);
            EnsureCapacity(byteCount);
            Encoding.ASCII.GetBytes(label, _buffer.AsSpan(_position));
            _position += byteCount;
        }

        WriteByte(0); // Root label
    }

    public void WriteCharacterString(string value)
    {
        var byteCount = Encoding.UTF8.GetByteCount(value);
        if (byteCount > 255)
        {
            throw new DnsProtocolException($"Character string too long: {byteCount} bytes. Maximum is 255.");
        }

        WriteByte((byte)byteCount);
        EnsureCapacity(byteCount);
        Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_position));
        _position += byteCount;
    }

    public void WriteAsciiCharacterString(string value)
    {
        var byteCount = Encoding.ASCII.GetByteCount(value);
        if (byteCount > 255)
        {
            throw new DnsProtocolException($"Character string too long: {byteCount} bytes. Maximum is 255.");
        }

        WriteByte((byte)byteCount);
        EnsureCapacity(byteCount);
        Encoding.ASCII.GetBytes(value, _buffer.AsSpan(_position));
        _position += byteCount;
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

    [StructLayout(LayoutKind.Auto)]
    private ref struct LabelEnumerator
    {
        private ReadOnlySpan<char> _remaining;

        public LabelEnumerator(ReadOnlySpan<char> name)
        {
            _remaining = name;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public readonly LabelEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
                return false;

            var index = _remaining.IndexOf('.');
            if (index == -1)
            {
                Current = _remaining;
                _remaining = [];
            }
            else
            {
                Current = _remaining[..index];
                _remaining = _remaining[(index + 1)..];
            }

            return true;
        }
    }
}
