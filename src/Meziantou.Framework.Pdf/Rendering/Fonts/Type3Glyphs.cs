using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>The glyph procedures of a Type 3 font (ISO 32000-2, 9.6.4): content streams in glyph space.</summary>
internal sealed class Type3Glyphs(PdfDictionary font, SimpleFontDecoder decoder)
{
    public PdfDictionary? Resources { get; } = font.Get<PdfDictionary>(PdfNames.Resources);

    public PdfMatrix FontMatrix { get; } = Graphics.ContentRenderer.ReadMatrix(font.Get<PdfArray>(PdfNames.FontMatrix));

    /// <summary>Gets the glyph procedure of a code, or <see langword="null"/> when the font has none for it.</summary>
    /// <remarks>A glyph name with the null character (a <c>#00</c> escape) cannot be a key of <c>/CharProcs</c> (7.3.5): it has no procedure.</remarks>
    public PdfStream? GetProcedure(uint code) => decoder.GetGlyphName(code) is { } name && !name.Contains('\0', StringComparison.Ordinal) ? font.Get<PdfDictionary>(PdfNames.CharProcs)?.Get<PdfStream>(PdfName.Create(name)) : null;
}
