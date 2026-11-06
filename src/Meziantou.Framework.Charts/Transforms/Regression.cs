namespace Meziantou.Framework.Charts;

/// <summary>Fits polynomials to points by ordinary least squares, for trend lines (<see cref="RegressionTransform"/>).</summary>
public static class Regression
{
    /// <summary>Fits a polynomial to points. NaN coordinates are ignored.</summary>
    /// <param name="points">The points.</param>
    /// <param name="degree">The degree of the polynomial: 1 for a straight line, 2 for a parabola. From 0 to 10.</param>
    /// <returns>The polynomial.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="degree"/> is not between 0 and 10.</exception>
    /// <exception cref="ArgumentException">There are fewer distinct x values than the degree plus one.</exception>
    public static RegressionResult Fit(IEnumerable<(double X, double Y)> points, int degree = 1)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfNegative(degree);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(degree, 10);
        var data = points.Where(point => !double.IsNaN(point.X) && !double.IsNaN(point.Y)).ToArray();
        if (data.Select(point => point.X).Distinct().Count() <= degree)
            throw new ArgumentException($"A polynomial of degree {degree} needs at least {degree + 1} distinct x values.", nameof(points));

        // The x values are centered and scaled to [-1, 1] before solving, so the normal equations stay well conditioned
        var minimum = data.Min(point => point.X);
        var maximum = data.Max(point => point.X);
        var center = (minimum + maximum) / 2;
        var scale = maximum > minimum ? (maximum - minimum) / 2 : 1;
        var size = degree + 1;
        var matrix = new double[size][];
        for (var row = 0; row < size; row++)
        {
            matrix[row] = new double[size + 1];
        }

        foreach (var (x, y) in data)
        {
            var t = (x - center) / scale;
            var powers = new double[(2 * degree) + 1];
            powers[0] = 1;
            for (var i = 1; i < powers.Length; i++)
            {
                powers[i] = powers[i - 1] * t;
            }

            for (var row = 0; row < size; row++)
            {
                for (var column = 0; column < size; column++)
                {
                    matrix[row][column] += powers[row + column];
                }

                matrix[row][size] += powers[row] * y;
            }
        }

        var scaled = Solve(matrix, size);

        // Back to the powers of x: sum of c_k ((x - center) / scale)^k
        var coefficients = new double[size];
        for (var k = 0; k < size; k++)
        {
            var factor = scaled[k] / Math.Pow(scale, k);
            var binomial = 1.0;
            for (var j = 0; j <= k; j++)
            {
                // C(k, j) x^j (-center)^(k - j)
                coefficients[j] += factor * binomial * Math.Pow(-center, k - j);
                binomial = binomial * (k - j) / (j + 1);
            }
        }

        var mean = data.Average(point => point.Y);
        var total = data.Sum(point => (point.Y - mean) * (point.Y - mean));
        var residual = data.Sum(point => Math.Pow(point.Y - Evaluate(scaled, (point.X - center) / scale), 2));
        var rSquared = total > 0 ? Math.Max(0, 1 - (residual / total)) : 1;
        return new RegressionResult(coefficients, rSquared);
    }

    private static double Evaluate(double[] coefficients, double t)
    {
        var result = 0.0;
        for (var i = coefficients.Length - 1; i >= 0; i--)
        {
            result = (result * t) + coefficients[i];
        }

        return result;
    }

    // Gaussian elimination with partial pivoting of an augmented matrix
    private static double[] Solve(double[][] matrix, int size)
    {
        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < size; row++)
            {
                if (Math.Abs(matrix[row][column]) > Math.Abs(matrix[pivot][column]))
                {
                    pivot = row;
                }
            }

            for (var k = 0; k <= size; k++)
            {
                (matrix[column][k], matrix[pivot][k]) = (matrix[pivot][k], matrix[column][k]);
            }

            for (var row = column + 1; row < size; row++)
            {
                var factor = matrix[row][column] / matrix[column][column];
                for (var k = column; k <= size; k++)
                {
                    matrix[row][k] -= factor * matrix[column][k];
                }
            }
        }

        var solution = new double[size];
        for (var row = size - 1; row >= 0; row--)
        {
            var sum = matrix[row][size];
            for (var k = row + 1; k < size; k++)
            {
                sum -= matrix[row][k] * solution[k];
            }

            solution[row] = sum / matrix[row][row];
        }

        return solution;
    }
}
