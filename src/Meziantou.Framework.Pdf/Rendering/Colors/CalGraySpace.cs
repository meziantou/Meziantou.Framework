using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

/// <summary>
/// The CIE-based gray color space (8.6.5.2): the component A, raised to the gamma, is the luminance Y, and X and Z are
/// those of the white point at that luminance.
/// </summary>
internal sealed class CalGraySpace(PdfDictionary? dictionary) : RenderColorSpace
{
    private readonly double[] _whitePoint = CieConversion.ReadWhitePoint(dictionary);
    private readonly double _gamma = dictionary?.GetResolved(PdfName.Create("Gamma")) is PdfNumber { DoubleValue: > 0 and var gamma } && double.IsFinite(gamma) ? gamma : 1;

    public override int ComponentCount => 1;

    public override RgbColor ToRgb(ReadOnlySpan<double> components)
    {
        var a = components.Length > 0 && double.IsFinite(components[0]) ? Math.Clamp(components[0], 0, 1) : 0;
        var value = Math.Pow(a, _gamma);
        return CieConversion.FromXyz(_whitePoint[0] * value, _whitePoint[1] * value, _whitePoint[2] * value, _whitePoint);
    }
}
