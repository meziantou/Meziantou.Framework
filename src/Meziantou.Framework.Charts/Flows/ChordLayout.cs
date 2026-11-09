namespace Meziantou.Framework.Charts;

/// <summary>
/// The chord layout of d3-chord (ISC license), for a square matrix of flows: <c>matrix[i][j]</c> flows from node i to
/// node j. Each node gets an arc whose length is the total of its row, and each pair of nodes gets a chord whose ends
/// are the flows between them.
/// </summary>
internal static class ChordLayout
{
    private const double Tau = 2 * Math.PI;

    /// <summary>Lays the matrix out around a circle, with a gap of <paramref name="padAngle"/> radians after each arc.</summary>
    public static (ChordGroup[] Groups, List<Chord> Chords) Compute(double[][] matrix, double padAngle)
    {
        var n = matrix.Length;
        var groupSums = new double[n];

        // The scale from a value to an angle, so the arcs and the gaps fill the circle. The total is the sum of the values
        // scaled by a power of two, which keeps their ratios exactly, so that it cannot overflow for huge values.
        var exponent = matrix.SelectMany(row => row).DefaultIfEmpty().Max() is > 0 and var max ? -Math.ILogB(max) : 0;
        var k = 0.0;
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            var scaledSum = 0.0;
            for (var j = 0; j < n; j++)
            {
                sum += matrix[i][j];
                scaledSum += Math.ScaleB(matrix[i][j], exponent);
            }

            groupSums[i] = sum;
            k += scaledSum;
        }

        k = Math.Max(0, Tau - (padAngle * n)) / k;
        var dx = k is not 0 && double.IsFinite(k) ? padAngle : Tau / n;
        if (!double.IsFinite(k))
        {
            k = 0;
        }

        var groups = new ChordGroup[n];
        var chords = new Chord?[n * n];
        var x = 0.0;
        for (var i = 0; i < n; i++)
        {
            var x0 = x;
            for (var j = 0; j < n; j++)
            {
                if (matrix[i][j] is 0 && matrix[j][i] is 0)
                    continue;

                var start = x;
                x += Math.ScaleB(matrix[i][j], exponent) * k;
                var subgroup = new ChordSubgroup(i, start, x, matrix[i][j]);
                Chord chord;
                if (i < j)
                {
                    chord = chords[(i * n) + j] ??= new Chord();
                    chord.Source = subgroup;
                }
                else
                {
                    chord = chords[(j * n) + i] ??= new Chord();
                    chord.Target = subgroup;
                    if (i == j)
                    {
                        chord.Source = subgroup;
                    }
                }

                if (chord.Source is { } source && chord.Target is { } target && source.Value < target.Value)
                {
                    chord.Source = target;
                    chord.Target = source;
                }
            }

            groups[i] = new ChordGroup(i, x0, x, groupSums[i]);
            x += dx;
        }

        return (groups, [.. chords.Where(chord => chord is not null).Select(chord => chord!)]);
    }

    /// <summary>The ribbon of a chord, as d3's <c>ribbon</c>: the arcs of its ends joined by quadratic curves through the center.</summary>
    public static PathData Ribbon(double centerX, double centerY, double radius, ChordSubgroup source, ChordSubgroup target)
    {
        var builder = new PathBuilder();
        var (sx, sy) = PolarGeometry.Point(centerX, centerY, radius, source.StartAngle);
        builder.MoveTo(sx, sy);
        AppendArc(builder, centerX, centerY, radius, source.StartAngle, source.EndAngle);
        if (source.StartAngle != target.StartAngle || source.EndAngle != target.EndAngle)
        {
            var (tx, ty) = PolarGeometry.Point(centerX, centerY, radius, target.StartAngle);
            builder.QuadraticTo(centerX, centerY, tx, ty);
            AppendArc(builder, centerX, centerY, radius, target.StartAngle, target.EndAngle);
        }

        builder.QuadraticTo(centerX, centerY, sx, sy);
        return builder.Close().Build();
    }

    private static void AppendArc(PathBuilder builder, double centerX, double centerY, double radius, double startAngle, double endAngle)
    {
        var sweep = endAngle - startAngle;
        if (!(sweep > 0))
            return;

        if (sweep >= Tau - 1e-9)
        {
            // An arc of a full circle is two half arcs
            var (hx, hy) = PolarGeometry.Point(centerX, centerY, radius, startAngle + Math.PI);
            builder.ArcTo(radius, radius, 0, largeArc: false, sweep: true, hx, hy);
        }

        var (x, y) = PolarGeometry.Point(centerX, centerY, radius, endAngle);
        builder.ArcTo(radius, radius, 0, sweep > Math.PI && sweep < Tau - 1e-9, sweep: true, x, y);
    }
}
