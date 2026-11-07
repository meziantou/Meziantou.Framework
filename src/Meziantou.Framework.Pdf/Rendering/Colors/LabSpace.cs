using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Functions;

namespace Meziantou.Framework.Pdf.Rendering.Colors;

internal sealed class LabSpace(PdfDictionary? dictionary) : RenderColorSpace
{
    private readonly double[] _whitePoint = CieConversion.ReadWhitePoint(dictionary);
    private readonly double[] _range = RenderFunction.ReadNumbers(dictionary?.Get<PdfArray>(PdfName.Create("Range"))) is { Length: 4 } range ? range : [-100, 100, -100, 100];

    public override int ComponentCount => 3;

    public override double[] GetDefaultDecode(int bitsPerComponent) => [0, 100, _range[0], _range[1], _range[2], _range[3]];

    public override RgbColor ToRgb(ReadOnlySpan<double> components)
    {
        var l = Math.Clamp(components.Length > 0 ? components[0] : 0, 0, 100);
        var a = Math.Clamp(components.Length > 1 ? components[1] : 0, _range[0], _range[1]);
        var b = Math.Clamp(components.Length > 2 ? components[2] : 0, _range[2], _range[3]);
        var m = (l + 16) / 116;
        var lx = m + (a / 500);
        var nz = m - (b / 200);
        return CieConversion.FromXyz(_whitePoint[0] * G(lx), _whitePoint[1] * G(m), _whitePoint[2] * G(nz), _whitePoint);
    }

    private static double G(double x) => x >= 6.0 / 29 ? x * x * x : 108.0 / 841 * (x - (4.0 / 29));
}
