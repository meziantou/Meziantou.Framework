using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DnsClient.Protocol;

[StructLayout(LayoutKind.Auto)]
internal ref struct DnsWireReader
{
    private readonly ReadOnlySpan<byte> _message;
    private int _position;

    public DnsWireReader(ReadOnlySpan<byte> message)
    {
        _message = message;
        _position = 0;
    }

    public readonly int Position => _position;

    public ushort ReadUInt16()
    {
        EnsureAvailable(2, "UInt16");
        var value = BinaryPrimitives.ReadUInt16BigEndian(_message[_position..]);
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        EnsureAvailable(4, "UInt32");
        var value = BinaryPrimitives.ReadUInt32BigEndian(_message[_position..]);
        _position += 4;
        return value;
    }

    public int ReadInt32()
    {
        EnsureAvailable(4, "Int32");
        var value = BinaryPrimitives.ReadInt32BigEndian(_message[_position..]);
        _position += 4;
        return value;
    }

    public byte ReadByte()
    {
        if (_position >= _message.Length)
            throw new DnsProtocolException("Unexpected end of DNS message while reading byte.");

        return _message[_position++];
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0)
            throw new DnsProtocolException($"Invalid DNS record data length: {count}. The record declares fewer bytes than its fixed fields require.");

        if (count > _message.Length - _position)
            throw new DnsProtocolException($"Unexpected end of DNS message while reading {count} bytes.");

        var span = _message.Slice(_position, count);
        _position += count;
        return span;
    }

    public void Skip(int count)
    {
        if (count < 0)
            throw new DnsProtocolException($"Cannot skip {count} bytes: the count is negative.");

        if (count > _message.Length - _position)
            throw new DnsProtocolException($"Cannot skip {count} bytes: exceeds message boundary.");

        _position += count;
    }

    public readonly ReadOnlySpan<byte> GetBytes(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > _message.Length || count > _message.Length - offset)
            throw new DnsProtocolException($"Cannot read {count} bytes at offset {offset}: exceeds message boundary.");

        return _message.Slice(offset, count);
    }

    /// <summary>
    /// Creates a reader restricted to <paramref name="length"/> bytes starting at the current position, and advances
    /// this reader past them. Domain names inside the window are still resolved against the whole message, because
    /// compression pointers may target any earlier offset.
    /// </summary>
    public DnsWireReader ReadWindow(int length)
    {
        if (length < 0)
            throw new DnsProtocolException($"Invalid DNS record data length: {length}.");

        if (length > _message.Length - _position)
            throw new DnsProtocolException($"DNS record data length {length} exceeds the message boundary.");

        var window = new DnsWireReader(_message[..(_position + length)]) { _position = _position };
        _position += length;
        return window;
    }

    public string ReadDomainName()
    {
        var sb = new StringBuilder(64);
        ReadDomainNameCore(sb, _message, ref _position, maxPointers: DnsName.MaxLabels);
        return sb.ToString();
    }

    private void EnsureAvailable(int count, string what)
    {
        if (count > _message.Length - _position)
            throw new DnsProtocolException($"Unexpected end of DNS message while reading {what}.");
    }

    private static void ReadDomainNameCore(StringBuilder sb, ReadOnlySpan<byte> message, ref int position, int maxPointers)
    {
        var jumped = false;
        var originalPosition = -1;
        var pointerCount = 0;
        var nameLength = 1; // the terminating root label

        while (position < message.Length)
        {
            var length = message[position];
            var labelType = length & 0xC0;

            if (length is 0)
            {
                position++;
                break;
            }

            // Compression pointer (top 2 bits set)
            if (labelType is 0xC0)
            {
                if (++pointerCount > maxPointers)
                    throw new DnsProtocolException("Too many compression pointers in domain name (possible loop).");

                if (position + 1 >= message.Length)
                    throw new DnsProtocolException("Unexpected end of DNS message while reading compression pointer.");

                var pointer = ((length & 0x3F) << 8) | message[position + 1];

                // RFC 1035 4.1.4: a pointer refers to a *prior* occurrence of the name. Requiring a strictly
                // backwards jump keeps the chain finite and makes compression loops impossible to express.
                if (pointer >= position)
                    throw new DnsProtocolException($"Invalid compression pointer to offset {pointer}: pointers must refer to an earlier position in the message.");

                if (!jumped)
                {
                    originalPosition = position + 2;
                }

                position = pointer;
                jumped = true;
                continue;
            }

            if (labelType != 0)
                throw new DnsProtocolException($"Invalid label type: 0x{length:X2}.");

            position++;

            if (length > message.Length - position)
                throw new DnsProtocolException("Domain name label extends beyond message boundary.");

            nameLength += length + 1;
            if (nameLength > DnsName.MaxLength)
                throw new DnsProtocolException($"Domain name exceeds the maximum length of {DnsName.MaxLength} bytes.");

            if (sb.Length > 0)
            {
                sb.Append('.');
            }

            DnsName.AppendLabel(sb, message.Slice(position, length));
            position += length;
        }

        if (jumped)
        {
            position = originalPosition;
        }
    }
}
