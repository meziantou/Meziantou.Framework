using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>How <see cref="Drawing.DrawingCanvas.FillGlyphs"/> and <see cref="Drawing.DrawingCanvas.StrokeGlyphs"/> draw a run of glyphs.</summary>
public sealed class GlyphRunOptions
{
    /// <summary>Gets what is made from the glyphs when the style wanted has no font: a bold, an oblique, or both (<see cref="FontSynthesis"/>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has a flag that <see cref="FontSynthesis"/> does not define.</exception>
    public FontSynthesis Synthesis
    {
        get;
        init
        {
            if ((value & ~(FontSynthesis.Bold | FontSynthesis.Oblique)) != FontSynthesis.None)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The synthesis is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the width of the stroke a synthetic bold fills the glyphs with, in units of the drawing, or
    /// <see langword="null"/> (the default) for 1/24 of the size of the text. Browsers use 1/24 up to 9 pixels and 1/32
    /// from 36 pixels.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or exceeds 16777216.</exception>
    public double? BoldWidth
    {
        get;
        init
        {
            if (value is { } width)
            {
                DrawingGuard.Length(width, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>
    /// Gets what a synthetic oblique adds to the horizontal position of a point for each unit of its height above the
    /// baseline, or <see langword="null"/> (the default) for 1/4: the tangent of the angle of the slant.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of the value exceeds 16.</exception>
    public double? ObliqueShear
    {
        get;
        init
        {
            if (value is { } shear)
            {
                if (!double.IsFinite(shear))
                    throw new ArgumentException("The shear must be a finite number.", nameof(value));

                ArgumentOutOfRangeException.ThrowIfGreaterThan(Math.Abs(shear), 16, nameof(value));
            }

            field = value;
        }
    }

    /// <summary>Gets the index of the palette of the glyphs a font draws with its own colors. Defaults to 0.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public int Palette
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    }

    /// <summary>
    /// Gets the cache that keeps the coverage of the glyphs and the display lists of the glyphs a font draws with its own
    /// colors from one draw call to the next, or <see langword="null"/> (the default) for none: the pixels are the same
    /// either way.
    /// </summary>
    public GlyphCache? Cache { get; init; }

    internal static GlyphRunOptions Default { get; } = new();

    /// <summary>Gets the width of the stroke of a synthetic bold for a size, or 0.</summary>
    internal double GetBoldWidth(double size) => (Synthesis & FontSynthesis.Bold) == FontSynthesis.None ? 0 : BoldWidth ?? (size * GlyphRun.BoldWidth);

    /// <summary>Gets the shear of a synthetic oblique, or 0.</summary>
    internal double GetShear() => (Synthesis & FontSynthesis.Oblique) == FontSynthesis.None ? 0 : ObliqueShear ?? GlyphRun.ObliqueShear;
}
