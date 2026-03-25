using System.Buffers.Binary;

namespace Meziantou.Framework.NtpClient;

/// <summary>
/// Provides conversion between NTP 64-bit timestamps and <see cref="DateTimeOffset"/>.
/// NTP timestamps consist of 32 bits for seconds since 1900-01-01 and 32 bits for fractional seconds.
/// </summary>
internal static class NtpTimestamp
{
    private static readonly DateTimeOffset NtpEpoch = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset Decode(ReadOnlySpan<byte> buffer)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(buffer[4..]);

        if (seconds == 0 && fraction == 0)
            return default;

        var ticks = (long)seconds * TimeSpan.TicksPerSecond + (long)fraction * TimeSpan.TicksPerSecond / 0x1_0000_0000L;

        return NtpEpoch.AddTicks(ticks);
    }

    public static void Encode(DateTimeOffset value, Span<byte> buffer)
    {
        if (value == default)
        {
            buffer[..8].Clear();
            return;
        }

        var ticks = (value - NtpEpoch).Ticks;
        var seconds = (uint)(ticks / TimeSpan.TicksPerSecond);
        var remainingTicks = ticks % TimeSpan.TicksPerSecond;
        var fraction = (uint)(remainingTicks * 0x1_0000_0000L / TimeSpan.TicksPerSecond);

        BinaryPrimitives.WriteUInt32BigEndian(buffer, seconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[4..], fraction);
    }
}
