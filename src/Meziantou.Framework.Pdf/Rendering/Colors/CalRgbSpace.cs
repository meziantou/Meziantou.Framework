using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

/// <summary>The CIE-based RGB color space (8.6.5.3): the components, raised to their gamma, are mixed by the matrix into XYZ.</summary>
internal sealed class CalRgbSpace(PdfDictionary? dictionary) : RenderColorSpace
{
    private readonly double[] _whitePoint = CieConversion.ReadWhitePoint(dictionary);
    private readonly double[] _gamma = RenderFunction.ReadNumbers(dictionary?.Get<PdfArray>(PdfName.Create("Gamma"))) is { Length: 3 } gamma ? gamma : [1, 1, 1];
    private readonly double[] _matrix = RenderFunction.ReadNumbers(dictionary?.Get<PdfArray>(PdfNames.Matrix)) is { Length: 9 } matrix ? matrix : [1, 0, 0, 0, 1, 0, 0, 0, 1];

    public override int ComponentCount => 3;

    public override RgbColor ToRgb(ReadOnlySpan<double> components)
    {
        var a = Math.Pow(Math.Clamp(components.Length > 0 ? components[0] : 0, 0, 1), _gamma[0]);
        var b = Math.Pow(Math.Clamp(components.Length > 1 ? components[1] : 0, 0, 1), _gamma[1]);
        var c = Math.Pow(Math.Clamp(components.Length > 2 ? components[2] : 0, 0, 1), _gamma[2]);
        var x = (_matrix[0] * a) + (_matrix[3] * b) + (_matrix[6] * c);
        var y = (_matrix[1] * a) + (_matrix[4] * b) + (_matrix[7] * c);
        var z = (_matrix[2] * a) + (_matrix[5] * b) + (_matrix[8] * c);
        return CieConversion.FromXyz(x, y, z, _whitePoint);
    }
}
