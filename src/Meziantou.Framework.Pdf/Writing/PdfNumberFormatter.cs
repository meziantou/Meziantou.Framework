using System.Globalization;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Formats numbers as the PDF syntax writes them (ISO 32000-2, 7.3.3): no exponent, at most six decimals, no negative zero.</summary>
internal static class PdfNumberFormatter
{
    public const int MaxDecimals = 6;

    // The longest double written without exponent: 309 digits, a sign, a point and six decimals
    public const int MaxLength = 320;

    // Below this magnitude, the distance between two doubles is less than half a millionth: a double that is a number of
    // millionths within that distance is written with the digits of that number by the general format
    private const double FixedPointLimit = 1 << 30;

    public static int Format(double value, Span<byte> destination)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A PDF number must be finite");

        var rounded = Math.Round(value, MaxDecimals, MidpointRounding.AwayFromZero);
        if (rounded == 0)
        {
            destination[0] = (byte)'0';
            return 1;
        }

        // The rounded value is a number of millionths within the precision of a double: most values are written from that
        // integer, without the general formatting of a double
        if (Math.Abs(rounded) < FixedPointLimit)
            return FormatMillionths((long)Math.Round(rounded * 1_000_000, MidpointRounding.AwayFromZero), destination);

        return FormatRounded(rounded, destination);
    }

    /// <summary>Formats a value rounded to six decimals with the general format of a double.</summary>
    internal static int FormatRounded(double rounded, Span<byte> destination)
    {
        // "F6" writes every digit of the integer part, where a custom format such as "0.######" keeps 15 significant
        // digits only (9007199254740992 would be written 9007199254740990); the zeros of the decimals are then removed
        if (!rounded.TryFormat(destination, out var written, "F6", CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The destination is too small");

        while (destination[written - 1] == (byte)'0')
        {
            written--;
        }

        if (destination[written - 1] == (byte)'.')
        {
            written--;
        }

        return written;
    }

    /// <summary>Writes a number of millionths as a decimal number, without the zeros at the end of its decimals.</summary>
    private static int FormatMillionths(long millionths, Span<byte> destination)
    {
        var position = 0;
        if (millionths < 0)
        {
            destination[position++] = (byte)'-';
            millionths = -millionths;
        }

        var integerPart = (ulong)millionths / 1_000_000;
        var fraction = (uint)((ulong)millionths % 1_000_000);
        if (!integerPart.TryFormat(destination[position..], out var written, default, CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The destination is too small");

        position += written;
        if (fraction != 0)
        {
            var decimals = MaxDecimals;
            while (fraction % 10 == 0)
            {
                fraction /= 10;
                decimals--;
            }

            destination[position++] = (byte)'.';
            for (var i = position + decimals - 1; i >= position; i--)
            {
                destination[i] = (byte)('0' + (fraction % 10));
                fraction /= 10;
            }

            position += decimals;
        }

        return position;
    }

    public static string Format(double value)
    {
        Span<byte> buffer = stackalloc byte[MaxLength];
        var length = Format(value, buffer);
        return System.Text.Encoding.ASCII.GetString(buffer[..length]);
    }
}
