using System.Buffers.Binary;

namespace Meziantou.Framework.Ntp;

/// <summary>
/// Provides conversion between NTP 64-bit timestamps and <see cref="DateTimeOffset"/>.
/// NTP timestamps consist of 32 bits for seconds since 1900-01-01 and 32 bits for fractional seconds.
/// </summary>
/// <remarks>
/// The 32-bit seconds field wraps every 136 years, so it cannot by itself identify an instant.
/// Following RFC 5905 section 6, a seconds value with the high bit set belongs to era 0
/// (1968-2036) and a value with the high bit clear belongs to era 1 (2036-2104). Instants outside
/// that window cannot be represented and are rejected by <see cref="Encode"/> rather than silently
/// wrapping.
/// </remarks>
internal static class NtpTimestamp
{
    /// <summary>The size, in bytes, of an encoded NTP timestamp.</summary>
    public const int Size = 8;

    private const long SecondsPerEra = 0x1_0000_0000L;
    private const long FractionScale = 0x1_0000_0000L;
    private const long ShortFormatScale = 0x1_0000L;

    private static readonly DateTimeOffset Era0 = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Era1 = Era0.AddSeconds(SecondsPerEra);

    /// <summary>Gets the earliest instant that can be encoded (1968-01-20T03:14:08Z).</summary>
    public static DateTimeOffset MinValue { get; } = Era0.AddSeconds(SecondsPerEra / 2);

    /// <summary>Gets the first instant that is too late to be encoded (2104-02-26T09:42:24Z).</summary>
    public static DateTimeOffset ExclusiveMaxValue { get; } = Era1.AddSeconds(SecondsPerEra / 2);

    /// <summary>Decodes an NTP timestamp, returning <see langword="null"/> when the field is unset (all zero).</summary>
    public static DateTimeOffset? Decode(ReadOnlySpan<byte> buffer)
    {
        var seconds = BinaryPrimitives.ReadUInt32BigEndian(buffer);
        var fraction = BinaryPrimitives.ReadUInt32BigEndian(buffer[4..]);

        if (seconds is 0 && fraction is 0)
            return null;

        var ticks = (seconds * TimeSpan.TicksPerSecond) + ((long)fraction * TimeSpan.TicksPerSecond / FractionScale);
        var epoch = (seconds & 0x8000_0000) is not 0 ? Era0 : Era1;

        return epoch.AddTicks(ticks);
    }

    /// <summary>Encodes an NTP timestamp, writing an all-zero (unset) field when <paramref name="value"/> is <see langword="null"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="value"/> is outside the range NTP can represent
    /// (<see cref="MinValue"/> to <see cref="ExclusiveMaxValue"/>).
    /// </exception>
    public static void Encode(DateTimeOffset? value, Span<byte> buffer)
    {
        if (value is not { } instant)
        {
            buffer[..Size].Clear();
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(instant, MinValue, nameof(value));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(instant, ExclusiveMaxValue, nameof(value));

        var epoch = instant < Era1 ? Era0 : Era1;
        var ticks = (instant - epoch).Ticks;
        var seconds = (uint)(ticks / TimeSpan.TicksPerSecond);
        var remainingTicks = ticks % TimeSpan.TicksPerSecond;
        var fraction = (uint)(remainingTicks * FractionScale / TimeSpan.TicksPerSecond);

        BinaryPrimitives.WriteUInt32BigEndian(buffer, seconds);
        BinaryPrimitives.WriteUInt32BigEndian(buffer[4..], fraction);
    }

    /// <summary>Reads a 32-bit NTP short format value: 16 bits of seconds and 16 bits of fraction.</summary>
    public static TimeSpan DecodeShortFormat(ReadOnlySpan<byte> buffer)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(buffer);

        return TimeSpan.FromTicks((long)value * TimeSpan.TicksPerSecond / ShortFormatScale);
    }

    /// <summary>Writes a 32-bit NTP short format value, saturating at the ~18.2 hours it can represent.</summary>
    public static void EncodeShortFormat(TimeSpan value, Span<byte> buffer)
    {
        var scaled = value <= TimeSpan.Zero ? 0 : value.Ticks * ShortFormatScale / TimeSpan.TicksPerSecond;

        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)Math.Min(scaled, uint.MaxValue));
    }
}
