namespace Meziantou.Framework.Imaging.Barcodes.Internal;

/// <summary>
/// A projective transform from the coordinates of the modules of a symbol to the coordinates of the image, from 4 pairs of
/// points (or an affine transform from 3 pairs).
/// </summary>
internal sealed class Homography
{
    // x = (a u + b v + c) / (g u + h v + 1), y = (d u + e v + f) / (g u + h v + 1)
    private readonly double[] _coefficients;

    private Homography(double[] coefficients)
    {
        _coefficients = coefficients;
    }

    public PointD Transform(double u, double v)
    {
        var c = _coefficients;
        var w = (c[6] * u) + (c[7] * v) + 1;
        return new PointD(((c[0] * u) + (c[1] * v) + c[2]) / w, ((c[3] * u) + (c[4] * v) + c[5]) / w);
    }

    /// <summary>Gets the transform that maps the source points to the destination points, or <see langword="null"/> when they are degenerate.</summary>
    public static Homography? Create(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination)
    {
        var count = source.Length is 3 ? 6 : 8;
        var matrix = new double[count, count + 1];
        for (var i = 0; i < source.Length; i++)
        {
            var (u, v) = source[i];
            var (x, y) = destination[i];
            var row = 2 * i;
            matrix[row, 0] = u;
            matrix[row, 1] = v;
            matrix[row, 2] = 1;
            matrix[row + 1, 3] = u;
            matrix[row + 1, 4] = v;
            matrix[row + 1, 5] = 1;
            if (count is 8)
            {
                matrix[row, 6] = -u * x;
                matrix[row, 7] = -v * x;
                matrix[row + 1, 6] = -u * y;
                matrix[row + 1, 7] = -v * y;
            }

            matrix[row, count] = x;
            matrix[row + 1, count] = y;
        }

        if (Solve(matrix, count) is not { } solution)
            return null;

        var coefficients = new double[8];
        solution.CopyTo(coefficients, 0);
        return new Homography(coefficients);
    }

    /// <summary>
    /// Gets the transform that best maps the source points to the destination points (least squares), or
    /// <see langword="null"/> when they are degenerate. Each set of points is normalized (centered, at a mean distance of
    /// √2 from the origin) so that the equations are well conditioned.
    /// </summary>
    public static Homography? Fit(ReadOnlySpan<PointD> source, ReadOnlySpan<PointD> destination)
    {
        if (source.Length < 4 || source.Length != destination.Length)
            return null;

        var (sourceScale, sourceX, sourceY) = GetNormalization(source);
        var (destinationScale, destinationX, destinationY) = GetNormalization(destination);

        // The normal equations of the 2 linear equations of each pair, h33 being 1
        var normal = new double[8, 9];
        Span<double> row = stackalloc double[9];
        for (var i = 0; i < source.Length; i++)
        {
            var u = (source[i].X - sourceX) * sourceScale;
            var v = (source[i].Y - sourceY) * sourceScale;
            var x = (destination[i].X - destinationX) * destinationScale;
            var y = (destination[i].Y - destinationY) * destinationScale;
            for (var equation = 0; equation < 2; equation++)
            {
                row.Clear();
                var target = equation is 0 ? x : y;
                var offset = equation * 3;
                row[offset] = u;
                row[offset + 1] = v;
                row[offset + 2] = 1;
                row[6] = -u * target;
                row[7] = -v * target;
                row[8] = target;
                for (var r = 0; r < 8; r++)
                {
                    for (var c = 0; c < 9; c++)
                    {
                        normal[r, c] += row[r] * row[c];
                    }
                }
            }
        }

        if (Solve(normal, 8) is not { } h)
            return null;

        // H = D^-1 * Hn * S, where S and D are the normalizations: x = (xn / ds) + dx
        double[] hn = [h[0], h[1], h[2], h[3], h[4], h[5], h[6], h[7], 1];
        double[] sMatrix = [sourceScale, 0, -sourceScale * sourceX, 0, sourceScale, -sourceScale * sourceY, 0, 0, 1];
        double[] dInverse = [1 / destinationScale, 0, destinationX, 0, 1 / destinationScale, destinationY, 0, 0, 1];
        var result = Multiply(dInverse, Multiply(hn, sMatrix));
        if (Math.Abs(result[8]) < 1e-12)
            return null;

        return new Homography([result[0] / result[8], result[1] / result[8], result[2] / result[8], result[3] / result[8], result[4] / result[8], result[5] / result[8], result[6] / result[8], result[7] / result[8]]);
    }

    private static (double Scale, double X, double Y) GetNormalization(ReadOnlySpan<PointD> points)
    {
        var x = 0d;
        var y = 0d;
        foreach (var point in points)
        {
            x += point.X;
            y += point.Y;
        }

        x /= points.Length;
        y /= points.Length;
        var distance = 0d;
        foreach (var point in points)
        {
            distance += Math.Sqrt(((point.X - x) * (point.X - x)) + ((point.Y - y) * (point.Y - y)));
        }

        distance /= points.Length;
        return (distance < 1e-12 ? 1 : Math.Sqrt(2) / distance, x, y);
    }

    private static double[] Multiply(double[] a, double[] b)
    {
        var result = new double[9];
        for (var r = 0; r < 3; r++)
        {
            for (var c = 0; c < 3; c++)
            {
                for (var k = 0; k < 3; k++)
                {
                    result[(r * 3) + c] += a[(r * 3) + k] * b[(k * 3) + c];
                }
            }
        }

        return result;
    }

    // Gaussian elimination with partial pivoting of an augmented matrix of `count` equations
    private static double[]? Solve(double[,] matrix, int count)
    {
        for (var column = 0; column < count; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < count; row++)
            {
                if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(matrix[pivot, column]) < 1e-12)
                return null;

            for (var k = 0; k <= count; k++)
            {
                (matrix[column, k], matrix[pivot, k]) = (matrix[pivot, k], matrix[column, k]);
            }

            for (var row = 0; row < count; row++)
            {
                if (row == column)
                    continue;

                var factor = matrix[row, column] / matrix[column, column];
                for (var k = column; k <= count; k++)
                {
                    matrix[row, k] -= factor * matrix[column, k];
                }
            }
        }

        var solution = new double[count];
        for (var i = 0; i < count; i++)
        {
            solution[i] = matrix[i, count] / matrix[i, i];
        }

        return solution;
    }
}
