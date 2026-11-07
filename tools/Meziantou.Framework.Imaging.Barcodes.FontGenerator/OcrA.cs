using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

/// <summary>
/// The OCR-A characters of ANSI X3.17-1981, Size I: the centerlines of Fig. 1 to 94 and Appendix B, in the fractions of
/// the nominal centerline height <c>H</c> (2.40 mm) and width <c>W</c> (1.40 mm) the drawings give, painted with the
/// nominal stroke width <c>T</c> (0.35 mm), Table 1. The origin of the drawings (0.0) is the left end of the centerline
/// width on the baseline; the drawings are centered on the advance.
/// </summary>
internal static partial class OcrA
{
    public const double H = 2.40 * OcrFont.UnitsPerMillimeter;
    public const double W = 1.40 * OcrFont.UnitsPerMillimeter;
    public const double T = 0.35 * OcrFont.UnitsPerMillimeter;

    // The radii of Table 1, Size I
    private const double R1 = 0.635 * OcrFont.UnitsPerMillimeter;
    private const double R2 = 0.283 * OcrFont.UnitsPerMillimeter;
    private const double R3 = 0.255 * OcrFont.UnitsPerMillimeter;
    private const double R4 = 0.223 * OcrFont.UnitsPerMillimeter;

    // The x of the 0.0 of the drawings: the half-width centerline (1/2 W) is the middle of the advance
    private const double Left = (OcrFont.Advance / 2.0) - (W / 2);

    public static OcrFont Font { get; } = new()
    {
        FileName = "OcrA.ttf",
        FamilyName = "Meziantou OCR-A",
        PostScriptName = "MeziantouOCR-A",
        Standard = "ANSI X3.17-1981 (OCR-A)",
        Glyphs = CreateGlyphs(),
    };

    /// <summary>A point of a drawing, in fractions of <see cref="W"/> and <see cref="H"/>.</summary>
    private static PointD P(double x, double y) => new(Left + (x * W), y * H);

    /// <summary>A point of a small-letter drawing, in thousandths of an inch from the 0.0 of the drawing.</summary>
    private static PointD In(double x, double y) => new(Left + (x * 0.0254 * OcrFont.UnitsPerMillimeter), y * 0.0254 * OcrFont.UnitsPerMillimeter);

    private const LineJoin Round = LineJoin.Round;
    private const LineJoin Sharp = LineJoin.Miter;

    private static GlyphDesign G(string name, params int[] codePoints) => new(name, codePoints, T);

    /// <summary>A vertex of a drawing, in fractions of <see cref="W"/> and <see cref="H"/>, whose corner is an arc of a radius.</summary>
    private static Vertex V(double x, double y, double radius) => new(P(x, y), radius);

    /// <summary>The area of a stroke of width <see cref="T"/> along the line through two points, cut by a rectangle: a corner whose outline the drawings cut square.</summary>
    private static VectorPath Band(PointD a, PointD b, RectangleD clip)
    {
        var length = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
        var (ux, uy) = ((b.X - a.X) / length, (b.Y - a.Y) / length);
        var (nx, ny) = (-uy * T / 2, ux * T / 2);
        const double Extent = 2000;
        var band = new VectorPathBuilder()
            .MoveTo(a.X - (ux * Extent) + nx, a.Y - (uy * Extent) + ny)
            .LineTo(b.X + (ux * Extent) + nx, b.Y + (uy * Extent) + ny)
            .LineTo(b.X + (ux * Extent) - nx, b.Y + (uy * Extent) - ny)
            .LineTo(a.X - (ux * Extent) - nx, a.Y - (uy * Extent) - ny)
            .Close()
            .Build();
        return band.Combine(new VectorPathBuilder().AddRectangle(clip).Build(), PathCombineMode.Intersect);
    }

    /// <summary>A rectangle between two points of a drawing (fractions of <see cref="W"/> and <see cref="H"/>), widened by font units.</summary>
    private static RectangleD Box(double x1, double y1, double x2, double y2, double margin = 0)
    {
        var a = P(x1, y1);
        var b = P(x2, y2);
        return new RectangleD(Math.Min(a.X, b.X) - margin, Math.Min(a.Y, b.Y) - margin, Math.Abs(b.X - a.X) + (2 * margin), Math.Abs(b.Y - a.Y) + (2 * margin));
    }

    /// <summary>Moves a glyph whose character centerline is <paramref name="deltaX"/> (in fractions of <see cref="W"/>) right of the half width (ΔX of the drawings).</summary>
    private static GlyphDesign Shift(GlyphDesign design, double deltaX)
    {
        design.OffsetX = -deltaX * W;
        return design;
    }

    private static List<GlyphDesign> CreateGlyphs()
    {
        var glyphs = new List<GlyphDesign>();
        AddDigits(glyphs);
        AddCapitals(glyphs);
        AddSymbols(glyphs);
        AddSmallLetters(glyphs);
        return glyphs;
    }
}
