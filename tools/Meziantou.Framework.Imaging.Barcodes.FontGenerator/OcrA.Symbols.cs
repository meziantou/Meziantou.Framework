namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

// The punctuation and the symbols, Fig. 41 to 68, in their preferred shapes (2.4.6)
internal static partial class OcrA
{
    private static void AddSymbols(List<GlyphDesign> glyphs)
    {
        // Fig. 41
        glyphs.Add(Dot(G("period", '.'), 3.0 / 8, 3.0 / 16, 5.0 / 8, 5.0 / 16));

        // Fig. 42
        glyphs.Add(G("comma", ',')
            .Line(P(0.25, 3.0 / 8), P(0.75, 3.0 / 8), P(0.75, 0)));

        // Fig. 43
        glyphs.Add(Dot(Dot(G("colon", ':'), 3.0 / 8, 3.0 / 16, 5.0 / 8, 5.0 / 16), 3.0 / 8, 11.0 / 16, 5.0 / 8, 13.0 / 16));

        // Fig. 44
        glyphs.Add(Dot(G("semicolon", ';'), 0.25, 11.0 / 16, 0.5, 13.0 / 16)
            .Line(P(0.25, 3.0 / 8), P(0.75, 3.0 / 8), P(0.75, 0)));

        // Fig. 45
        glyphs.Add(G("equal", '=')
            .Line(P(0, 11.0 / 16), P(1, 11.0 / 16))
            .Line(P(0, 3.0 / 8), P(1, 3.0 / 8)));

        // Fig. 46: the vertical stroke is W long, centered on 1/2 H
        glyphs.Add(G("plus", '+')
            .Line(P(0, 0.5), P(1, 0.5))
            .Line(new PointD(P(0.5, 0).X, (H / 2) - (W / 2)), new PointD(P(0.5, 0).X, (H / 2) + (W / 2))));

        // Fig. 47
        glyphs.Add(G("slash", '/')
            .Line(P(0, 0), P(1, 1)));

        // Fig. 48
        glyphs.Add(G("asterisk", '*')
            .Line(P(0.5, 1.0 / 8), P(0.5, 7.0 / 8))
            .Line(P(0, 0.75), P(1, 0.25))
            .Line(P(0, 0.25), P(1, 0.75)));

        // Fig. 49: the two wedges hang from a bar whose ends are cut square 1/2 T beyond the 0.0 and W lines
        glyphs.Add(G("quotedbl", '"')
            .Line(P(0, 1), P(1.0 / 8, 5.0 / 8), P(0.25, 1))
            .Line(P(0.75, 1), P(7.0 / 8, 5.0 / 8), P(1, 1))
            .Fill(Box(0, 1, 1, 1, T / 2)));

        // Fig. 50 and 51: the nib turns into the stem along arcs of radius T
        glyphs.Add(G("braceleft", '{')
            .Curve(Round, P(1, 1), P(0.5, 1), V(0.5, 0.5, T), P(0, 0.5))
            .Curve(Round, P(0, 0.5), V(0.5, 0.5, T), P(0.5, 0), P(1, 0)));
        glyphs.Add(G("braceright", '}')
            .Curve(Round, P(0, 1), P(0.5, 1), V(0.5, 0.5, T), P(1, 0.5))
            .Curve(Round, P(1, 0.5), V(0.5, 0.5, T), P(0.5, 0), P(0, 0)));

        // Fig. 52
        glyphs.Add(Dot(Dot(G("percent", '%'), 0, 7.0 / 8, 0.25, 1), 0.75, 0, 1, 1.0 / 8)
            .Line(P(0, 1.0 / 8), P(1, 7.0 / 8)));

        // Fig. 53: the dot is a disc of diameter T on the baseline
        glyphs.Add(G("question", '?')
            .Line(P(0, 5.0 / 8), P(0.75, 1), P(1, 7.0 / 8), P(1, 0.75), P(0.25, 3.0 / 8), P(0.25, 0.25))
            .Line(P(0.25, 0)));

        // Fig. 54: two diagonals, from (W, 0) to (0, 5/8 H) and from (3/4 W, 5/8 H) to (0, 1/4 H), and the arm from
        // (1/2 W, 0) to (W, 1/4 H); the loops turn along arcs of radius T
        glyphs.Add(G("ampersand", '&')
            .Curve(Round, P(1, 0), P(0, 5.0 / 8), V(0, 7.0 / 8, T), V(0.75, 7.0 / 8, T), P(0.75, 5.0 / 8), P(0, 0.25), V(0, 0, T), P(0.5, 0), P(1, 0.25)));

        // Fig. 55
        glyphs.Add(G("quotesingle", '\'', 0x2019)
            .Loop(P(3.0 / 8, 0.5), P(3.0 / 8, 1), P(5.0 / 8, 1))
            .Fill(builder => builder.MoveTo(P(3.0 / 8, 0.5)).LineTo(P(3.0 / 8, 1)).LineTo(P(5.0 / 8, 1)).Close()));

        // Fig. 56
        glyphs.Add(Dot(G("hyphen", '-', 0x2212), 0, 7.0 / 16, 1, 0.5));

        // Fig. 57
        glyphs.Add(G("dollar", '$')
            .Line(P(1, 0.75), P(0, 0.75), P(0, 0.5), P(1, 0.5), P(1, 0.25), P(0, 0.25))
            .Line(P(0.5, 0.75), P(0.5, 1))
            .Line(P(0.5, 0), P(0.5, 0.25)));

        // Fig. 58
        glyphs.Add(G("asciicircum", '^')
            .Line(P(0, 3.0 / 16), P(0.5, 0.75), P(1, 3.0 / 16)));

        // Fig. 59 and 60
        glyphs.Add(G("bracketleft", '[')
            .Line(P(1, 1), P(0, 1), P(0, 0), P(1, 0))
            .Line(P(3.0 / 8, 1), P(3.0 / 8, 0)));
        glyphs.Add(G("bracketright", ']')
            .Line(P(0, 1), P(1, 1), P(1, 0), P(0, 0))
            .Line(P(5.0 / 8, 1), P(5.0 / 8, 0)));

        // Fig. 61 and 62
        glyphs.Add(G("less", '<')
            .Line(P(1, 7.0 / 8), P(0, 17.0 / 32), P(0, 15.0 / 32), P(1, 1.0 / 8)));
        glyphs.Add(G("greater", '>')
            .Line(P(0, 7.0 / 8), P(1, 17.0 / 32), P(1, 15.0 / 32), P(0, 1.0 / 8)));

        // Fig. 63 and 64: the bends are arcs of radius 3/2 T
        glyphs.Add(G("parenleft", '(')
            .Curve(Round, P(0.75, 1), V(3.0 / 8, 23.0 / 32, 1.5 * T), V(3.0 / 8, 9.0 / 32, 1.5 * T), P(0.75, 0)));
        glyphs.Add(G("parenright", ')')
            .Curve(Round, P(0.25, 1), V(5.0 / 8, 23.0 / 32, 1.5 * T), V(5.0 / 8, 9.0 / 32, 1.5 * T), P(0.25, 0)));

        // Fig. 65
        glyphs.Add(Dot(G("exclam", '!'), 7.0 / 16, 0, 9.0 / 16, 1.0 / 16)
            .Loop(P(7.0 / 16, 1), P(9.0 / 16, 1), P(0.5, 7.0 / 16))
            .Fill(builder => builder.MoveTo(P(7.0 / 16, 1)).LineTo(P(9.0 / 16, 1)).LineTo(P(0.5, 7.0 / 16)).Close()));

        // Fig. 66
        glyphs.Add(G("numbersign", '#')
            .Line(P(0.25, 3.0 / 16), P(0.25, 1))
            .Line(P(0.75, 3.0 / 16), P(0.75, 1))
            .Line(P(0, 0.75), P(1, 0.75))
            .Line(P(0, 7.0 / 16), P(1, 7.0 / 16)));

        // Fig. 67: the curl is an arc of radius 3/16 H ending at (0, 7/8 H); the bowl and the bottom turn along arcs of
        // radius 1/4 W
        var curlCenter = new PointD(P(0, 0).X + (3.0 / 16 * H), 13.0 / 16 * H);
        var curlStart = 180 - (Math.Atan2((7.0 / 8 * H) - curlCenter.Y, 3.0 / 16 * H) * 180 / Math.PI);
        glyphs.Add(G("at", '@')
            .Arc(curlCenter, 3.0 / 16 * H, curlStart, 90 - curlStart)
            .Curve(Round, new PointD(curlCenter.X, H), V(1, 1, 3.0 / 16 * H), V(1, 0, W / 4), V(0.5, 0, W / 4), P(0.5, 9.0 / 16))
            .ClosedCurve(Round, P(0.5, 9.0 / 16), V(0, 9.0 / 16, W / 4), V(0, 0, W / 4), V(0.5, 0, W / 4)));

        // Fig. 68
        glyphs.Add(G("backslash", '\\')
            .Line(P(0, 1), P(1, 0)));
    }

    /// <summary>Adds a square dot: the area a stroke of width <see cref="T"/> covers around a rectangle of the drawing.</summary>
    private static GlyphDesign Dot(GlyphDesign design, double x1, double y1, double x2, double y2)
        => design.Loop(P(x1, y1), P(x2, y1), P(x2, y2), P(x1, y2)).Fill(Box(x1, y1, x2, y2));
}
