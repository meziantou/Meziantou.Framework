using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Svg.Animations;

/// <summary>
/// A time of the SMIL timing model (https://www.w3.org/TR/SMIL3/smil-timing.html), in microseconds: a finite time, or
/// <see cref="Indefinite"/> or <see cref="Unresolved"/> (which are greater than all the finite times, unresolved being the
/// greatest). The integer representation makes the comparisons of the interval ends exact.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct SmilTime : IEquatable<SmilTime>, IComparable<SmilTime>
{
    private const long TicksPerSecond = 1_000_000;

    private readonly long _ticks;

    private SmilTime(long ticks) => _ticks = ticks;

    public static SmilTime Zero => default;

    /// <summary>The time of an instance that is not resolved yet (e.g. an event that did not occur).</summary>
    public static SmilTime Unresolved => new(long.MaxValue);

    public static SmilTime Indefinite => new(long.MaxValue - 1);

    /// <summary>A time before all the times.</summary>
    public static SmilTime Earliest => new(long.MinValue);

    /// <summary>The smallest difference between two times.</summary>
    public static SmilTime Epsilon => new(1);

    public bool IsUnresolved => _ticks == long.MaxValue;

    public bool IsIndefinite => _ticks == long.MaxValue - 1;

    public bool IsFinite => _ticks < long.MaxValue - 1 && _ticks > long.MinValue;

    public bool IsZero => _ticks == 0;

    public long Ticks => _ticks;

    public double Seconds => (double)_ticks / TicksPerSecond;

    public static SmilTime FromSeconds(double seconds)
    {
        if (double.IsNaN(seconds))
            return Unresolved;

        var ticks = Math.Round(seconds * TicksPerSecond, MidpointRounding.AwayFromZero);
        if (ticks >= long.MaxValue - 1)
            return Indefinite;

        // The times that are not representable are clamped (the earliest time is not a valid time)
        if (ticks <= long.MinValue)
            return new SmilTime(long.MinValue + 1);

        return new SmilTime((long)ticks);
    }

    public static SmilTime FromTicks(long ticks) => new(ticks);

    public static SmilTime FromMilliseconds(double milliseconds) => FromSeconds(milliseconds / 1000);

    public static SmilTime operator +(SmilTime a, SmilTime b)
    {
        if (!a.IsFinite)
            return a;

        if (!b.IsFinite)
            return b;

        var result = a._ticks + b._ticks;

        // Overflow
        if (((a._ticks ^ result) & (b._ticks ^ result)) < 0)
            return a._ticks > 0 ? Indefinite : new SmilTime(long.MinValue + 1);

        return result >= long.MaxValue - 1 ? Indefinite : new SmilTime(result);
    }

    public static SmilTime operator -(SmilTime a, SmilTime b)
    {
        if (!a.IsFinite)
            return a;

        if (!b.IsFinite)
            return b;

        return a + new SmilTime(-b._ticks);
    }

    public static SmilTime operator -(SmilTime a) => a.IsFinite ? new SmilTime(-a._ticks) : a;

    /// <summary>The time repeated <paramref name="count"/> times (a fractional count is allowed).</summary>
    public SmilTime Repeat(double count)
    {
        if (!IsFinite)
            return this;

        if (double.IsPositiveInfinity(count))
            return _ticks == 0 ? Zero : Indefinite;

        return FromSeconds(Seconds * count);
    }

    public static bool operator ==(SmilTime a, SmilTime b) => a._ticks == b._ticks;

    public static bool operator !=(SmilTime a, SmilTime b) => a._ticks != b._ticks;

    public static bool operator <(SmilTime a, SmilTime b) => a._ticks < b._ticks;

    public static bool operator >(SmilTime a, SmilTime b) => a._ticks > b._ticks;

    public static bool operator <=(SmilTime a, SmilTime b) => a._ticks <= b._ticks;

    public static bool operator >=(SmilTime a, SmilTime b) => a._ticks >= b._ticks;

    public static SmilTime Min(SmilTime a, SmilTime b) => a._ticks <= b._ticks ? a : b;

    public static SmilTime Max(SmilTime a, SmilTime b) => a._ticks >= b._ticks ? a : b;

    public int CompareTo(SmilTime other) => _ticks.CompareTo(other._ticks);

    public bool Equals(SmilTime other) => _ticks == other._ticks;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SmilTime other && Equals(other);

    public override int GetHashCode() => _ticks.GetHashCode();

    public override string ToString() => IsUnresolved ? "unresolved" : IsIndefinite ? "indefinite" : Seconds.ToString(CultureInfo.InvariantCulture) + "s";

    /// <summary>
    /// Parses a clock value (https://www.w3.org/TR/SMIL3/smil-timing.html#Timing-ClockValueSyntax): a full clock value
    /// (<c>hh:mm:ss.fff</c>), a partial clock value (<c>mm:ss.fff</c>) or a timecount value (<c>3.2h</c>, <c>45min</c>,
    /// <c>30s</c>, <c>5ms</c>, <c>12.467</c>), or <c>indefinite</c>. Returns <see cref="Unresolved"/> for an invalid value.
    /// </summary>
    public static SmilTime ParseClockValue(string? value, bool allowIndefinite = true)
    {
        if (value is null)
            return Unresolved;

        var text = value.AsSpan().Trim(" \t\n\f\r");
        if (text.Length == 0)
            return Unresolved;

        if (text is "indefinite")
            return allowIndefinite ? Indefinite : Unresolved;

        var firstColon = text.IndexOf(':');
        if (firstColon < 0)
            return ParseTimecount(text);

        var rest = text[(firstColon + 1)..];
        var secondColon = rest.IndexOf(':');
        double seconds;
        if (secondColon >= 0)
        {
            // Full clock value: hours ":" minutes ":" seconds ("." fraction)?
            var minutes = rest[..secondColon];
            if (!TryParseDigits(text[..firstColon], out var hours) || minutes.Length != 2 || !TryParseDigits(minutes, out var minuteCount) || minuteCount > 59
                || !TryParseSeconds(rest[(secondColon + 1)..], out var secondCount))
            {
                return Unresolved;
            }

            seconds = (hours * 3600d) + (minuteCount * 60d) + secondCount;
        }
        else
        {
            // Partial clock value: minutes ":" seconds ("." fraction)?
            var minutes = text[..firstColon];
            if (minutes.Length != 2 || !TryParseDigits(minutes, out var minuteCount) || minuteCount > 59 || !TryParseSeconds(rest, out var secondCount))
                return Unresolved;

            seconds = (minuteCount * 60d) + secondCount;
        }

        return FromSeconds(seconds);
    }

    private static bool TryParseDigits(ReadOnlySpan<char> text, out long result)
    {
        result = 0;
        if (text.Length == 0)
            return false;

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;

            result = Math.Min((result * 10) + (c - '0'), int.MaxValue);
        }

        return true;
    }

    // Seconds: two digits, with an optional fraction
    private static bool TryParseSeconds(ReadOnlySpan<char> text, out double result)
    {
        result = 0;
        var dot = text.IndexOf('.');
        var integer = dot < 0 ? text : text[..dot];
        if (integer.Length != 2 || !TryParseDigits(integer, out var seconds) || seconds > 59)
            return false;

        result = seconds;
        if (dot >= 0)
        {
            var fraction = text[(dot + 1)..];
            if (!TryParseDigits(fraction, out _))
                return false;

            result += double.Parse(string.Concat("0.", fraction), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return true;
    }

    /// <summary>Parses a timecount value: a number with an optional metric (<c>h</c>, <c>min</c>, <c>s</c>, <c>ms</c>; seconds by default).</summary>
    private static SmilTime ParseTimecount(ReadOnlySpan<char> text)
    {
        var scale = 1d;
        if (text.EndsWith("ms", StringComparison.Ordinal))
        {
            scale = 0.001;
            text = text[..^2];
        }
        else if (text.EndsWith("min", StringComparison.Ordinal))
        {
            scale = 60;
            text = text[..^3];
        }
        else if (text.EndsWith("h", StringComparison.Ordinal))
        {
            scale = 3600;
            text = text[..^1];
        }
        else if (text.EndsWith("s", StringComparison.Ordinal))
        {
            text = text[..^1];
        }

        // digits ("." digits)?, with an optional sign for the offsets
        var position = 0;
        if (position < text.Length && text[position] is '+' or '-')
        {
            position++;
        }

        var digits = 0;
        while (position < text.Length && char.IsAsciiDigit(text[position]))
        {
            position++;
            digits++;
        }

        if (position < text.Length && text[position] == '.')
        {
            position++;
            var fraction = 0;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
                fraction++;
            }

            if (fraction == 0)
                return Unresolved;
        }

        if (digits == 0 || position != text.Length)
            return Unresolved;

        return FromSeconds(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) * scale);
    }
}

/// <summary>An interval of a timed element: the begin time is included, the end time is excluded.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct SmilInterval(SmilTime Begin, SmilTime End)
{
    public static SmilInterval Unresolved => new(SmilTime.Unresolved, SmilTime.Unresolved);

    public bool IsResolved => Begin.IsFinite;

    public bool Contains(SmilTime time) => IsResolved && Begin <= time && time < End;
}
