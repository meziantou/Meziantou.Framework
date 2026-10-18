using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Unicode;
using FontException = Meziantou.Framework.Fonts.FontException;
using GlyphBuffer = Meziantou.Framework.Fonts.GlyphBuffer;
using LayoutQueries = Meziantou.Framework.Fonts.LayoutQueries;
using SharedGlyphPosition = Meziantou.Framework.Fonts.GlyphPosition;
using SharedTextShaper = Meziantou.Framework.Fonts.TextShaper;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>The adjustment of the position and of the advance of a glyph, in font units (https://learn.microsoft.com/typography/opentype/spec/gpos#value-record).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GlyphAdjustment(double XPlacement, double YPlacement, double XAdvance, double YAdvance)
{
    public bool IsEmpty => XPlacement == 0 && YPlacement == 0 && XAdvance == 0 && YAdvance == 0;
}

/// <summary>The adjustments of a pair of glyphs (kerning): the first glyph, then the second one.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PairAdjustment(GlyphAdjustment First, GlyphAdjustment Second)
{
    public bool IsEmpty => First.IsEmpty && Second.IsEmpty;
}

/// <summary>
/// What the layout and the MathML layout ask of the OpenType layout tables of the font (GSUB and GPOS,
/// https://learn.microsoft.com/typography/opentype/spec/ttochap1) outside of the shaping of a text. A malformed table
/// answers as a font without the table.
/// </summary>
internal sealed partial class OpenTypeFont
{
    private readonly ConcurrentDictionary<int, bool> _verticalAlternates = new();
    private readonly ConcurrentDictionary<uint, bool> _positioningFeatures = new();

    /// <summary>
    /// Whether the <c>vert</c> feature of the font (of any script) substitutes a glyph by a single substitution: a character
    /// whose Vertical_Orientation is Tr is upright with its vertical alternate when the font has one, else rotated
    /// (https://www.unicode.org/reports/tr50/#vo).
    /// </summary>
    public bool HasVerticalAlternate(int glyph)
    {
        return _verticalAlternates.GetOrAdd(glyph, static (glyph, face) =>
        {
            try
            {
                return LayoutQueries.HasVerticalAlternate(face, glyph);
            }
            catch (FontException)
            {
                return false;
            }
        }, Face);
    }

    /// <summary>Whether the GPOS table of the font has a feature, in any script and language system.</summary>
    public bool HasPositioningFeature(uint feature) => _positioningFeatures.GetOrAdd(feature, static (feature, face) => LayoutQueries.HasFeature(face, isPositioning: true, feature), Face);

    /// <summary>
    /// The kerning of a pair of glyphs in font units (the adjustment of the advance of the first glyph and of the position of
    /// the second one): the lookups of the <c>kern</c> feature of the GPOS table when the font has it, else the kern table.
    /// </summary>
    public double GetKerning(ushort first, ushort second, FontVariationInstance? instance = null, uint script = OpenTypeTag.DefaultScript)
    {
        var adjustment = GetPairAdjustment(first, second, OpenTypeTag.Kern, script, instance);
        return adjustment.First.XAdvance + adjustment.Second.XPlacement;
    }

    /// <summary>
    /// The adjustments of two consecutive glyphs by the lookups of a feature of the GPOS table for a script (the default
    /// language system of the script, else of <c>DFLT</c>, <c>dflt</c> or <c>latn</c>). For the <c>kern</c> feature, the kern
    /// table is used when the GPOS table does not have the feature. The y placements are upwards.
    /// </summary>
    public PairAdjustment GetPairAdjustment(ushort first, ushort second, uint feature, uint script = OpenTypeTag.DefaultScript, FontVariationInstance? instance = null)
    {
        try
        {
            var (firstPosition, secondPosition) = LayoutQueries.GetPairAdjustment(instance?.Face ?? Face, feature, first, second, script);
            return new PairAdjustment(ToAdjustment(firstPosition), ToAdjustment(secondPosition));
        }
        catch (FontException)
        {
            return default;
        }

        static GlyphAdjustment ToAdjustment(SharedGlyphPosition position) => new(position.XOffset, position.YOffset, position.XAdvance, position.YAdvance);
    }

    /// <summary>
    /// Returns the glyph that the lookups of a feature of the GSUB table substitute for a glyph (e.g. <c>rtlm</c>, the
    /// right-to-left mirrored forms), or the glyph itself when the feature does not substitute it by one glyph.
    /// </summary>
    public ushort GetSingleSubstitution(ushort glyph, uint feature, uint script = OpenTypeTag.DefaultScript)
    {
        try
        {
            return (ushort)LayoutQueries.GetSingleSubstitution(Face, feature, glyph, script);
        }
        catch (FontException)
        {
            return glyph;
        }
    }

    /// <summary>
    /// Shapes a text with the font alone, without the font fallback nor the script itemization (the fuzzing and the tests of
    /// the shaping): the glyphs, in the order of the text, and their positions in font units.
    /// </summary>
    /// <exception cref="FontException">The layout tables of the font are malformed.</exception>
    internal GlyphBuffer Shape(ReadOnlySpan<char> text, UnicodeScript script, bool isRightToLeft = false, bool isVertical = false, string? language = null, FontVariationInstance? instance = null)
    {
        var buffer = new GlyphBuffer(Meziantou.Framework.Fonts.FontResourceLimits.Default);
        SharedTextShaper.AddText(buffer, text);
        SharedTextShaper.Shape(buffer, instance?.Face ?? Face, ShapingWorkspace.GetScript(script), language is null ? [] : Meziantou.Framework.Fonts.OpenTypeLanguage.GetTags(language), isRightToLeft, [], isVertical);
        if (isRightToLeft)
        {
            buffer.Reverse();
        }

        return buffer;
    }
}
