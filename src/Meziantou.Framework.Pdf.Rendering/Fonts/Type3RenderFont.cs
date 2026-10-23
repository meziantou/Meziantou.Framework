using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>A Type 3 font: its glyphs are content streams, which the content renderer runs.</summary>
internal sealed class Type3RenderFont(PdfFontDecoder decoder, Type3Glyphs glyphs) : RenderFont(decoder, glyphs.FontMatrix, _ => null)
{
    public override Type3Glyphs? Type3 { get; } = glyphs;
}
