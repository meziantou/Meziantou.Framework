using System.Numerics;

namespace Meziantou.Framework.Csv.Serialization.Converters;

/// <summary>Converts <see cref="decimal"/> values. A plain number, such as <c>-123.45</c>, read with the invariant culture, is parsed faster than with <see cref="decimal.Parse(ReadOnlySpan{char}, IFormatProvider?)"/>.</summary>
internal sealed class DecimalCsvConverter : CsvConverter<decimal>
{
    // 10^18 < 2^63: 18 digits always fit in a ulong
    private const int MaxFastDigits = 18;

    public override decimal Read(ReadOnlySpan<char> value, CsvConverterContext context)
    {
        if (IsInvariant(context.FormatProvider) && TryParsePlain(value, out var result))
            return result;

        return decimal.Parse(value, context.FormatProvider);
    }

    public override decimal Read(ReadOnlySpan<byte> utf8Value, CsvConverterContext context)
    {
        if (IsInvariant(context.FormatProvider) && TryParsePlain(utf8Value, out var result))
            return result;

        return decimal.Parse(utf8Value, context.FormatProvider);
    }

    public override bool TryFormat(decimal value, Span<char> destination, out int charsWritten, CsvConverterContext context)
    {
        if (context.Format is null && IsInvariant(context.FormatProvider) && TryFormatPlain(value, destination, out charsWritten, out var tooSmall))
            return !tooSmall;

        return value.TryFormat(destination, out charsWritten, context.Format, context.FormatProvider);
    }

    public override bool TryFormat(decimal value, Span<byte> utf8Destination, out int bytesWritten, CsvConverterContext context)
    {
        if (context.Format is null && IsInvariant(context.FormatProvider) && TryFormatPlain(value, utf8Destination, out bytesWritten, out var tooSmall))
            return !tooSmall;

        return value.TryFormat(utf8Destination, out bytesWritten, context.Format, context.FormatProvider);
    }

    private static bool IsInvariant(IFormatProvider provider) => ReferenceEquals(provider, CultureInfo.InvariantCulture) || ReferenceEquals(provider, NumberFormatInfo.InvariantInfo);

    /// <summary>Parses an optional minus sign, then digits, then optionally a dot followed by digits, up to 18 digits in all.</summary>
    /// <remarks>The scale is the number of digits after the dot, as with <see cref="decimal.Parse(string)"/>: <c>1.50</c> keeps its two decimals.</remarks>
    internal static bool TryParsePlain<TChar>(ReadOnlySpan<TChar> value, out decimal result)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        result = default;
        var negative = !value.IsEmpty && value[0] == TChar.CreateTruncating('-');
        if (negative)
        {
            value = value[1..];
        }

        if (value.IsEmpty || value.Length > MaxFastDigits + 1)
            return false;

        ulong number = 0;
        var digits = 0;
        var scale = -1;
        for (var i = 0; i < value.Length; i++)
        {
            var digit = uint.CreateTruncating(value[i]) - '0';
            if (digit <= 9)
            {
                number = (number * 10) + digit;
                digits++;
                continue;
            }

            // A single dot, between digits
            if (value[i] != TChar.CreateTruncating('.') || scale >= 0 || i == 0 || i == value.Length - 1)
                return false;

            scale = value.Length - i - 1;
        }

        // The general parser decides the sign of a negative zero
        if (digits > MaxFastDigits || (negative && number == 0))
            return false;

        result = new decimal((int)(uint)number, (int)(uint)(number >> 32), 0, negative, (byte)Math.Max(scale, 0));
        return true;
    }

    /// <summary>Formats a value whose 96-bit integer fits in 64 bits as <see cref="decimal.ToString(IFormatProvider?)"/> does with the invariant culture: every digit of the scale, with no exponent.</summary>
    /// <returns><see langword="true"/> when the value is handled, written or with <paramref name="tooSmall"/> set; <see langword="false"/> to use the general formatter.</returns>
    internal static bool TryFormatPlain<TChar>(decimal value, Span<TChar> destination, out int written, out bool tooSmall)
        where TChar : unmanaged, IBinaryInteger<TChar>
    {
        written = 0;
        tooSmall = false;
        Span<int> bits = unsafe(stackalloc int[4]);
        decimal.GetBits(value, bits);
        var negative = bits[3] < 0;
        var scale = (bits[3] >> 16) & 0xFF;
        var number = (uint)bits[0] | ((ulong)(uint)bits[1] << 32);

        // The general formatter decides how a negative zero is written
        if (bits[2] != 0 || (negative && number == 0))
            return false;

        // The text is written from the end, in one pass: the fraction, padded with zeros to the scale, the dot, then the integer part, at least one
        // digit. 20 digits, 28 zeros of the scale, the dot and the sign fit in 64 elements.
        Span<TChar> buffer = unsafe(stackalloc TChar[64]);
        var position = buffer.Length;
        for (var i = 0; i < scale; i++)
        {
            (number, var digit) = Math.DivRem(number, 10);
            buffer[--position] = TChar.CreateTruncating('0' + (int)digit);
        }

        if (scale > 0)
        {
            buffer[--position] = TChar.CreateTruncating('.');
        }

        do
        {
            (number, var digit) = Math.DivRem(number, 10);
            buffer[--position] = TChar.CreateTruncating('0' + (int)digit);
        }
        while (number != 0);

        if (negative)
        {
            buffer[--position] = TChar.CreateTruncating('-');
        }

        var text = buffer[position..];
        if (!text.TryCopyTo(destination))
        {
            tooSmall = true;
            return true;
        }

        written = text.Length;
        return true;
    }
}
