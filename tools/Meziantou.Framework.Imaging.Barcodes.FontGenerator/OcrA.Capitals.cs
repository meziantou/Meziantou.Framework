namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

// The capital letters, Fig. 15 to 40
internal static partial class OcrA
{
    private static void AddCapitals(List<GlyphDesign> glyphs)
    {
        // Fig. 15
        glyphs.Add(G("A", 'A')
            .Line(P(0, 0), P(0.5, 1), P(1, 0))
            .Line(P(1.0 / 8, 0.25), P(7.0 / 8, 0.25)));

        // Fig. 16: the bowls are arcs of radius 3/2 T; the left corners are sharp
        glyphs.Add(G("B", 'B')
            .Curve(Sharp, P(0, 0.5), P(0, 1), V(1, 1, 1.5 * T), V(1, 0.5, 1.5 * T), P(0, 0.5))
            .Curve(Sharp, P(0, 0.5), V(1, 0.5, 1.5 * T), V(1, 0, 1.5 * T), P(0, 0), P(0, 0.5)));

        // Fig. 17: the knees are arcs of radius T, the left side arcs of radius r1
        glyphs.Add(G("C", 'C')
            .Curve(Round, P(1, 1), V(3.0 / 8, 1, T), V(0, 9.0 / 16, R1), V(0, 7.0 / 16, R1), V(3.0 / 8, 0, T), P(1, 0)));

        // Fig. 18
        glyphs.Add(G("D", 'D')
            .Curve(Round, P(0, 1), V(5.0 / 8, 1, T), V(1, 9.0 / 16, R1), V(1, 7.0 / 16, R1), V(5.0 / 8, 0, T), P(0, 0))
            .Line(P(0.25, 1), P(0.25, 0)));

        // Fig. 19
        glyphs.Add(G("E", 'E')
            .SharpLine(P(1, 1), P(0, 1), P(0, 0), P(1, 0))
            .Line(P(0, 0.5), P(0.5, 0.5)));

        // Fig. 20
        glyphs.Add(G("F", 'F')
            .SharpLine(P(1, 1), P(0, 1), P(0, 0))
            .Line(P(0, 5.0 / 8), P(0.75, 5.0 / 8)));

        // Fig. 21: the corner where the bar meets the right stroke is sharp
        glyphs.Add(G("G", 'G')
            .Curve(Round, P(1, 1), V(0.5, 1, T), V(0, 5.0 / 8, 1.5 * T), V(0, 0, T), V(1, 0, T), P(1, 3.0 / 16))
            .SharpLine(P(1, 3.0 / 16), P(1, 3.0 / 8), P(0.5, 3.0 / 8)));

        // Fig. 22
        glyphs.Add(G("H", 'H')
            .Line(P(0, 0), P(0, 1))
            .Line(P(1, 0), P(1, 1))
            .Line(P(0, 0.5), P(1, 0.5)));

        // Fig. 23
        glyphs.Add(G("I", 'I')
            .Line(P(0, 1), P(1, 1))
            .Line(P(0, 0), P(1, 0))
            .Line(P(0.5, 0), P(0.5, 1)));

        // Fig. 24
        glyphs.Add(G("J", 'J')
            .Curve(Round, P(1.0 / 8, 3.0 / 8), V(1.0 / 8, 0, T), V(7.0 / 8, 0, T), P(7.0 / 8, 1)));

        // Fig. 25
        glyphs.Add(G("K", 'K')
            .Line(P(0, 0), P(0, 1))
            .Line(P(1, 1), P(0, 0.5), P(1, 0)));

        // Fig. 26
        glyphs.Add(G("L", 'L')
            .SharpLine(P(0, 1), P(0, 0), P(1, 0)));

        // Fig. 27: the outer top corners are sharp, the bottom of the V is round
        glyphs.Add(G("M", 'M')
            .Curve(Sharp, P(0, 0), P(0, 1), P(1.0 / 8, 1), V(0.5, 11.0 / 16, 1), P(7.0 / 8, 1), P(1, 1), P(1, 0)));

        // Fig. 28
        glyphs.Add(G("N", 'N')
            .Curve(Sharp, P(0, 0), P(0, 1), P(1.0 / 8, 1), P(7.0 / 8, 0), P(1, 0), P(1, 1)));

        // Fig. 29: arcs of radius r2 at the top and the bottom, r1 at the sides
        glyphs.Add(G("O", 'O')
            .ClosedCurve(Round, V(3.0 / 8, 1, R2), V(5.0 / 8, 1, R2), V(1, 9.0 / 16, R1), V(1, 7.0 / 16, R1), V(5.0 / 8, 0, R2), V(3.0 / 8, 0, R2), V(0, 7.0 / 16, R1), V(0, 9.0 / 16, R1)));

        // Fig. 30
        glyphs.Add(G("P", 'P')
            .Curve(Sharp, P(0, 0), P(0, 1), V(1, 1, T), V(1, 7.0 / 16, T), P(0, 7.0 / 16)));

        // Fig. 31: a parallelogram with rounded corners, the tail turns sharply
        glyphs.Add(G("Q", 'Q')
            .ClosedCurve(Round, V(0, 5.0 / 8, 1.5 * T), V(0, 0, T), V(1, 3.0 / 8, 1.5 * T), V(1, 1, R3), V(0.75, 1, R3))
            .SharpLine(P(0.5, 3.0 / 8), P(0.75, 0), P(1, 0)));

        // Fig. 32
        glyphs.Add(G("R", 'R')
            .Curve(Sharp, P(0, 0), P(0, 1), V(1, 1, T), V(1, 5.0 / 8, T), P(0, 5.0 / 8))
            .Line(P(3.0 / 8, 5.0 / 8), P(1, 0)));

        // Fig. 33: the diagonal runs from (0, 7/8 H) to (W, 1/8 H); it turns into the bars along arcs of radius r4 and the
        // ends curl along arcs of radius 1/8 H
        glyphs.Add(G("S", 'S')
            .Curve(Round, P(1, 7.0 / 8), V(1, 1, H / 8), V(-1.0 / 6, 1, R4), V(7.0 / 6, 0, R4), V(0, 0, H / 8), P(0, 1.0 / 8)));

        // Fig. 34: the top corners are sharp
        glyphs.Add(G("T", 'T')
            .SharpLine(P(0, 7.0 / 8), P(0, 1), P(1, 1), P(1, 7.0 / 8))
            .Line(P(0.5, 1), P(0.5, 0)));

        // Fig. 35
        glyphs.Add(G("U", 'U')
            .Curve(Round, P(0, 1), V(0, 0, T), V(1, 0, T), P(1, 1)));

        // Fig. 36
        glyphs.Add(G("V", 'V')
            .Line(P(0, 1), P(0, 0.75), P(0.5, 0), P(1, 0.75), P(1, 1)));

        // Fig. 37
        glyphs.Add(G("W", 'W')
            .Line(P(0, 1), P(0, 1.0 / 8), P(1.0 / 8, 0), P(3.0 / 8, 0), P(0.5, 1.0 / 8), P(5.0 / 8, 0), P(7.0 / 8, 0), P(1, 1.0 / 8), P(1, 1))
            .Line(P(0.5, 1.0 / 8), P(0.5, 5.0 / 8)));

        // Fig. 38
        glyphs.Add(G("X", 'X')
            .Line(P(0, 1), P(1, 0))
            .Line(P(1, 1), P(0, 0)));

        // Fig. 39
        glyphs.Add(G("Y", 'Y')
            .Line(P(0, 1), P(0, 7.0 / 8), P(0.5, 0.5), P(1, 7.0 / 8), P(1, 1))
            .Line(P(0.5, 0.5), P(0.5, 0)));

        // Fig. 40: the corners of the diagonal are cut square 1/2 T beyond the 0.0 and W lines
        glyphs.Add(G("Z", 'Z')
            .Line(P(0, 1), P(1, 1))
            .Line(P(0, 0), P(1, 0))
            .Fill(Band(P(1, 1), P(0, 0), Box(0, 0, 1, 1, T / 2)))
            .Fill(Box(0.5, 1, 1, 1, T / 2))
            .Fill(Box(0, 0, 0.5, 0, T / 2)));
    }
}
