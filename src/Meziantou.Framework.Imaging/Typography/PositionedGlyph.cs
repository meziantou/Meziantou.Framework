using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Typography;

/// <summary>A glyph of a font at a position: what a text shaper gives, which <see cref="Drawing.DrawingCanvas.FillGlyphs"/> draws.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct PositionedGlyph : IEquatable<PositionedGlyph>
{
    /// <summary>Initializes a new instance of the <see cref="PositionedGlyph"/> struct.</summary>
    /// <param name="glyph">The index of the glyph in the font.</param>
    /// <param name="origin">The origin of the glyph, on its baseline, in the coordinates of the drawing.</param>
    public PositionedGlyph(int glyph, PointD origin)
    {
        Glyph = glyph;
        Origin = origin;
    }

    /// <summary>Gets the index of the glyph in the font.</summary>
    public int Glyph { get; }

    /// <summary>Gets the origin of the glyph, on its baseline, in the coordinates of the drawing.</summary>
    public PointD Origin { get; }

    /// <inheritdoc />
    public bool Equals(PositionedGlyph other) => Glyph == other.Glyph && Origin.Equals(other.Origin);

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is PositionedGlyph other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Glyph, Origin);

    /// <summary>Compares two glyphs for equality.</summary>
    /// <param name="left">The first glyph.</param>
    /// <param name="right">The second glyph.</param>
    /// <returns><see langword="true"/> if they are the same glyph at the same position; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(PositionedGlyph left, PositionedGlyph right) => left.Equals(right);

    /// <summary>Compares two glyphs for inequality.</summary>
    /// <param name="left">The first glyph.</param>
    /// <param name="right">The second glyph.</param>
    /// <returns><see langword="true"/> if they are different glyphs or at different positions; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(PositionedGlyph left, PositionedGlyph right) => !left.Equals(right);
}
