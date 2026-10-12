using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Consecutive glyphs of a layout that have the same font, size, paint and synthesis.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GlyphRun(int Font, double Size, int Brush, int Start, int Count, int Palette = 0, FontSynthesis Synthesis = FontSynthesis.None)
{
    /// <summary>What a synthetic oblique adds to the horizontal position of a point for each unit of its height above the baseline.</summary>
    public const double ObliqueShear = 0.25;

    /// <summary>The width of the stroke a synthetic bold fills a glyph with, and what it adds to an advance, for a size of 1.</summary>
    public const double BoldWidth = 1.0 / 24;

    /// <summary>The miter limit of the stroke of a synthetic bold: a corner sharper than 60 degrees is cut.</summary>
    public const double BoldMiterLimit = 2;

    /// <summary>Gets the shear of the glyphs of the run: 0, or <see cref="ObliqueShear"/>.</summary>
    public double Shear => (Synthesis & FontSynthesis.Oblique) != FontSynthesis.None ? ObliqueShear : 0;

    /// <summary>Gets the width, in pixels, of the stroke the glyphs of the run are filled with: 0 unless they are made bold.</summary>
    public double EmboldenWidth => (Synthesis & FontSynthesis.Bold) != FontSynthesis.None ? Size * BoldWidth : 0;
}
