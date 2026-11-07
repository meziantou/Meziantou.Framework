using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Unicode;
using FontException = Meziantou.Framework.Fonts.FontException;
using GlyphBuffer = Meziantou.Framework.Fonts.GlyphBuffer;
using LayoutQueries = Meziantou.Framework.Fonts.LayoutQueries;
using SharedGlyphPosition = Meziantou.Framework.Fonts.GlyphPosition;
using SharedTextShaper = Meziantou.Framework.Fonts.TextShaper;
using ShapingFeature = Meziantou.Framework.Fonts.ShapingFeature;

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
    private readonly ConcurrentDictionary<(int CodePoint, bool IsVertical, string? Language), (ushort Glyph, double Offset, double Advance)> _halfWidthAdjustments = new();

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
    /// The adjustment of the glyph of a fullwidth character by the <c>halt</c> feature (<c>vhal</c> for the upright glyphs of
    /// vertical text) along the inline axis, in font units: the change of its offset and of its advance (negative: the blank
    /// half is removed). The character is shaped alone, with and without the feature: the adjustment is empty when the feature
    /// does not change its glyph, and null when the glyph shaped alone is not <paramref name="glyph"/>.
    /// </summary>
    public (double Offset, double Advance)? GetHalfWidthAdjustment(int codePoint, ushort glyph, bool isVertical, string? language)
    {
        var result = _halfWidthAdjustments.GetOrAdd((codePoint, isVertical, language), static (key, font) =>
        {
            try
            {
                Span<char> text = stackalloc char[2];
                var length = new Rune(key.CodePoint).EncodeToUtf16(text);
                var feature = key.IsVertical ? OpenTypeTag.Vhal : OpenTypeTag.Halt;
                var full = font.Shape(text[..length], UnicodeScript.Han, isVertical: key.IsVertical, language: key.Language);
                var half = font.Shape(text[..length], UnicodeScript.Han, isVertical: key.IsVertical, language: key.Language, features: [new ShapingFeature(feature, 1)]);
                if (full.Length != 1 || half.Length != 1 || full.Info[0].Glyph != half.Info[0].Glyph)
                    return default;

                var (before, after) = (full.Positions[0], half.Positions[0]);
                return key.IsVertical
                    ? ((ushort)full.Info[0].Glyph, -(after.YOffset - before.YOffset), -(after.YAdvance - before.YAdvance))
                    : ((ushort)full.Info[0].Glyph, after.XOffset - before.XOffset, after.XAdvance - before.XAdvance);
            }
            catch (FontException)
            {
                return default;
            }
        }, this);

        return result.Glyph == glyph && glyph != 0 ? (result.Offset, result.Advance) : null;
    }

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
    internal GlyphBuffer Shape(ReadOnlySpan<char> text, UnicodeScript script, bool isRightToLeft = false, bool isVertical = false, string? language = null, FontVariationInstance? instance = null, ReadOnlySpan<ShapingFeature> features = default)
    {
        var buffer = new GlyphBuffer(Meziantou.Framework.Fonts.FontResourceLimits.Default);
        SharedTextShaper.AddText(buffer, text);
        SharedTextShaper.Shape(buffer, instance?.Face ?? Face, ShapingWorkspace.GetScript(script), language is null ? [] : Meziantou.Framework.Fonts.OpenTypeLanguage.GetTags(language), isRightToLeft, features, isVertical);
        if (isRightToLeft)
        {
            buffer.Reverse();
        }

        return buffer;
    }
}
