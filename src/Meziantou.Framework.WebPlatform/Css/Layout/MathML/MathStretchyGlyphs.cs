using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A glyph drawn by the math layout: its origin (the left end of its baseline) in CSS px, relative to a reference point, y down.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MathPlacedGlyph(ushort Glyph, double X, double Y);

/// <summary>
/// A glyph shaped by the math layout instead of the text layout (a stretched operator, a size variant, a glyph assembly, a
/// radical symbol): the glyphs and their origins relative to the inline-start edge and the alphabetic baseline of the
/// shaped glyph, and its box metrics (https://w3c.github.io/mathml-core/#algorithms-for-glyph-stretching).
/// </summary>
internal sealed record MathShapedGlyph(MatchedFont Font, double EmSize, MathPlacedGlyph[] Glyphs, double Width, double Ascent, double Descent, double ItalicCorrection);

/// <summary>
/// The glyphs of the math layout (https://w3c.github.io/mathml-core/#size-variants-for-operators-mathvariants): the size variants
/// and the glyph assemblies of the MathVariants table of the OpenType MATH table.
/// </summary>
internal static class MathStretchyGlyphs
{
    /// <summary>
    /// "Get a glyph corresponding to a character c given a directionality dir" (https://w3c.github.io/mathml-core/#dfn-get-a-glyph-corresponding-to-a-character):
    /// the glyph of the first available font; in rtl, its <c>rtlm</c> variant (the single substitutions of the feature), else the
    /// glyph of the Bidi_Mirroring_Glyph of a Bidi_Mirrored character (a failure when it has none).
    /// </summary>
    public static bool TryGetGlyph(MatchedFont font, int codePoint, bool rightToLeft, out ushort glyph)
    {
        glyph = font.GetGlyph(codePoint);
        if (glyph == 0)
            return false;

        if (rightToLeft)
        {
            var mirrored = font.Font.GetSingleSubstitution(glyph, OpenTypeTag.RightToLeftMirroredForms);
            if (mirrored != glyph)
            {
                glyph = mirrored;
                return true;
            }

            if (UnicodeProperties.IsBidiMirrored(codePoint))
            {
                var mirroredCodePoint = CharacterProperties.GetBidiMirroringGlyph(codePoint);
                glyph = mirroredCodePoint >= 0 ? font.GetGlyph(mirroredCodePoint) : (ushort)0;
                return glyph != 0;
            }
        }

        return true;
    }

    /// <summary>Returns the advance width and the ink ascent and descent (from the bounding box) of a glyph, in font units.</summary>
    private static (double Advance, double Ascent, double Descent) GetGlyphMetrics(OpenTypeFont font, ushort glyph)
    {
        var advance = font.GetAdvanceWidth(glyph);
        return font.TryGetGlyphBounds(glyph, out var bounds) ? (advance, bounds.YMax, -bounds.YMin) : (advance, 0, 0);
    }

    /// <summary>Shapes one glyph without stretching ("normal shaping and bounding box for that glyph").</summary>
    public static MathShapedGlyph ShapeGlyph(MatchedFont font, double emSize, ushort glyph)
    {
        var scale = emSize / font.Font.UnitsPerEm;
        var (advance, ascent, descent) = GetGlyphMetrics(font.Font, glyph);
        var italicCorrection = font.Font.MathTable?.GetItalicsCorrection(glyph) ?? 0;
        return new MathShapedGlyph(font, emSize, [new MathPlacedGlyph(glyph, 0, 0)], advance * scale, ascent * scale, descent * scale, italicCorrection * scale);
    }

    /// <summary>
    /// "The preferred inline size of a glyph stretched along the block axis" (https://w3c.github.io/mathml-core/#dfn-preferred-inline-size-of-a-glyph-stretched-along-the-block-axis):
    /// the largest advance width of the glyph, of its vertical size variants and of its vertical assembly, in CSS px.
    /// </summary>
    public static double GetPreferredInlineSize(MatchedFont font, double emSize, ushort glyph)
    {
        var openType = font.Font;
        double size = openType.GetAdvanceWidth(glyph);
        if (openType.MathTable?.GetConstruction(glyph, vertical: true) is { } construction)
        {
            foreach (var variant in construction.Variants)
            {
                size = Math.Max(size, openType.GetAdvanceWidth((ushort)variant.Glyph));
            }

            if (IsValidAssembly(construction, openType.MathTable.MinConnectorOverlap))
            {
                foreach (var part in construction.AssemblyParts)
                {
                    size = Math.Max(size, openType.GetAdvanceWidth((ushort)part.Glyph));
                }
            }
        }

        return size * emSize / openType.UnitsPerEm;
    }

    /// <summary>Whether the font has a construction to stretch the glyph in a direction.</summary>
    public static bool CanStretch(MatchedFont font, ushort glyph, bool vertical) => font.Font.MathTable?.GetConstruction(glyph, vertical) is not null;

    /// <summary>
    /// "The algorithm to shape a stretchy glyph to inline (respectively block) dimension T" (https://w3c.github.io/mathml-core/#dfn-shape-a-stretchy-glyph):
    /// the glyph itself, a size variant or a glyph assembly; null when the font has no construction for the glyph in that direction.
    /// </summary>
    /// <param name="font">The first available font.</param>
    /// <param name="emSize">The used em size of the font.</param>
    /// <param name="glyph">The glyph.</param>
    /// <param name="vertical">Whether the glyph is stretched along the block axis (vertically).</param>
    /// <param name="target">The target size T, in CSS px.</param>
    public static MathShapedGlyph? Shape(MatchedFont font, double emSize, ushort glyph, bool vertical, double target)
    {
        var openType = font.Font;
        var math = openType.MathTable;
        if (math?.GetConstruction(glyph, vertical) is not { } construction)
            return null;

        var scale = emSize / openType.UnitsPerEm;
        var targetUnits = target / scale;

        // The glyph itself, then the size variants
        var (advance, ascent, descent) = GetGlyphMetrics(openType, glyph);
        if ((vertical ? ascent + descent : advance) >= targetUnits)
            return ShapeGlyph(font, emSize, glyph);

        var lastTried = glyph;
        foreach (var variant in construction.Variants)
        {
            lastTried = (ushort)variant.Glyph;
            if (variant.AdvanceMeasurement >= targetUnits)
                return ShapeGlyph(font, emSize, (ushort)variant.Glyph);
        }

        var overlap = math.MinConnectorOverlap;
        if (!IsValidAssembly(construction, overlap))
            return ShapeGlyph(font, emSize, lastTried);

        return ShapeAssembly(font, emSize, construction, overlap, targetUnits, vertical);
    }

    /// <summary>
    /// https://w3c.github.io/mathml-core/#the-glyphassembly-table: the assembly is valid when it has extenders and when the extenders
    /// make it grow. The connectors shorter than the minimum overlap do not invalidate it (as in the browsers: WPT
    /// mathml/presentation-markup/operators/mo-minsize-maxsize-001.html uses a part without start connector): they limit the overlap.
    /// </summary>
    private static bool IsValidAssembly(MathGlyphConstruction construction, int minConnectorOverlap)
    {
        var parts = construction.AssemblyParts;
        if (parts.Count == 0)
            return false;

        var extenderCount = 0;
        var extenderSize = 0;
        foreach (var part in parts)
        {
            if (part.IsExtender)
            {
                extenderCount++;
                extenderSize += part.FullAdvance;
            }

        }

        return extenderCount > 0 && extenderSize - (minConnectorOverlap * extenderCount) > 0;
    }

    /// <summary>Builds the glyph assembly of a target size (font units): the extenders repeated r_min times with the overlap o_max.</summary>
    private static MathShapedGlyph ShapeAssembly(MatchedFont font, double emSize, MathGlyphConstruction construction, int minOverlap, double target, bool vertical)
    {
        var openType = font.Font;
        var parts = construction.AssemblyParts;
        var extenderCount = 0;
        var nonExtenderCount = 0;
        double extenderSize = 0;
        double nonExtenderSize = 0;
        foreach (var part in parts)
        {
            if (part.IsExtender)
            {
                extenderCount++;
                extenderSize += part.FullAdvance;
            }
            else
            {
                nonExtenderCount++;
                nonExtenderSize += part.FullAdvance;
            }
        }

        var extenderNonOverlapping = extenderSize - (minOverlap * extenderCount);
        var repetitions = (int)Math.Max(0, Math.Ceiling((target - nonExtenderSize + (minOverlap * (nonExtenderCount - 1))) / extenderNonOverlapping));
        var glyphCount = nonExtenderCount + (repetitions * extenderCount);
        double overlap = 0;
        if (glyphCount > 1)
        {
            // o_max: the theoretical overlap, limited by the connectors that join two glyphs (the start connector of the first part
            // and the end connector of the last part join nothing, unless they are repeated extenders)
            overlap = ((nonExtenderSize + (repetitions * extenderSize)) - target) / (glyphCount - 1);
            for (var i = 0; i < parts.Count; i++)
            {
                if (i != 0 || parts[i].IsExtender)
                {
                    overlap = Math.Min(overlap, parts[i].StartConnectorLength);
                }

                if (i != parts.Count - 1 || parts[i].IsExtender)
                {
                    overlap = Math.Min(overlap, parts[i].EndConnectorLength);
                }
            }

            overlap = Math.Max(0, overlap);
        }

        var size = nonExtenderSize + (repetitions * extenderSize) - (overlap * (glyphCount - 1));
        var scale = emSize / openType.UnitsPerEm;
        var glyphs = new MathPlacedGlyph[Math.Max(0, glyphCount)];
        var index = 0;
        double position = 0;
        double maxWidth = 0;
        double maxAscent = 0;
        double maxDescent = 0;
        foreach (var part in parts)
        {
            var (advance, ascent, descent) = GetGlyphMetrics(openType, (ushort)part.Glyph);
            maxWidth = Math.Max(maxWidth, advance);
            maxAscent = Math.Max(maxAscent, ascent);
            maxDescent = Math.Max(maxDescent, descent);
            var count = part.IsExtender ? repetitions : 1;
            for (var r = 0; r < count && index < glyphs.Length; r++)
            {
                // Vertical: the parts are drawn from the bottom (the alphabetic baseline of the assembly) upwards, the bottom of
                // their bounding box at the position; horizontal: their origins from the left
                glyphs[index++] = vertical ? new MathPlacedGlyph((ushort)part.Glyph, 0, (-position - descent) * scale) : new MathPlacedGlyph((ushort)part.Glyph, position * scale, 0);
                position += part.FullAdvance - overlap;
            }
        }

        var italicCorrection = construction.AssemblyItalicsCorrection * scale;
        return vertical
            ? new MathShapedGlyph(font, emSize, glyphs, maxWidth * scale, size * scale, 0, italicCorrection)
            : new MathShapedGlyph(font, emSize, glyphs, size * scale, maxAscent * scale, maxDescent * scale, italicCorrection);
    }
}
