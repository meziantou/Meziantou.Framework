using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DnsServer.Protocol.Wire;

[StructLayout(LayoutKind.Auto)]
internal ref struct DnsWireWriter
{
    /// <summary>The maximum length of a single label (RFC 1035 2.3.4).</summary>
    private const int MaxLabelLength = 63;

    /// <summary>The highest offset a compression pointer can address, as it only carries 14 bits.</summary>
    private const int MaxPointerOffset = 0x3FFF;

    private byte[] _buffer;
    private int _position;
    private Dictionary<string, int>? _nameOffsets;

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

    /// <summary>Writes a domain name in full, without using compression pointers.</summary>
    public void WriteDomainName(string name) => WriteDomainNameCore(name, compress: false);

    /// <summary>
    /// Writes a domain name, reusing an earlier occurrence through a compression pointer when possible.
    /// Only valid for owner names and for the record types defined in RFC 1035; RFC 3597 3 forbids
    /// compression inside the data of any type defined later.
    /// </summary>
    public void WriteCompressibleDomainName(string name) => WriteDomainNameCore(name, compress: true);

    private void WriteDomainNameCore(string name, bool compress)
    {
        var span = name.AsSpan();
        if (span.Length > 0 && span[^1] == '.')
        {
            span = span[..^1];
        }

        if (span.IsEmpty)
        {
            WriteByte(0);
            return;
        }

        var encodedLength = 1; // the terminating root label
        var remaining = span;

        while (!remaining.IsEmpty)
        {
            if (compress)
            {
                _nameOffsets ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var lookup = _nameOffsets.GetAlternateLookup<ReadOnlySpan<char>>();

                if (lookup.TryGetValue(remaining, out var matchOffset))
                {
                    EnsureDomainNameLength(name, encodedLength + 2);
                    WriteUInt16((ushort)(0xC000 | matchOffset));
                    return;
                }

                if (_position <= MaxPointerOffset)
                {
                    lookup[remaining] = _position;
                }
            }

            var separatorIndex = remaining.IndexOf('.');
            ReadOnlySpan<char> label;
            if (separatorIndex < 0)
            {
                label = remaining;
                remaining = [];
            }
            else
            {
                label = remaining[..separatorIndex];
                remaining = remaining[(separatorIndex + 1)..];
            }

            if (label.IsEmpty || label.Length > MaxLabelLength)
                throw new DnsProtocolException($"Invalid domain name label length: {label.Length}. Labels must be between 1 and {MaxLabelLength} bytes.");

            if (!Ascii.IsValid(label))
                throw new DnsProtocolException($"Domain name '{name}' contains non-ASCII characters. Convert internationalized names to their punycode (IDNA) form before encoding them.");

            encodedLength += label.Length + 1;
            EnsureDomainNameLength(name, encodedLength);

            WriteByte((byte)label.Length);
            EnsureCapacity(label.Length);
            Ascii.FromUtf16(label, _buffer.AsSpan(_position), out var bytesWritten);
            _position += bytesWritten;
        }

        WriteByte(0); // Root label
    }

    private static void EnsureDomainNameLength(string name, int encodedLength)
    {
        if (encodedLength > DnsWireReader.MaxDomainNameLength)
            throw new DnsProtocolException($"Domain name '{name}' exceeds the maximum encoded length of {DnsWireReader.MaxDomainNameLength} bytes.");
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
        EnsureAscii(value);
        if (value.Length > 255)
        {
            throw new DnsProtocolException($"Character string too long: {value.Length} bytes. Maximum is 255.");
        }

        WriteByte((byte)value.Length);
        WriteAsciiCore(value);
    }

    /// <summary>Writes an ASCII string without a length prefix, for record data that runs to the end of the record.</summary>
    public void WriteAsciiString(string value)
    {
        EnsureAscii(value);
        WriteAsciiCore(value);
    }

    private void WriteAsciiCore(string value)
    {
        EnsureCapacity(value.Length);
        Ascii.FromUtf16(value, _buffer.AsSpan(_position), out var bytesWritten);
        _position += bytesWritten;
    }

    private static void EnsureAscii(string value)
    {
        if (!Ascii.IsValid(value))
            throw new DnsProtocolException($"The value '{value}' contains non-ASCII characters and cannot be encoded in this DNS record.");
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
