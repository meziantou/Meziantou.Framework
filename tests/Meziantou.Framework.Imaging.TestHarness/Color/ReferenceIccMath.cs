using Meziantou.Framework.Imaging.TestHarness.Resampling;

namespace Meziantou.Framework.Imaging.TestHarness.Color;

/// <summary>The colorimetry of the ICC profile connection space in <see cref="decimal"/> arithmetic (ICC.1:2022 section 6.3 and annex A).</summary>
internal static class ReferenceIccMath
{
    /// <summary>The PCS illuminant D50 (X, Y, Z).</summary>
    public static readonly decimal[] D50 = [0.9642m, 1m, 0.8249m];

    private const decimal Delta = 6m / 29;

    /// <summary>Clips to [0, 1]. A negative zero (the product of zero and a negative number) becomes a plain zero.</summary>
    public static decimal Clip(decimal value) => value <= 0 ? 0m : value >= 1 ? 1m : value;

    /// <summary>CIELAB from CIEXYZ relative to D50.</summary>
    public static decimal[] XyzToLab(decimal[] xyz)
    {
        var fx = LabFunction(xyz[0] / D50[0]);
        var fy = LabFunction(xyz[1] / D50[1]);
        var fz = LabFunction(xyz[2] / D50[2]);
        return [(116 * fy) - 16, 500 * (fx - fy), 200 * (fy - fz)];
    }

    /// <summary>CIEXYZ relative to D50 from CIELAB.</summary>
    public static decimal[] LabToXyz(decimal[] lab)
    {
        var fy = (lab[0] + 16) / 116;
        var fx = fy + (lab[1] / 500);
        var fz = fy - (lab[2] / 200);
        return [D50[0] * LabInverse(fx), D50[1] * LabInverse(fy), D50[2] * LabInverse(fz)];
    }

    /// <summary>Multiplies a row-major 3x3 matrix by a vector.</summary>
    public static decimal[] Multiply(decimal[] matrix, decimal[] vector)
        =>
        [
            (matrix[0] * vector[0]) + (matrix[1] * vector[1]) + (matrix[2] * vector[2]),
            (matrix[3] * vector[0]) + (matrix[4] * vector[1]) + (matrix[5] * vector[2]),
            (matrix[6] * vector[0]) + (matrix[7] * vector[1]) + (matrix[8] * vector[2]),
        ];

    /// <summary>Inverts a row-major 3x3 matrix by Gauss-Jordan elimination with partial pivoting.</summary>
    public static decimal[] Invert(decimal[] matrix)
    {
        var work = new decimal[3][];
        for (var row = 0; row < 3; row++)
        {
            work[row] = new decimal[6];
            for (var column = 0; column < 3; column++)
            {
                work[row][column] = matrix[(3 * row) + column];
            }

            work[row][3 + row] = 1;
        }

        for (var pivot = 0; pivot < 3; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < 3; row++)
            {
                if (Math.Abs(work[row][pivot]) > Math.Abs(work[best][pivot]))
                {
                    best = row;
                }
            }

            if (work[best][pivot] == 0)
                throw new NotSupportedException("The matrix is singular.");

            (work[pivot], work[best]) = (work[best], work[pivot]);

            var scale = work[pivot][pivot];
            for (var column = 0; column < 6; column++)
            {
                work[pivot][column] /= scale;
            }

            for (var row = 0; row < 3; row++)
            {
                if (row == pivot)
                    continue;

                var factor = work[row][pivot];
                for (var column = 0; column < 6; column++)
                {
                    work[row][column] -= factor * work[pivot][column];
                }
            }
        }

        var result = new decimal[9];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                result[(3 * row) + column] = work[row][3 + column];
            }
        }

        return result;
    }

    private static decimal LabFunction(decimal t)
        => t > Delta * Delta * Delta ? DecimalMath.Exp(DecimalMath.Ln(t) / 3) : (t / (3 * Delta * Delta)) + (4m / 29);

    private static decimal LabInverse(decimal f)
        => f > Delta ? f * f * f : 3 * Delta * Delta * (f - (4m / 29));
}
