namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>
/// The glyphs of a shaped text from left to right, the adjustment after the last one, the text of the glyphs that show
/// one cluster on their own, and whether the text needs a replacement text to be extracted (several glyphs for a
/// cluster, right-to-left runs).
/// </summary>
internal sealed record ShapedText(ShapedGlyph[] Glyphs, double TrailingAdjustment, (int Glyph, string Text)[] Mappings, bool NeedsActualText);
