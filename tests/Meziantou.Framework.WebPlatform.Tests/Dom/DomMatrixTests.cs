using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The DOMMatrix string constructor and setMatrixValue() (https://drafts.fxtf.org/geometry/#parse-a-string-into-an-abstract-matrix).</summary>
public sealed class DomMatrixTests
{
    [Theory]
    [InlineData("", "matrix(1, 0, 0, 1, 0, 0)", true)]
    [InlineData(" NONE /**/", "matrix(1, 0, 0, 1, 0, 0)", true)]
    [InlineData("scale(2) translateX(5px) translateY(5px)", "matrix(2, 0, 0, 2, 10, 10)", true)]
    [InlineData("translate(1in, calc(2 * 2.5px))", "matrix(1, 0, 0, 1, 96, 5)", true)]
    [InlineData("scale(50%, 2) rotate(0)", "matrix(0.5, 0, 0, 2, 0, 0)", true)]
    [InlineData("translateZ(0)", "matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1)", false)]
    [InlineData("perspective(10px)", "matrix3d(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -0.1, 0, 0, 0, 1)", false)]
    public void Constructor_ParsesTheTransformList(string transformList, string expected, bool is2D)
    {
        var matrix = new DomMatrix(transformList);
        Assert.Equal(expected, matrix.ToString());
        Assert.Equal(is2D, matrix.Is2D);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("translateX(5em)")]
    [InlineData("translateX(5%)")]
    [InlineData("scale(2 2)")]
    [InlineData("scale(2), translateX(5px)")]
    [InlineData("rotate(5)")]
    [InlineData("inherit")]
    public void Constructor_InvalidTransformList_ThrowsASyntaxError(string transformList)
    {
        var exception = Assert.Throws<DomException>(() => new DomMatrixReadOnly(transformList));
        Assert.Equal("SyntaxError", exception.Name);
    }

    [Fact]
    public void SetMatrixValue_InvalidValue_LeavesTheMatrixUnchanged()
    {
        var matrix = new DomMatrix("translate(1px, 2px)");
        Assert.Throws<DomException>(() => matrix.SetMatrixValue("translate(1em)"));
        Assert.Equal("matrix(1, 0, 0, 1, 1, 2)", matrix.ToString());

        matrix.SetMatrixValue("rotateX(0deg)");
        Assert.False(matrix.Is2D);
    }
}
