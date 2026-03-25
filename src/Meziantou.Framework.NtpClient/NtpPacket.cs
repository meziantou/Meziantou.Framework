using System.Runtime.InteropServices;

namespace Meziantou.Framework.NtpClient;

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
            RootDelay = (uint)(buffer[4] << 24 | buffer[5] << 16 | buffer[6] << 8 | buffer[7]),
            RootDispersion = (uint)(buffer[8] << 24 | buffer[9] << 16 | buffer[10] << 8 | buffer[11]),
            ReferenceIdentifier = (uint)(buffer[12] << 24 | buffer[13] << 16 | buffer[14] << 8 | buffer[15]),
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
        buffer[4] = (byte)(RootDelay >> 24);
        buffer[5] = (byte)(RootDelay >> 16);
        buffer[6] = (byte)(RootDelay >> 8);
        buffer[7] = (byte)RootDelay;
        buffer[8] = (byte)(RootDispersion >> 24);
        buffer[9] = (byte)(RootDispersion >> 16);
        buffer[10] = (byte)(RootDispersion >> 8);
        buffer[11] = (byte)RootDispersion;
        buffer[12] = (byte)(ReferenceIdentifier >> 24);
        buffer[13] = (byte)(ReferenceIdentifier >> 16);
        buffer[14] = (byte)(ReferenceIdentifier >> 8);
        buffer[15] = (byte)ReferenceIdentifier;
        NtpTimestamp.Encode(ReferenceTimestamp, buffer[16..]);
        NtpTimestamp.Encode(OriginateTimestamp, buffer[24..]);
        NtpTimestamp.Encode(ReceiveTimestamp, buffer[32..]);
        NtpTimestamp.Encode(TransmitTimestamp, buffer[40..]);
    }
}
