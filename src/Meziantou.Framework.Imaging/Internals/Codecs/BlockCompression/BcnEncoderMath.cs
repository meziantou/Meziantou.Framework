using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The arithmetic shared by the BCn block encoders (<see cref="S3tcBlockEncoder"/>, <see cref="Bc7BlockEncoder"/>): the
/// principal axis of a set of colors, along which a block encoder places the two endpoints of a line of colors.
/// </summary>
/// <remarks>
/// Only scalar double arithmetic and <see cref="Math.Sqrt(double)"/> are used, in a fixed order: the results are the same
/// on every platform, so the encoded bytes are too.
/// </remarks>
internal static class BcnEncoderMath
{
    /// <summary>The number of power iterations: enough for the axis of 16 colors, whose eigenvalues are well separated or irrelevant.</summary>
    private const int DefaultIterations = 8;

    /// <summary>
    /// Computes the principal axis (the eigenvector of the largest eigenvalue) of a symmetric scatter matrix by power
    /// iteration.
    /// </summary>
    /// <param name="scatter">The scatter matrix, <paramref name="dimensions"/> × <paramref name="dimensions"/>, row-major.</param>
    /// <param name="dimensions">The number of channels, 1 to 4.</param>
    /// <param name="axis">The unit axis; zero when the matrix is zero (every color is the same).</param>
    /// <param name="iterations">The number of power iterations.</param>
    /// <returns>The largest eigenvalue (the spread of the colors along the axis), or a lower bound of it after few iterations.</returns>
    public static double PrincipalAxis(ReadOnlySpan<double> scatter, int dimensions, Span<double> axis, int iterations = DefaultIterations)
    {
        // The matrix padded to 4 × 4 with zeros (whose axis components stay 0): the iterations run unrolled
        var m00 = scatter[0];
        double m01 = 0, m02 = 0, m03 = 0, m11 = 0, m12 = 0, m13 = 0, m22 = 0, m23 = 0, m33 = 0;
        if (dimensions > 1)
        {
            m01 = scatter[1];
            m11 = scatter[dimensions + 1];
        }

        if (dimensions > 2)
        {
            m02 = scatter[2];
            m12 = scatter[dimensions + 2];
            m22 = scatter[(2 * dimensions) + 2];
        }

        if (dimensions > 3)
        {
            m03 = scatter[3];
            m13 = scatter[dimensions + 3];
            m23 = scatter[(2 * dimensions) + 3];
            m33 = scatter[(3 * dimensions) + 3];
        }

        var eigenvalue = PrincipalAxis(new SymmetricMatrix4(m00, m01, m02, m03, m11, m12, m13, m22, m23, m33), iterations, out var x, out var y, out var z, out var w);
        axis[0] = x;
        if (dimensions > 1)
        {
            axis[1] = y;
        }

        if (dimensions > 2)
        {
            axis[2] = z;
        }

        if (dimensions > 3)
        {
            axis[3] = w;
        }

        return eigenvalue;
    }

    /// <summary>
    /// Computes the principal axis of a symmetric 4 × 4 scatter matrix by power iteration (see
    /// <see cref="PrincipalAxis(ReadOnlySpan{double}, int, Span{double}, int)"/>).
    /// </summary>
    /// <returns>The largest eigenvalue, or a lower bound of it after few iterations; 0 for a zero matrix (and a zero axis).</returns>
    public static double PrincipalAxis(in SymmetricMatrix4 matrix, int iterations, out double x, out double y, out double z, out double w)
    {
        var (m00, m01, m02, m03, m11, m12, m13, m22, m23, m33) = matrix;

        // Start from the column of the channel that varies the most, which already leans toward the principal axis; a fixed
        // start vector such as (1, 1, 1) is orthogonal to common axes (for example red against green)
        var largestVariance = Math.Max(Math.Max(m00, m11), Math.Max(m22, m33));
        if (largestVariance <= 0)
        {
            (x, y, z, w) = (0, 0, 0, 0);
            return 0;
        }

        if (m00 == largestVariance)
        {
            (x, y, z, w) = (m00, m01, m02, m03);
        }
        else if (m11 == largestVariance)
        {
            (x, y, z, w) = (m01, m11, m12, m13);
        }
        else if (m22 == largestVariance)
        {
            (x, y, z, w) = (m02, m12, m22, m23);
        }
        else
        {
            (x, y, z, w) = (m03, m13, m23, m33);
        }

        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var nx = (m00 * x) + (m01 * y) + (m02 * z) + (m03 * w);
            var ny = (m01 * x) + (m11 * y) + (m12 * z) + (m13 * w);
            var nz = (m02 * x) + (m12 * y) + (m22 * z) + (m23 * w);
            var nw = (m03 * x) + (m13 * y) + (m23 * z) + (m33 * w);

            // Scaling by the largest component keeps the values bounded without a square root per iteration
            var largest = Math.Max(Math.Max(Math.Abs(nx), Math.Abs(ny)), Math.Max(Math.Abs(nz), Math.Abs(nw)));
            if (largest <= 0)
                break;

            var scale = 1 / largest;
            x = nx * scale;
            y = ny * scale;
            z = nz * scale;
            w = nw * scale;
        }

        var length = (x * x) + (y * y) + (z * z) + (w * w);
        if (length <= 0)
        {
            (x, y, z, w) = (0, 0, 0, 0);
            return 0;
        }

        var inverseLength = 1 / Math.Sqrt(length);
        x *= inverseLength;
        y *= inverseLength;
        z *= inverseLength;
        w *= inverseLength;

        // The Rayleigh quotient of the unit axis
        return (x * ((m00 * x) + (m01 * y) + (m02 * z) + (m03 * w)))
            + (y * ((m01 * x) + (m11 * y) + (m12 * z) + (m13 * w)))
            + (z * ((m02 * x) + (m12 * y) + (m22 * z) + (m23 * w)))
            + (w * ((m03 * x) + (m13 * y) + (m23 * z) + (m33 * w)));
    }

    /// <summary>A symmetric 4 × 4 matrix: its diagonal and the entries above it.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct SymmetricMatrix4(double M00, double M01, double M02, double M03, double M11, double M12, double M13, double M22, double M23, double M33);
}
