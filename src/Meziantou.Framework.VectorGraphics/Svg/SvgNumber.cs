using System.Globalization;
using System.Text;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>Writes numbers and paths for SVG: invariant culture, a fixed number of decimals, no trailing zeros, no negative zero.</summary>
internal static class SvgNumber
{
    private static readonly string[] Formats = [.. Enumerable.Range(0, 16).Select(decimals => decimals is 0 ? "0" : "0." + new string('#', decimals))];

    public static string Format(double value, int precision)
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
        var builder = new StringBuilder();
        foreach (var command in path.Commands)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    builder.Append('M').Append(Format(command.X, precision)).Append(',').Append(Format(command.Y, precision));
                    break;
                case PathCommandKind.LineTo:
                    builder.Append('L').Append(Format(command.X, precision)).Append(',').Append(Format(command.Y, precision));
                    break;
                case PathCommandKind.CubicTo:
                    builder.Append('C')
                        .Append(Format(command.X1, precision)).Append(',').Append(Format(command.Y1, precision)).Append(' ')
                        .Append(Format(command.X2, precision)).Append(',').Append(Format(command.Y2, precision)).Append(' ')
                        .Append(Format(command.X, precision)).Append(',').Append(Format(command.Y, precision));
                    break;
                case PathCommandKind.QuadraticTo:
                    builder.Append('Q')
                        .Append(Format(command.X1, precision)).Append(',').Append(Format(command.Y1, precision)).Append(' ')
                        .Append(Format(command.X, precision)).Append(',').Append(Format(command.Y, precision));
                    break;
                case PathCommandKind.ArcTo:
                    builder.Append('A')
                        .Append(Format(command.X1, precision)).Append(',').Append(Format(command.Y1, precision)).Append(' ')
                        .Append(Format(command.X2, precision)).Append(' ')
                        .Append(command.LargeArc ? '1' : '0').Append(',').Append(command.Sweep ? '1' : '0').Append(' ')
                        .Append(Format(command.X, precision)).Append(',').Append(Format(command.Y, precision));
                    break;
                case PathCommandKind.Close:
                    builder.Append('Z');
                    break;
            }
        }

        return builder.ToString();
    }
}
