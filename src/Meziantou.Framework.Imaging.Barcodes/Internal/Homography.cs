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

        // Gaussian elimination with partial pivoting
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

        var coefficients = new double[8];
        for (var i = 0; i < count; i++)
        {
            coefficients[i] = matrix[i, count] / matrix[i, i];
        }

        return new Homography(coefficients);
    }
}
