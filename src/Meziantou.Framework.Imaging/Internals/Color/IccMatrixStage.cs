namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies a 3x3 matrix (row-major) followed by an offset to the first three components.</summary>
internal sealed class IccMatrixStage : IccStage
{
    private readonly double _m00;
    private readonly double _m01;
    private readonly double _m02;
    private readonly double _m10;
    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m20;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _o0;
    private readonly double _o1;
    private readonly double _o2;

    public IccMatrixStage(ReadOnlySpan<double> matrix, ReadOnlySpan<double> offset = default)
    {
        (_m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22) = (matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5], matrix[6], matrix[7], matrix[8]);
        if (!offset.IsEmpty)
        {
            (_o0, _o1, _o2) = (offset[0], offset[1], offset[2]);
        }
    }

    /// <summary>Creates the stage scaling each component then adding an offset.</summary>
    public static IccMatrixStage CreateScale(double s0, double s1, double s2, double o0 = 0, double o1 = 0, double o2 = 0)
        => new([s0, 0, 0, 0, s1, 0, 0, 0, s2], [o0, o1, o2]);

    /// <summary>Creates the single stage equivalent to this stage followed by <paramref name="next"/>.</summary>
    public IccMatrixStage Then(IccMatrixStage next)
    {
        ReadOnlySpan<double> a = [_m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22];
        ReadOnlySpan<double> b = [next._m00, next._m01, next._m02, next._m10, next._m11, next._m12, next._m20, next._m21, next._m22];
        Span<double> matrix = stackalloc double[9];
        Span<double> offset = stackalloc double[3];
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                matrix[(3 * row) + column] = (b[3 * row] * a[column]) + (b[(3 * row) + 1] * a[3 + column]) + (b[(3 * row) + 2] * a[6 + column]);
            }

            offset[row] = (b[3 * row] * _o0) + (b[(3 * row) + 1] * _o1) + (b[(3 * row) + 2] * _o2);
        }

        offset[0] += next._o0;
        offset[1] += next._o1;
        offset[2] += next._o2;
        return new IccMatrixStage(matrix, offset);
    }

    public override void Apply(Span<double> values)
    {
        var (x, y, z) = (values[0], values[1], values[2]);
        values[0] = (_m00 * x) + (_m01 * y) + (_m02 * z) + _o0;
        values[1] = (_m10 * x) + (_m11 * y) + (_m12 * z) + _o1;
        values[2] = (_m20 * x) + (_m21 * y) + (_m22 * z) + _o2;
    }

    /// <summary>Inverts a 3x3 matrix.</summary>
    /// <returns><see langword="false"/> if the matrix is singular or not finite.</returns>
    public static bool TryInvert(ReadOnlySpan<double> m, Span<double> inverse)
    {
        var c00 = (m[4] * m[8]) - (m[5] * m[7]);
        var c01 = (m[5] * m[6]) - (m[3] * m[8]);
        var c02 = (m[3] * m[7]) - (m[4] * m[6]);
        var determinant = (m[0] * c00) + (m[1] * c01) + (m[2] * c02);
        if (!double.IsFinite(determinant) || determinant == 0)
            return false;

        inverse[0] = c00 / determinant;
        inverse[1] = ((m[2] * m[7]) - (m[1] * m[8])) / determinant;
        inverse[2] = ((m[1] * m[5]) - (m[2] * m[4])) / determinant;
        inverse[3] = c01 / determinant;
        inverse[4] = ((m[0] * m[8]) - (m[2] * m[6])) / determinant;
        inverse[5] = ((m[2] * m[3]) - (m[0] * m[5])) / determinant;
        inverse[6] = c02 / determinant;
        inverse[7] = ((m[1] * m[6]) - (m[0] * m[7])) / determinant;
        inverse[8] = ((m[0] * m[4]) - (m[1] * m[3])) / determinant;
        foreach (var value in inverse[..9])
        {
            if (!double.IsFinite(value))
                return false;
        }

        return true;
    }
}
