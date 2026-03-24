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

    public readonly int Remaining => _message.Length - _position;

    public ushort ReadUInt16()
    {
        if (_position + 2 > _message.Length)
            throw new DnsProtocolException("Unexpected end of DNS message while reading UInt16.");

        var value = BinaryPrimitives.ReadUInt16BigEndian(_message[_position..]);
        _position += 2;
        return value;
    }

    public uint ReadUInt32()
    {
        if (_position + 4 > _message.Length)
            throw new DnsProtocolException("Unexpected end of DNS message while reading UInt32.");

        var value = BinaryPrimitives.ReadUInt32BigEndian(_message[_position..]);
        _position += 4;
        return value;
    }

    public int ReadInt32()
    {
        if (_position + 4 > _message.Length)
            throw new DnsProtocolException("Unexpected end of DNS message while reading Int32.");

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
        if (_position + count > _message.Length)
            throw new DnsProtocolException($"Unexpected end of DNS message while reading {count} bytes.");

        var span = _message.Slice(_position, count);
        _position += count;
        return span;
    }

    public void Skip(int count)
    {
        if (_position + count > _message.Length)
            throw new DnsProtocolException($"Cannot skip {count} bytes: exceeds message boundary.");

        _position += count;
    }

    public string ReadDomainName()
    {
        var sb = new StringBuilder(64);
        ReadDomainNameCore(sb, _message, ref _position, maxPointers: 128);
        return sb.ToString();
    }

    public static string ReadDomainNameAtOffset(ReadOnlySpan<byte> message, int offset)
    {
        var sb = new StringBuilder(64);
        ReadDomainNameCore(sb, message, ref offset, maxPointers: 128);
        return sb.ToString();
    }

    private static void ReadDomainNameCore(StringBuilder sb, ReadOnlySpan<byte> message, ref int position, int maxPointers)
    {
        var jumped = false;
        var originalPosition = -1;
        var pointerCount = 0;

        while (position < message.Length)
        {
            var length = message[position];

            if (length is 0)
            {
                position++;
                break;
            }

            // Check for compression pointer (top 2 bits set)
            if ((length & 0xC0) is 0xC0)
            {
                if (++pointerCount > maxPointers)
                    throw new DnsProtocolException("Too many compression pointers in domain name (possible loop).");

                if (position + 1 >= message.Length)
                    throw new DnsProtocolException("Unexpected end of DNS message while reading compression pointer.");

                var pointer = ((length & 0x3F) << 8) | message[position + 1];

                if (!jumped)
                {
                    originalPosition = position + 2;
                }

                position = pointer;
                jumped = true;
                continue;
            }

            if ((length & 0xC0) != 0)
                throw new DnsProtocolException($"Invalid label type: 0x{length:X2}.");

            position++;

            if (position + length > message.Length)
                throw new DnsProtocolException("Domain name label extends beyond message boundary.");

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
