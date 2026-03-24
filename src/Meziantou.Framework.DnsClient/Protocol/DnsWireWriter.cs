using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

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

    public void WriteBytes(ReadOnlySpan<byte> data)
    {
        EnsureCapacity(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    public void WriteDomainName(string name)
    {
        if (string.IsNullOrEmpty(name) || name == ".")
        {
            WriteByte(0);
            return;
        }

        // Remove trailing dot if present
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
