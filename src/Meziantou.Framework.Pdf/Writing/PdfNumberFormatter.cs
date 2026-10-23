using System.Globalization;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Formats numbers as the PDF syntax writes them (ISO 32000-2, 7.3.3): no exponent, at most six decimals, no negative zero.</summary>
internal static class PdfNumberFormatter
{
    public const int MaxDecimals = 6;

    // The longest double written without exponent: 309 digits, a sign, a point and six decimals
    public const int MaxLength = 320;

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

        if (!rounded.TryFormat(destination, out var written, "0.######", CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The destination is too small");

        return written;
    }

    public static string Format(double value)
    {
        Span<byte> buffer = stackalloc byte[MaxLength];
        var length = Format(value, buffer);
        return System.Text.Encoding.ASCII.GetString(buffer[..length]);
    }
}
