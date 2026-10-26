namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

// The small letters, Fig. 69 to 94, dimensioned in thousandths of an inch (Size I only). The corners the drawings
// mark with a radius of 13.8 mils are arcs; the 2 degree lean of some stems is not reproduced.
internal static partial class OcrA
{
    private const double SmallRadius = 13.8;

    private static void AddSmallLetters(List<GlyphDesign> glyphs)
    {
        // Fig. 69
        glyphs.Add(G("a", 'a')
            .Curve(Round, In(13.8, 64.6), M(55, 64.6, SmallRadius), In(55, 0))
            .Curve(Round, In(55, 35.3), M(0, 35.3, SmallRadius), M(0, 0, SmallRadius), In(34.4, 0), In(55, 11.8)));

        // Fig. 70
        glyphs.Add(G("b", 'b')
            .Line(In(0, 94), In(0, 0))
            .Curve(Round, In(0, 47), M(20.6, 64.6, SmallRadius), M(41.3, 64.6, SmallRadius), M(55, 52.9, SmallRadius), M(55, 11.8, SmallRadius), M(41.3, 0, SmallRadius), M(20.6, 0, SmallRadius), In(0, 17.6)));

        // Fig. 71
        glyphs.Add(G("c", 'c')
            .Curve(Round, In(55, 64.6), M(20.6, 64.6, SmallRadius), M(0, 47, SmallRadius), M(0, 17.6, SmallRadius), M(20.6, 0, SmallRadius), In(55, 0)));

        // Fig. 72
        glyphs.Add(G("d", 'd')
            .Line(In(55, 94), In(55, 0))
            .Curve(Round, In(55, 47), M(34.4, 64.6, SmallRadius), M(13.8, 64.6, SmallRadius), M(0, 52.9, SmallRadius), M(0, 11.8, SmallRadius), M(13.8, 0, SmallRadius), M(34.4, 0, SmallRadius), In(55, 17.6)));

        // Fig. 73
        glyphs.Add(G("e", 'e')
            .Curve(Round, In(0, 29.4), In(55, 29.4), M(55, 52.9, SmallRadius), M(41.3, 64.6, SmallRadius), M(13.8, 64.6, SmallRadius), M(0, 52.9, SmallRadius), M(0, 11.8, SmallRadius), M(13.8, 0, SmallRadius), In(55, 0)));

        // Fig. 74: ΔX = +3.4
        glyphs.Add(ShiftMils(G("f", 'f')
            .Curve(Round, In(20.6, 0), M(20.6, 94, 17.6), In(55, 94))
            .Line(In(6.9, 64.6), In(41.3, 64.6)), 3.4));

        // Fig. 75
        glyphs.Add(G("g", 'g')
            .Curve(Round, In(55, 47), M(34.4, 64.6, SmallRadius), M(13.8, 64.6, SmallRadius), M(0, 52.9, SmallRadius), M(0, 17.6, SmallRadius), M(13.8, 5.9, SmallRadius), M(34.4, 5.9, SmallRadius), In(55, 23.5))
            .Curve(Round, In(55, 64.6), M(55, -17.6, SmallRadius), M(41.3, -29.4, SmallRadius), In(6.9, -29.4)));

        // Fig. 76
        glyphs.Add(G("h", 'h')
            .Line(In(0, 94), In(0, 0))
            .Curve(Round, In(0, 47), M(20.6, 64.6, SmallRadius), M(55, 64.6, SmallRadius), In(55, 0)));

        // Fig. 77: the dot is a square of 5.9 mils
        glyphs.Add(SmallDot(G("i", 'i'), 27.5, 94)
            .Line(In(6.9, 64.6), In(27.5, 64.6), In(27.5, 0))
            .Line(In(6.9, 0), In(48.1, 0)));

        // Fig. 78: ΔX = -6.9
        glyphs.Add(ShiftMils(SmallDot(G("j", 'j'), 41.3, 94)
            .Curve(Round, In(20.6, 64.6), In(41.3, 64.6), M(41.3, -29.4, SmallRadius), M(0, -29.4, SmallRadius), In(0, -17.6)), -6.9));

        // Fig. 79: the arm runs from (48.1, 64.6) to the stem at (0, 23.5), the leg from (13.8, 35.3)
        glyphs.Add(G("k", 'k')
            .Line(In(0, 94), In(0, 0))
            .Line(In(48.1, 64.6), In(0, 23.5))
            .Line(In(13.8, 35.3), In(55, 0)));

        // Fig. 80
        glyphs.Add(G("l", 'l')
            .Line(In(6.9, 94), In(27.5, 94), In(27.5, 0))
            .Line(In(6.9, 0), In(48.1, 0)));

        // Fig. 81: wider than W, the arches turn along arcs of 10.0 and 9.7 mils
        glyphs.Add(G("m", 'm')
            .Line(In(-1.5, 64.6), In(-1.5, 0))
            .Curve(Round, In(-1.5, 51.6), M(13.8, 64.6, 10.0), M(27.5, 64.6, 10.0), In(27.5, 0))
            .Curve(Round, In(27.5, 52.9), M(41.3, 64.6, 9.7), M(56.5, 64.6, 9.7), In(56.5, 0)));

        // Fig. 82
        glyphs.Add(G("n", 'n')
            .Line(In(0, 64.6), In(0, 0))
            .Curve(Round, In(0, 47), M(20.6, 64.6, SmallRadius), M(55, 64.6, SmallRadius), In(55, 0)));

        // Fig. 83
        glyphs.Add(G("o", 'o')
            .ClosedCurve(Round, M(13.8, 64.6, SmallRadius), M(41.3, 64.6, SmallRadius), M(55, 52.9, SmallRadius), M(55, 11.8, SmallRadius), M(41.3, 0, SmallRadius), M(13.8, 0, SmallRadius), M(0, 11.8, SmallRadius), M(0, 52.9, SmallRadius)));

        // Fig. 84: the bowl is taller than the x-height, from -2.95 to 67.55
        glyphs.Add(G("p", 'p')
            .Line(In(0, 67.55), In(0, -29.4))
            .Curve(Round, In(0, 49.8), M(20.6, 67.55, SmallRadius), M(34.4, 67.55, SmallRadius), M(55, 47, SmallRadius), M(55, 17.6, SmallRadius), M(34.4, -2.95, SmallRadius), M(20.6, -2.95, SmallRadius), In(0, 14.8)));

        // Fig. 85
        glyphs.Add(G("q", 'q')
            .Line(In(55, 64.6), In(55, -29.4))
            .Curve(Round, In(55, 47), M(34.4, 64.6, SmallRadius), M(13.8, 64.6, SmallRadius), M(0, 52.9, SmallRadius), M(0, 11.8, SmallRadius), M(13.8, 0, SmallRadius), M(34.4, 0, SmallRadius), In(55, 17.6)));

        // Fig. 86
        glyphs.Add(G("r", 'r')
            .Line(In(0, 64.6), In(0, 0))
            .Curve(Round, In(0, 41.1), M(27.5, 64.6, SmallRadius), M(55, 64.6, SmallRadius), In(55, 47)));

        // Fig. 87: the diagonal runs through (0, 47.0) and (55.0, 23.5); it turns into the bars along arcs of 11.0 and
        // 14.2 mils where it crosses their lines
        glyphs.Add(G("s", 's')
            .Curve(Round, In(53.5, 58.8), M(53.5, 64.6, 5.8), M(-41.2, 64.6, 11.0), M(110.0, 0, 14.2), M(1.5, 0, 5.9), In(1.5, 5.9)));

        // Fig. 88
        glyphs.Add(G("t", 't')
            .Curve(Round, In(13.8, 88.1), M(13.8, 0, SmallRadius), M(55, 0, 11.8), In(55, 11.8))
            .Line(In(0, 64.6), In(48.1, 64.6)));

        // Fig. 89
        glyphs.Add(G("u", 'u')
            .Curve(Round, In(0, 64.6), M(0, 0, SmallRadius), M(27.5, 0, SmallRadius), In(55, 17.6))
            .Line(In(55, 64.6), In(55, 0)));

        // Fig. 90
        glyphs.Add(G("v", 'v')
            .Line(In(0, 64.6), In(0, 52.9), In(23.2, 0), In(31.8, 0), In(55, 52.9), In(55, 64.6)));

        // Fig. 91
        glyphs.Add(G("w", 'w')
            .Line(In(0, 64.6), In(0, 23.5), In(6.9, 0), In(13.8, 0), In(27.5, 23.5), In(41.3, 0), In(48.1, 0), In(55, 23.5), In(55, 64.6))
            .Line(In(27.5, 23.5), In(27.5, 41.1)));

        // Fig. 92
        glyphs.Add(G("x", 'x')
            .Line(In(1.5, 64.6), In(53.5, 0))
            .Line(In(53.5, 64.6), In(1.5, 0)));

        // Fig. 93
        glyphs.Add(G("y", 'y')
            .Line(In(0, 64.6), In(0, 52.9), In(20.6, 5.9), In(34.4, 5.9))
            .Line(In(55, 64.6), In(55, 52.9), In(18.9, -29.4), In(0, -29.4)));

        // Fig. 94
        glyphs.Add(G("z", 'z')
            .Line(In(0, 64.6), In(55, 64.6), In(55, 58.8), In(0, 5.9), In(0, 0), In(55, 0)));
    }

    /// <summary>A vertex of a small-letter drawing, in mils, whose corner is an arc of a radius in mils.</summary>
    private static Vertex M(double x, double y, double radius) => new(In(x, y), radius * 0.0254 * OcrFont.UnitsPerMillimeter);

    /// <summary>Adds the square dot of i and j: 5.9 mils high from 94.0 mils, centered on a stem.</summary>
    private static GlyphDesign SmallDot(GlyphDesign design, double x, double y)
    {
        var (a, b) = (In(x - 2.95, y), In(x + 2.95, y + 5.9));
        return design
            .Loop(a, new PointD(b.X, a.Y), b, new PointD(a.X, b.Y))
            .Fill(new RectangleD(a.X, a.Y, b.X - a.X, b.Y - a.Y));
    }

    /// <summary>Moves a small letter whose character centerline is <paramref name="deltaX"/> mils right of the half width.</summary>
    private static GlyphDesign ShiftMils(GlyphDesign design, double deltaX)
    {
        design.OffsetX = -deltaX * 0.0254 * OcrFont.UnitsPerMillimeter;
        return design;
    }
}
