using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Animations;

public sealed class CssMatrixDecompositionTests
{
    private static double[] Translate(double x, double y, double z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];

    private static double[] Scale(double x, double y, double z) => [x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1];

    private static double[] SkewX(double angle) => [1, 0, 0, 0, Math.Tan(angle * Math.PI / 180), 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static double[] Perspective(double d) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -1 / d, 0, 0, 0, 1];

    public static TheoryData<double[]> Matrices3D => new()
    {
        Translate(10, 20, 30),
        CssTransforms.Multiply(Translate(10, -20, 5), CssTransforms.Rotation(1, 2, 3, 70)),
        CssTransforms.Multiply(CssTransforms.Rotation(0, 1, 0, 120), Scale(2, 3, 0.5)),
        CssTransforms.Multiply(CssTransforms.Multiply(Translate(5, 6, 7), SkewX(20)), Scale(-1, 2, 3)),
        CssTransforms.Multiply(Perspective(100), CssTransforms.Rotation(1, 0, 0, 45)),
    };

    [Theory]
    [MemberData(nameof(Matrices3D))]
    public void Decompose3D_RecomposesTheMatrix(double[] matrix)
    {
        var decomposed = CssMatrixDecomposition.Decompose3D(matrix);
        Assert.NotNull(decomposed);
        var recomposed = CssMatrixDecomposition.Recompose(decomposed);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(matrix[i], recomposed[i], 1e-9);
        }
    }

    public static TheoryData<double[]> Matrices2D => new()
    {
        Translate(10, 20, 0),
        CssTransforms.Multiply(Translate(10, -20, 0), CssTransforms.Rotation(0, 0, 1, 70)),
        CssTransforms.Multiply(CssTransforms.Rotation(0, 0, 1, 120), Scale(2, 3, 1)),
        CssTransforms.Multiply(CssTransforms.Multiply(Translate(5, 6, 0), SkewX(20)), Scale(-1, 2, 1)),
    };

    [Theory]
    [MemberData(nameof(Matrices2D))]
    public void Decompose2D_RecomposesTheMatrix(double[] matrix)
    {
        var decomposed = CssMatrixDecomposition.Decompose2D(matrix);
        Assert.NotNull(decomposed);
        var recomposed = CssMatrixDecomposition.Recompose(decomposed);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(matrix[i], recomposed[i], 1e-9);
        }
    }

    [Fact]
    public void Interpolate2D_InterpolatesTheSkewAndTheScales()
    {
        // matrix(1, 0, 0, 7, 0, 0) to matrix(7, 0, 1, 1, 0, 0) is matrix(4, 0, 2, 4, 0, 0) halfway: the scales (1, 7) and (7, 1)
        // and the skews 0 and 1 are interpolated (css/css-transforms/animation/transform-interpolation-005.html)
        var a = CssMatrixDecomposition.Decompose2D([1, 0, 0, 0, 0, 7, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])!;
        var b = CssMatrixDecomposition.Decompose2D([7, 0, 0, 0, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])!;
        var middle = CssMatrixDecomposition.Recompose(CssMatrixDecomposition.Interpolate(a, b, 0.5));
        double[] expected = [4, 0, 0, 0, 2, 4, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(expected[i], middle[i], 1e-9);
        }
    }

    [Fact]
    public void Decompose2D_MatrixWithSkewAndRotation_RecomposesTheMatrix()
    {
        // matrix(1, 0.36, 0, 1, 200, 200): a rotation, a skew and two scales
        double[] matrix = [1, 0.36, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 200, 200, 0, 1];
        var recomposed = CssMatrixDecomposition.Recompose(CssMatrixDecomposition.Decompose2D(matrix)!);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(matrix[i], recomposed[i], 1e-9);
        }
    }

    [Fact]
    public void Slerp_HalfRotation()
    {
        var a = CssMatrixDecomposition.Decompose3D(CssTransforms.Rotation(0, 0, 1, 0))!;
        var b = CssMatrixDecomposition.Decompose3D(CssTransforms.Rotation(0, 0, 1, 90))!;
        var middle = CssMatrixDecomposition.Recompose(CssMatrixDecomposition.Interpolate(a, b, 0.5));
        var expected = CssTransforms.Rotation(0, 0, 1, 45);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(expected[i], middle[i], 1e-9);
        }
    }
}
