using System.Globalization;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>Writes numbers and paths for SVG: invariant culture, a fixed number of decimals, no trailing zeros, no negative zero.</summary>
internal static class SvgNumber
{
    /// <summary>The length of the buffers of <see cref="TryFormat"/>: more than a sign, 15 digits and a decimal point.</summary>
    public const int MaxFastLength = 32;

    private static readonly string[] Formats = [.. Enumerable.Range(0, 16).Select(decimals => decimals is 0 ? "0" : "0." + new string('#', decimals))];

    private static readonly long[] PowersOfTen =
    [
        1, 10, 100, 1_000, 10_000, 100_000, 1_000_000, 10_000_000, 100_000_000, 1_000_000_000, 10_000_000_000, 100_000_000_000,
        1_000_000_000_000, 10_000_000_000_000, 100_000_000_000_000, 1_000_000_000_000_000,
    ];

    public static string Format(double value, int precision)
    {
        Span<char> buffer = stackalloc char[MaxFastLength];
        if (TryFormat(value, precision, buffer, out var written))
            return buffer[..written].ToString();

        return FormatSlow(value, precision);
    }

    /// <summary>
    /// Writes a number as <see cref="Format"/> does, without allocating when it has 15 significant digits at most. Returns
    /// <see langword="false"/> when the number does not fit the destination, which can happen from 16 significant digits.
    /// </summary>
    public static bool TryFormat(double value, int precision, Span<char> destination, out int charsWritten)
    {
        if (!double.IsFinite(value) || (uint)precision >= PowersOfTen.Length || destination.Length < MaxFastLength)
            return TryCopy(FormatSlow(value, precision), destination, out charsWritten);

        // The rounding of DecimalRounding.Round, keeping the integer: the value is k / 10^precision
        var first = DecimalRounding.Round(value, precision + 6);
        var scale = (double)PowersOfTen[precision];
        var scaled = first * scale;
        if (!(Math.Abs(scaled) < 1e15))
            return TryCopy(FormatSlow(value, precision), destination, out charsWritten);

        // The custom format writes the 15 significant digits of the double nearest k / 10^precision, which are the digits
        // of k as long as k has 15 digits at most
        var integer = (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        if (integer is 0)
        {
            destination[0] = '0';
            charsWritten = 1;
            return true;
        }

        var position = 0;
        if (integer < 0)
        {
            destination[position++] = '-';
            integer = -integer;
        }

        var divisor = PowersOfTen[precision];
        var (whole, fraction) = Math.DivRem(integer, divisor);
        _ = whole.TryFormat(destination[position..], out var wholeLength, provider: CultureInfo.InvariantCulture);
        position += wholeLength;
        if (fraction != 0)
        {
            // The decimals, with their leading zeros, without the trailing ones
            var decimals = precision;
            while (fraction % 10 is 0)
            {
                fraction /= 10;
                decimals--;
            }

            destination[position++] = '.';
            for (var i = decimals - 1; i >= 0; i--)
            {
                destination[position + i] = (char)('0' + (fraction % 10));
                fraction /= 10;
            }

            position += decimals;
        }

        charsWritten = position;
        return true;

        static bool TryCopy(string text, Span<char> destination, out int charsWritten)
        {
            if (text.TryCopyTo(destination))
            {
                charsWritten = text.Length;
                return true;
            }

            charsWritten = 0;
            return false;
        }
    }

    /// <summary>Writes a number with the custom format of .NET; <see cref="TryFormat"/> gives the same result faster.</summary>
    internal static string FormatSlow(double value, int precision)
    {
        if (!double.IsFinite(value))
            return "0";

        // The last bits of a computed coordinate can differ between runtimes (fused multiply-add): snap the value to a
        // finer grid first, so the noise cannot move the value across a midpoint
        var rounded = DecimalRounding.Round(DecimalRounding.Round(value, precision + 6), precision);
        if (rounded == 0)
            return "0";

        return rounded.ToString(Formats[precision], CultureInfo.InvariantCulture);
    }

    public static string FormatPath(PathData path, int precision)
    {
        using var output = new SvgOutput(initialCapacity: 256);
        WritePath(output, path, precision);
        return output.ToString();
    }

    /// <summary>Writes the commands of a path, separated by spaces: the characters never need to be escaped in XML.</summary>
    public static void WritePath(SvgOutput output, PathData path, int precision)
    {
        var first = true;
        foreach (ref readonly var command in path.CommandSpan)
        {
            if (!first)
            {
                output.Write(' ');
            }

            first = false;
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    output.Write('M');
                    WritePoint(output, command.X, command.Y, precision);
                    break;
                case PathCommandKind.LineTo:
                    output.Write('L');
                    WritePoint(output, command.X, command.Y, precision);
                    break;
                case PathCommandKind.CubicTo:
                    output.Write('C');
                    WritePoint(output, command.X1, command.Y1, precision);
                    output.Write(' ');
                    WritePoint(output, command.X2, command.Y2, precision);
                    output.Write(' ');
                    WritePoint(output, command.X, command.Y, precision);
                    break;
                case PathCommandKind.QuadraticTo:
                    output.Write('Q');
                    WritePoint(output, command.X1, command.Y1, precision);
                    output.Write(' ');
                    WritePoint(output, command.X, command.Y, precision);
                    break;
                case PathCommandKind.ArcTo:
                    output.Write('A');
                    WritePoint(output, command.X1, command.Y1, precision);
                    output.Write(' ');
                    Write(output, command.X2, precision);
                    output.Write(' ');
                    output.Write(command.LargeArc ? '1' : '0');
                    output.Write(',');
                    output.Write(command.Sweep ? '1' : '0');
                    output.Write(' ');
                    WritePoint(output, command.X, command.Y, precision);
                    break;
                case PathCommandKind.Close:
                    output.Write('Z');
                    break;
            }
        }
    }

    /// <summary>Writes a number as <see cref="Format"/> does.</summary>
    public static void Write(SvgOutput output, double value, int precision)
    {
        if (TryFormat(value, precision, output.GetSpan(MaxFastLength), out var written))
        {
            output.Advance(written);
        }
        else
        {
            output.Write(FormatSlow(value, precision));
        }
    }

    private static void WritePoint(SvgOutput output, double x, double y, int precision)
    {
        Write(output, x, precision);
        output.Write(',');
        Write(output, y, precision);
    }
}
