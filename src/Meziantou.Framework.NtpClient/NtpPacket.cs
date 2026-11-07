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

    public const int ReferenceTimestampOffset = 16;
    public const int OriginateTimestampOffset = 24;
    public const int ReceiveTimestampOffset = 32;
    public const int TransmitTimestampOffset = 40;

    /// <summary>The stratum value that marks a Kiss-o'-Death packet (RFC 5905 section 7.4).</summary>
    public const byte KissOfDeathStratum = 0;

    public NtpLeapIndicator LeapIndicator { get; set; }
    public NtpVersion Version { get; set; }
    public NtpMode Mode { get; set; }
    public byte Stratum { get; set; }
    public sbyte PollInterval { get; set; }
    public sbyte Precision { get; set; }
    public TimeSpan RootDelay { get; set; }
    public TimeSpan RootDispersion { get; set; }
    public uint ReferenceIdentifier { get; set; }
    public DateTimeOffset? ReferenceTimestamp { get; set; }
    public DateTimeOffset? OriginateTimestamp { get; set; }
    public DateTimeOffset? ReceiveTimestamp { get; set; }
    public DateTimeOffset? TransmitTimestamp { get; set; }

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
            RootDelay = NtpTimestamp.DecodeShortFormat(buffer.Slice(4, 4)),
            RootDispersion = NtpTimestamp.DecodeShortFormat(buffer.Slice(8, 4)),
            ReferenceIdentifier = BinaryPrimitives.ReadUInt32BigEndian(buffer.Slice(12, 4)),
            ReferenceTimestamp = NtpTimestamp.Decode(buffer[ReferenceTimestampOffset..]),
            OriginateTimestamp = NtpTimestamp.Decode(buffer[OriginateTimestampOffset..]),
            ReceiveTimestamp = NtpTimestamp.Decode(buffer[ReceiveTimestampOffset..]),
            TransmitTimestamp = NtpTimestamp.Decode(buffer[TransmitTimestampOffset..]),
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
        NtpTimestamp.EncodeShortFormat(RootDelay, buffer.Slice(4, 4));
        NtpTimestamp.EncodeShortFormat(RootDispersion, buffer.Slice(8, 4));
        BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(12, 4), ReferenceIdentifier);
        NtpTimestamp.Encode(ReferenceTimestamp, buffer[ReferenceTimestampOffset..]);
        NtpTimestamp.Encode(OriginateTimestamp, buffer[OriginateTimestampOffset..]);
        NtpTimestamp.Encode(ReceiveTimestamp, buffer[ReceiveTimestampOffset..]);
        NtpTimestamp.Encode(TransmitTimestamp, buffer[TransmitTimestampOffset..]);
    }

    /// <summary>
    /// Formats the reference identifier as the four ASCII characters NTP uses for stratum 0 and 1
    /// (for example <c>GPS</c>, or the <c>RATE</c> and <c>DENY</c> Kiss-o'-Death codes), or
    /// <see langword="null"/> when it does not hold printable ASCII.
    /// </summary>
    public static string? FormatReferenceIdentifier(uint referenceIdentifier)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < chars.Length; i++)
        {
            var c = (char)((referenceIdentifier >> ((3 - i) * 8)) & 0xFF);
            if (c is '\0')
            {
                chars = chars[..i];
                break;
            }

            if (c is < ' ' or > '~')
                return null;

            chars[i] = c;
        }

        return chars.IsEmpty ? null : new string(chars);
    }
}
