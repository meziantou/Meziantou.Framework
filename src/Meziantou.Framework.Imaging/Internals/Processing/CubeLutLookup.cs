namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The lookup of a <see cref="CubeLut"/>: per channel for a one-dimensional table, of whole colors for a three-dimensional one.</summary>
internal sealed class CubeLutLookup(CubeLut lut, LutInterpolation interpolation) : ColorLookup
{
    public override bool IsPerChannel => !lut.IsThreeDimensional;

    public override double MapChannel(int channel, double value)
    {
        if (double.IsNaN(value))
            return value;

        var last = lut.Size - 1;
        var position = Locate(value, lut.DomainMin[channel], lut.DomainMax[channel], last, out var index);
        var values = lut.Values;
        double low = values[(3 * index) + channel];
        double high = values[(3 * (index + 1)) + channel];
        return low + (position * (high - low));
    }

    public override void MapColor(ref double red, ref double green, ref double blue)
    {
        if (!lut.IsThreeDimensional)
        {
            red = MapChannel(0, red);
            green = MapChannel(1, green);
            blue = MapChannel(2, blue);
            return;
        }

        if (double.IsNaN(red) || double.IsNaN(green) || double.IsNaN(blue))
        {
            red = green = blue = double.NaN;
            return;
        }

        var size = lut.Size;
        var last = size - 1;
        var min = lut.DomainMin;
        var max = lut.DomainMax;
        var fr = Locate(red, min[0], max[0], last, out var ir);
        var fg = Locate(green, min[1], max[1], last, out var ig);
        var fb = Locate(blue, min[2], max[2], last, out var ib);
        var values = lut.Values;

        // The offsets of the corners of the cell, in values: red changes fastest
        var c000 = 3 * (ir + (size * (ig + (size * ib))));
        var dr = 3;
        var dg = 3 * size;
        var db = 3 * size * size;
        for (var c = 0; c < 3; c++)
        {
            double result;
            if (interpolation == LutInterpolation.Trilinear)
            {
                var o = c000 + c;
                var c00 = Lerp(values[o], values[o + dr], fr);
                var c10 = Lerp(values[o + dg], values[o + dg + dr], fr);
                var c01 = Lerp(values[o + db], values[o + db + dr], fr);
                var c11 = Lerp(values[o + db + dg], values[o + db + dg + dr], fr);
                result = Lerp(Lerp(c00, c10, fg), Lerp(c01, c11, fg), fb);
            }
            else
            {
                result = Tetrahedral(values, c000 + c, dr, dg, db, fr, fg, fb);
            }

            switch (c)
            {
                case 0:
                    red = result;
                    break;
                case 1:
                    green = result;
                    break;
                default:
                    blue = result;
                    break;
            }
        }
    }

    /// <summary>Clamps a value to the domain and locates it on the lattice: the index of the lower point (at most <paramref name="last"/> - 1) and the fraction of the way to the next one.</summary>
    private static double Locate(double value, double min, double max, int last, out int index)
    {
        var position = Math.Clamp((value - min) / (max - min), 0, 1) * last;
        index = Math.Min((int)position, last - 1);
        return position - index;
    }

    private static double Lerp(double a, double b, double t) => a + (t * (b - a));

    /// <summary>
    /// Blends the four corners of the tetrahedron of the cell that holds the color: the cell is split along its diagonal
    /// from the corner (0, 0, 0) to the corner (1, 1, 1), and the order of the three fractions selects one of six
    /// tetrahedra.
    /// </summary>
    private static double Tetrahedral(ReadOnlySpan<float> values, int o, int dr, int dg, int db, double fr, double fg, double fb)
    {
        double c000 = values[o];
        double c111 = values[o + dr + dg + db];
        if (fr > fg)
        {
            if (fg > fb)
                return ((1 - fr) * c000) + ((fr - fg) * values[o + dr]) + ((fg - fb) * values[o + dr + dg]) + (fb * c111);

            if (fr > fb)
                return ((1 - fr) * c000) + ((fr - fb) * values[o + dr]) + ((fb - fg) * values[o + dr + db]) + (fg * c111);

            return ((1 - fb) * c000) + ((fb - fr) * values[o + db]) + ((fr - fg) * values[o + dr + db]) + (fg * c111);
        }

        if (fb > fg)
            return ((1 - fb) * c000) + ((fb - fg) * values[o + db]) + ((fg - fr) * values[o + dg + db]) + (fr * c111);

        if (fb > fr)
            return ((1 - fg) * c000) + ((fg - fb) * values[o + dg]) + ((fb - fr) * values[o + dg + db]) + (fr * c111);

        return ((1 - fg) * c000) + ((fg - fr) * values[o + dg]) + ((fr - fb) * values[o + dr + dg]) + (fb * c111);
    }
}
