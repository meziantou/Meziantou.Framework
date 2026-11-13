using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

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

    /// <summary>
    /// Maps the colors of a three-dimensional table <see cref="Vector{T}.Count"/> pixels at a time (2 lanes of doubles on
    /// Arm64, 4 with AVX2): the lanes compute what <see cref="MapColor"/> computes, in the same order, the tetrahedron of
    /// each lane being selected by masks instead of branches, and the corners are read one lane at a time.
    /// </summary>
    public override int MapColors(Span<ushort> samples, double scale, int max)
    {
        if (!lut.IsThreeDimensional || !Vector.IsHardwareAccelerated || Vector<double>.Count is not (2 or 4))
            return 0;

        var size = lut.Size;
        var last = (double)(size - 1);
        var min = lut.DomainMin;
        var range = lut.DomainMax;
        var values = lut.Values;
        var lanes = Vector<double>.Count;
        var pixels = samples.Length / 4 / lanes * lanes;
        var scales = new Vector<double>(scale);
        var redMin = new Vector<double>(min[0]);
        var greenMin = new Vector<double>(min[1]);
        var blueMin = new Vector<double>(min[2]);
        var redRange = new Vector<double>(range[0] - min[0]);
        var greenRange = new Vector<double>(range[1] - min[1]);
        var blueRange = new Vector<double>(range[2] - min[2]);
        var lasts = new Vector<double>(last);
        var maxCell = new Vector<double>(last - 1);
        var levels = new Vector<double>(max);
        var dr = 3;
        var dg = 3 * size;
        var db = 3 * size * size;
        Span<int> corners = stackalloc int[4 * 4];
        for (var p = 0; p < pixels; p += lanes)
        {
            var row = samples.Slice(4 * p, 4 * lanes);
            var fr = Locate(LoadChannel(row, 0) * scales, redMin, redRange, lasts, maxCell, out var ir);
            var fg = Locate(LoadChannel(row, 1) * scales, greenMin, greenRange, lasts, maxCell, out var ig);
            var fb = Locate(LoadChannel(row, 2) * scales, blueMin, blueRange, lasts, maxCell, out var ib);

            // The offsets of the corners of the cell in values (exact integers in doubles)
            var origin = new Vector<double>(3) * (ir + (new Vector<double>(size) * (ig + (new Vector<double>(size) * ib))));
            if (interpolation == LutInterpolation.Trilinear)
            {
                for (var l = 0; l < lanes; l++)
                {
                    corners[l] = (int)origin[l];
                }

                for (var c = 0; c < 3; c++)
                {
                    var c00 = Lerp(Gather(values, corners, c), Gather(values, corners, c + dr), fr);
                    var c10 = Lerp(Gather(values, corners, c + dg), Gather(values, corners, c + dg + dr), fr);
                    var c01 = Lerp(Gather(values, corners, c + db), Gather(values, corners, c + db + dr), fr);
                    var c11 = Lerp(Gather(values, corners, c + db + dg), Gather(values, corners, c + db + dg + dr), fr);
                    Store(row, c, Quantize(Lerp(Lerp(c00, c10, fg), Lerp(c01, c11, fg), fb), levels));
                }

                continue;
            }

            // The tetrahedron of each lane (Tetrahedral): its fractions from the largest, and its second and third corners
            var rg = Vector.GreaterThan(fr, fg);
            var gb = Vector.GreaterThan(fg, fb);
            var rb = Vector.GreaterThan(fr, fb);
            var bg = Vector.GreaterThan(fb, fg);
            var br = Vector.GreaterThan(fb, fr);
            var x1 = Vector.ConditionalSelect(rg, Vector.ConditionalSelect(gb | rb, fr, fb), Vector.ConditionalSelect(bg, fb, fg));
            var x2 = Vector.ConditionalSelect(rg, Vector.ConditionalSelect(gb, fg, Vector.ConditionalSelect(rb, fb, fr)), Vector.ConditionalSelect(bg, fg, Vector.ConditionalSelect(br, fb, fr)));
            var x3 = Vector.ConditionalSelect(rg, Vector.ConditionalSelect(gb, fb, fg), Vector.ConditionalSelect(bg | br, fr, fb));
            var vr = new Vector<double>(dr);
            var vg = new Vector<double>(dg);
            var vb = new Vector<double>(db);
            var second = origin + Vector.ConditionalSelect(rg, Vector.ConditionalSelect(gb | rb, vr, vb), Vector.ConditionalSelect(bg, vb, vg));
            var third = origin + Vector.ConditionalSelect(rg, Vector.ConditionalSelect(gb, vr + vg, vr + vb), Vector.ConditionalSelect(bg | br, vg + vb, vr + vg));
            for (var l = 0; l < lanes; l++)
            {
                corners[l] = (int)origin[l];
                corners[4 + l] = (int)second[l];
                corners[8 + l] = (int)third[l];
            }

            var w0 = Vector<double>.One - x1;
            var w1 = x1 - x2;
            var w2 = x2 - x3;
            for (var c = 0; c < 3; c++)
            {
                var result = (w0 * Gather(values, corners, c)) + (w1 * Gather(values, corners[4..], c)) + (w2 * Gather(values, corners[8..], c)) + (x3 * Gather(values, corners, c + dr + dg + db));
                Store(row, c, Quantize(result, levels));
            }
        }

        return pixels;
    }

    /// <summary>Gets one sample of every pixel of a group, as doubles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> LoadChannel(ReadOnlySpan<ushort> row, int channel)
        => Vector<double>.Count == 4
            ? Vector256.Create((double)row[channel], row[channel + 4], row[channel + 8], row[channel + 12]).AsVector()
            : Vector128.Create((double)row[channel], row[channel + 4]).AsVector();

    /// <summary>Gets the values at the offsets of the lanes plus <paramref name="add"/>, as doubles.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> Gather(ReadOnlySpan<float> values, ReadOnlySpan<int> offsets, int add)
        => Vector<double>.Count == 4
            ? Vector256.Create((double)values[offsets[0] + add], values[offsets[1] + add], values[offsets[2] + add], values[offsets[3] + add]).AsVector()
            : Vector128.Create((double)values[offsets[0] + add], values[offsets[1] + add]).AsVector();

    /// <summary>Stores one sample of every pixel of a group (levels, as exact doubles).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Store(Span<ushort> row, int channel, Vector<double> levels)
    {
        for (var l = 0; l < Vector<double>.Count; l++)
        {
            row[channel + (4 * l)] = (ushort)levels[l];
        }
    }

    /// <summary>The lanes of <see cref="Locate(double, double, double, int, out int)"/>: the index is a double.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> Locate(Vector<double> value, Vector<double> min, Vector<double> range, Vector<double> last, Vector<double> maxCell, out Vector<double> index)
    {
        // Math.Clamp keeps the value unless it is below 0 or above 1
        var fraction = (value - min) / range;
        fraction = Vector.ConditionalSelect(Vector.LessThan(fraction, Vector<double>.Zero), Vector<double>.Zero, Vector.ConditionalSelect(Vector.GreaterThan(fraction, Vector<double>.One), Vector<double>.One, fraction));
        var position = fraction * last;
        index = Vector.Min(Vector.Floor(position), maxCell);
        return position - index;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> Lerp(Vector<double> a, Vector<double> b, Vector<double> t) => a + (t * (b - a));

    /// <summary>The lanes of <see cref="ColorLookup.Quantize"/>: values that are not numbers do not occur.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<double> Quantize(Vector<double> value, Vector<double> max)
    {
        var level = Vector.Min(Vector.Floor((value * max) + new Vector<double>(0.5)), max);
        level = Vector.ConditionalSelect(Vector.GreaterThanOrEqual(value, Vector<double>.One), max, level);
        return Vector.ConditionalSelect(Vector.GreaterThan(value, Vector<double>.Zero), level, Vector<double>.Zero);
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
