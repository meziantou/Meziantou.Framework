namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// A paint of the COLR table of a font under construction: OpenType specification, "COLR - Color Table", "Paint
/// tables". Only the formats that do not vary are written.
/// </summary>
internal abstract record FontColorPaint
{
    /// <summary>A color of the palette (0xFFFF for the color of the text) with an opacity.</summary>
    public sealed record Solid(int PaletteIndex, double Alpha = 1) : FontColorPaint;

    /// <summary>A gradient along the line from P0 to P1; the lines of one color are parallel to the line from P0 to P2.</summary>
    public sealed record Linear(FontColorLine Line, int X0, int Y0, int X1, int Y1, int X2, int Y2) : FontColorPaint;

    /// <summary>A gradient between two circles.</summary>
    public sealed record Radial(FontColorLine Line, int X0, int Y0, int Radius0, int X1, int Y1, int Radius1) : FontColorPaint;

    /// <summary>A gradient around a center, between two angles in degrees, counter-clockwise from the x axis.</summary>
    public sealed record Sweep(FontColorLine Line, int CenterX, int CenterY, double StartAngle, double EndAngle) : FontColorPaint;

    /// <summary>The outline of a glyph clips a paint.</summary>
    public sealed record Glyph(int GlyphId, FontColorPaint Paint) : FontColorPaint;

    /// <summary>The paint of another color glyph.</summary>
    public sealed record ColrGlyph(int GlyphId) : FontColorPaint;

    /// <summary>Paints, from the bottom to the top.</summary>
    public sealed record Layers(params FontColorPaint[] Paints) : FontColorPaint;

    /// <summary>A paint moved, scaled, rotated or skewed: x' = xx x + xy y + dx, y' = yx x + yy y + dy.</summary>
    public sealed record Transform(FontColorPaint Paint, double XX, double YX, double XY, double YY, double DX, double DY) : FontColorPaint;

    public sealed record Translate(FontColorPaint Paint, int DX, int DY) : FontColorPaint;

    /// <summary>A paint scaled around a center.</summary>
    public sealed record Scale(FontColorPaint Paint, double ScaleX, double ScaleY, int CenterX = 0, int CenterY = 0) : FontColorPaint;

    /// <summary>A paint rotated around a center, by an angle in degrees, counter-clockwise.</summary>
    public sealed record Rotate(FontColorPaint Paint, double Angle, int CenterX = 0, int CenterY = 0) : FontColorPaint;

    /// <summary>A paint skewed around a center, by angles in degrees.</summary>
    public sealed record Skew(FontColorPaint Paint, double XAngle, double YAngle, int CenterX = 0, int CenterY = 0) : FontColorPaint;

    /// <summary>A paint composed with a backdrop, with a composition mode (3 is source over).</summary>
    public sealed record Composite(FontColorPaint Source, int Mode, FontColorPaint Backdrop) : FontColorPaint;

    /// <summary>A paint of a format the specification does not define.</summary>
    public sealed record Unknown(int Format) : FontColorPaint;
}
