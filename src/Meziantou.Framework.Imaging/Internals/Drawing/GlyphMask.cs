namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The coverage of a glyph, in the pixels around the pixel of its origin: from <c>(Left, Top)</c>, row by row.</summary>
internal sealed class GlyphMask
{
    public GlyphMask(int left, int top, int width, int height, uint[] coverage)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
        Coverage = coverage;
    }

    /// <summary>Gets the mask of a glyph larger than a mask is kept for: it is drawn as a shape.</summary>
    public static GlyphMask TooLarge { get; } = new(0, 0, 0, 0, []);

    public int Left { get; }

    public int Top { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the coverages, from 0 to <see cref="CoverageRasterizer.FullCoverage"/>.</summary>
    public uint[] Coverage { get; }

    /// <summary>Gets an estimate of the bytes the mask holds.</summary>
    public long Bytes => 64 + (4L * Coverage.Length);
}
