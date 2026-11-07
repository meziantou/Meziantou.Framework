using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>The part of the graphics state the text extraction needs: the transformation matrix and the text state (ISO 32000-2, 9.3).</summary>
[StructLayout(LayoutKind.Auto)]
internal struct TextGraphicsState()
{
    public PdfMatrix Ctm { get; set; } = PdfMatrix.Identity;

    public PdfMatrix TextMatrix { get; set; } = PdfMatrix.Identity;

    public PdfMatrix LineMatrix { get; set; } = PdfMatrix.Identity;

    public double CharacterSpacing { get; set; }

    public double WordSpacing { get; set; }

    public double HorizontalScaling { get; set; } = 1;

    public double Leading { get; set; }

    public double Rise { get; set; }

    public double FontSize { get; set; } = 1;

    public PdfFontDecoder? Font { get; set; }
}
