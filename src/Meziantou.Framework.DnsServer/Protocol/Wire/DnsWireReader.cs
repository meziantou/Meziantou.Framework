using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DnsServer.Protocol.Wire;

[StructLayout(LayoutKind.Auto)]
internal ref struct DnsWireReader
{
    /// <summary>The maximum encoded length of a domain name, including the root label (RFC 1035 2.3.4).</summary>
    public const int MaxDomainNameLength = 255;

    private readonly ReadOnlySpan<byte> _message;
    private int _position;
    private int _limit;

    public DnsWireReader(ReadOnlySpan<byte> message)
    {
        _message = message;
        _position = 0;
        _limit = message.Length;
    }

    public readonly int Position => _position;

    /// <summary>Gets the number of bytes left before the current limit.</summary>
    public readonly int Remaining => _limit - _position;

    /// <summary>
    /// Restricts subsequent reads to <paramref name="length"/> bytes so that a record cannot read into
    /// its neighbours, and returns the previous limit to pass back to <see cref="PopLimit"/>.
    /// </summary>
    public int PushLimit(int length)
    {
        if (length < 0 || _position + length > _limit)
            throw new DnsProtocolException($"Record data length of {length} bytes extends beyond the DNS message boundary.");

        var previousLimit = _limit;
        _limit = _position + length;
        return previousLimit;
    }

    public void PopLimit(int previousLimit) => _limit = previousLimit;

    public ushort ReadUInt16()
    {
        if (_position + 2 > _limit)
            throw new DnsProtocolException("Unexpected end of DNS message while reading UInt16.");

        var value = BinaryPrimitives.ReadUInt16BigEndian(_message[_position..]);
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        if (_position + 4 > _limit)
            throw new DnsProtocolException("Unexpected end of DNS message while reading UInt32.");

        var value = BinaryPrimitives.ReadUInt32BigEndian(_message[_position..]);
        _position += 4;
        return value;
    }

    public int ReadInt32()
    {
        if (_position + 4 > _limit)
            throw new DnsProtocolException("Unexpected end of DNS message while reading Int32.");

        var value = BinaryPrimitives.ReadInt32BigEndian(_message[_position..]);
        _position += 4;
        return value;
    }

    public byte ReadByte()
    {
        if (_position >= _limit)
            throw new DnsProtocolException("Unexpected end of DNS message while reading byte.");

        return _message[_position++];
    }

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0)
            throw new DnsProtocolException($"Invalid DNS record data length: {count}.");

        if (_position + count > _limit)
            throw new DnsProtocolException($"Unexpected end of DNS message while reading {count} bytes.");

        var span = _message.Slice(_position, count);
        _position += count;
        return span;
    }

    public void Skip(int count)
    {
        if (count < 0 || _position + count > _limit)
            throw new DnsProtocolException($"Cannot skip {count} bytes: exceeds message boundary.");

        _position += count;
    }

    public string ReadDomainName()
    {
        var sb = new StringBuilder(64);
        ReadDomainNameCore(sb, _message, ref _position, _limit);
        return sb.ToString();
    }

    private static void ReadDomainNameCore(StringBuilder sb, ReadOnlySpan<byte> message, ref int position, int limit)
    {
        var jumped = false;
        var originalPosition = -1;

        // The encoded length of the name, including the terminating root label. Bounding this is what
        // keeps a maliciously compressed name from expanding without limit.
        var encodedLength = 1;

        while (true)
        {
            // Before the first pointer the name has to stay inside the current record; a pointer may
            // target any earlier offset of the message.
            var bound = jumped ? message.Length : limit;

            if (position >= bound)
                throw new DnsProtocolException("Unexpected end of DNS message while reading a domain name.");

            var length = message[position];

            if (length is 0)
            {
                position++;
                break;
            }

            // Check for compression pointer (top 2 bits set)
            if ((length & 0xC0) is 0xC0)
            {
                if (position + 2 > bound)
                    throw new DnsProtocolException("Unexpected end of DNS message while reading compression pointer.");

                var pointer = ((length & 0x3F) << 8) | message[position + 1];

                // RFC 1035 4.1.4 defines a pointer as a reference to a *prior* occurrence of a name.
                // Requiring a strictly backwards jump makes compression loops structurally impossible.
                if (pointer >= position)
                    throw new DnsProtocolException("Invalid compression pointer: it must point to an earlier offset in the message.");

                if (!jumped)
                {
                    originalPosition = position + 2;
                    jumped = true;
                }

                position = pointer;
                continue;
            }

            if ((length & 0xC0) != 0)
                throw new DnsProtocolException($"Invalid label type: 0x{length:X2}.");

            position++;

            if (position + length > bound)
                throw new DnsProtocolException("Domain name label extends beyond message boundary.");

            encodedLength += length + 1;
            if (encodedLength > MaxDomainNameLength)
                throw new DnsProtocolException($"Domain name exceeds the maximum encoded length of {MaxDomainNameLength} bytes.");

            if (sb.Length > 0)
            {
                sb.Append('.');
            }

            sb.Append(Encoding.ASCII.GetString(message.Slice(position, length)));
            position += length;
        }

        if (jumped)
        {
            position = originalPosition;
        }
    }
}
