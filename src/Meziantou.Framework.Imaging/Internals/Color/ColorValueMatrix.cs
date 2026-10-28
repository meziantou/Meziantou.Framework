using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A 3x3 matrix of the color value conversions, stored in nine fields so that a static instance costs nothing to read
/// (a static span of doubles allocates in a build without optimizations).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ColorValueMatrix
{
    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m13;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _m23;
    private readonly double _m31;
    private readonly double _m32;
    private readonly double _m33;

    public ColorValueMatrix(double m11, double m12, double m13, double m21, double m22, double m23, double m31, double m32, double m33)
    {
        _m11 = m11;
        _m12 = m12;
        _m13 = m13;
        _m21 = m21;
        _m22 = m22;
        _m23 = m23;
        _m31 = m31;
        _m32 = m32;
        _m33 = m33;
    }

    /// <summary>Creates a matrix from nine values, row by row.</summary>
    public static ColorValueMatrix FromRows(ReadOnlySpan<double> values) => new(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]);

    /// <summary>Copies the nine values, row by row.</summary>
    public void CopyTo(Span<double> values)
    {
        values[0] = _m11;
        values[1] = _m12;
        values[2] = _m13;
        values[3] = _m21;
        values[4] = _m22;
        values[5] = _m23;
        values[6] = _m31;
        values[7] = _m32;
        values[8] = _m33;
    }

    /// <summary>Multiplies a column vector. Each component is the sum of its three products, from left to right.</summary>
    public (double X, double Y, double Z) Transform(double x, double y, double z)
        => ((_m11 * x) + (_m12 * y) + (_m13 * z), (_m21 * x) + (_m22 * y) + (_m23 * z), (_m31 * x) + (_m32 * y) + (_m33 * z));

    /// <summary>Computes <c>this * right</c>: the matrix that applies <paramref name="right"/>, then this one.</summary>
    public ColorValueMatrix Multiply(ColorValueMatrix right)
    {
        Span<double> left = stackalloc double[9];
        Span<double> other = stackalloc double[9];
        Span<double> result = stackalloc double[9];
        CopyTo(left);
        right.CopyTo(other);
        ChromaticityMath.Multiply(left, other, result);
        return FromRows(result);
    }

    /// <summary>Computes the inverse.</summary>
    /// <exception cref="InvalidOperationException">The matrix has no inverse.</exception>
    public ColorValueMatrix Invert()
    {
        Span<double> values = stackalloc double[9];
        Span<double> inverse = stackalloc double[9];
        CopyTo(values);
        if (!ChromaticityMath.TryInvert(values, inverse))
            throw new InvalidOperationException("The matrix has no inverse.");

        return FromRows(inverse);
    }
}
