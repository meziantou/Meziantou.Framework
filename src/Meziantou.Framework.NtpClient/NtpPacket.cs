using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// Represents a 48-byte NTP packet as defined in RFC 5905.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct NtpPacket
{
    public const int PacketSize = 48;

    public NtpLeapIndicator LeapIndicator { get; set; }
    public NtpVersion Version { get; set; }
    public NtpMode Mode { get; set; }
    public byte Stratum { get; set; }
    public sbyte PollInterval { get; set; }
    public sbyte Precision { get; set; }
    public uint RootDelay { get; set; }
    public uint RootDispersion { get; set; }
    public uint ReferenceIdentifier { get; set; }
    public DateTimeOffset ReferenceTimestamp { get; set; }
    public DateTimeOffset OriginateTimestamp { get; set; }
    public DateTimeOffset ReceiveTimestamp { get; set; }
    public DateTimeOffset TransmitTimestamp { get; set; }

    public static NtpPacket CreateClientRequest(NtpVersion version)
    {
        return new NtpPacket
        {
            LeapIndicator = NtpLeapIndicator.NoWarning,
            Version = version,
            Mode = NtpMode.Client,
        };
    }

    public static NtpPacket Decode(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < PacketSize)
            throw new ArgumentException($"NTP packet must be at least {PacketSize} bytes, but was {buffer.Length} bytes.", nameof(buffer));

        var firstByte = buffer[0];

        return new NtpPacket
        {
            LeapIndicator = (NtpLeapIndicator)((firstByte >> 6) & 0x03),
            Version = (NtpVersion)((firstByte >> 3) & 0x07),
            Mode = (NtpMode)(firstByte & 0x07),
            Stratum = buffer[1],
            PollInterval = (sbyte)buffer[2],
            Precision = (sbyte)buffer[3],
            RootDelay = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(4, 4)),
            RootDispersion = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(8, 4)),
            ReferenceIdentifier = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(12, 4)),
            ReferenceTimestamp = NtpTimestamp.Decode(buffer[16..]),
            OriginateTimestamp = NtpTimestamp.Decode(buffer[24..]),
            ReceiveTimestamp = NtpTimestamp.Decode(buffer[32..]),
            TransmitTimestamp = NtpTimestamp.Decode(buffer[40..]),
        };
    }

    public readonly void Encode(Span<byte> buffer)
    {
        if (buffer.Length < PacketSize)
            throw new ArgumentException($"Buffer must be at least {PacketSize} bytes, but was {buffer.Length} bytes.", nameof(buffer));

        buffer.Clear();

        buffer[0] = (byte)(((int)LeapIndicator << 6) | ((int)Version << 3) | (int)Mode);
        buffer[1] = Stratum;
        buffer[2] = (byte)PollInterval;
        buffer[3] = (byte)Precision;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(4, 4), RootDelay);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(8, 4), RootDispersion);
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(12, 4), ReferenceIdentifier);
        NtpTimestamp.Encode(ReferenceTimestamp, buffer[16..]);
        NtpTimestamp.Encode(OriginateTimestamp, buffer[24..]);
        NtpTimestamp.Encode(ReceiveTimestamp, buffer[32..]);
        NtpTimestamp.Encode(TransmitTimestamp, buffer[40..]);
    }
}
