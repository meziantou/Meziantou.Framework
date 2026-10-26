namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

// The digits and the special symbols, Fig. 1 to 14. A corner the drawings mark with a radius of 1/2 T is a round join;
// a corner they draw square is a sharp (mitered) join.
internal static partial class OcrA
{
    private static void AddDigits(List<GlyphDesign> glyphs)
    {
        // Fig. 1
        glyphs.Add(G("one", '1')
            .SharpLine(P(0, 1), P(0.5, 1), P(0.5, 0))
            .Line(P(0, 0), P(1, 0), P(1, 3.0 / 8)));

        // Fig. 2: the bottom-left corner is sharp
        glyphs.Add(G("two", '2')
            .Line(P(0, 1), P(1, 1), P(1, 0.5), P(0, 0.5), P(0, 0.25))
            .SharpLine(P(0, 0.25), P(0, 0), P(1, 0)));

        // Fig. 3: the right stroke turns into the middle bar along arcs of radius T
        glyphs.Add(G("three", '3')
            .Curve(Round, P(0, 1), P(1, 1), V(1, 0.5, T), P(0.25, 0.5))
            .Curve(Round, P(0.25, 0.5), V(1, 0.5, T), P(1, 0), P(0, 0)));

        // Fig. 4: the character centerline is 1/16 W right of the half width
        glyphs.Add(Shift(G("four", '4')
            .SharpLine(P(1.0 / 8, 1), P(1.0 / 8, 3.0 / 8), P(1, 3.0 / 8))
            .Line(P(7.0 / 8, 7.0 / 8), P(7.0 / 8, 0)), 1.0 / 16));

        // Fig. 5: the top-left corners are sharp; the tail rises to 1/16 H
        glyphs.Add(G("five", '5')
            .SharpLine(P(1, 1), P(0, 1), P(0, 0.5), P(0.5, 0.5))
            .Curve(Round, P(0.5, 0.5), P(1, 0.5), P(1, 0), V(0.25, 0, T), P(0, 1.0 / 16)));

        // Fig. 6
        glyphs.Add(G("six", '6')
            .Line(P(1.0 / 8, 1), P(0, 1), P(0, 0), P(1, 0), P(1, 3.0 / 8), P(0, 3.0 / 8)));

        // Fig. 7: the top-right corner is sharp
        glyphs.Add(G("seven", '7')
            .Line(P(0, 15.0 / 16), P(0, 1), P(0.5, 1))
            .SharpLine(P(0.5, 1), P(1, 1), P(1, 5.0 / 8))
            .Line(P(1, 5.0 / 8), P(0.5, 3.0 / 8), P(0.5, 0)));

        // Fig. 8
        glyphs.Add(G("eight", '8')
            .Loop(P(0.25, 0.5), P(0.25, 1), P(0.75, 1), P(0.75, 0.5))
            .Loop(P(0, 0), P(0, 0.5), P(1, 0.5), P(1, 0)));

        // Fig. 9
        glyphs.Add(G("nine", '9')
            .Loop(P(0, 1), P(1, 1), P(1, 5.0 / 8), P(0, 5.0 / 8))
            .Line(P(1, 1), P(1, 0), P(7.0 / 8, 0)));

        // Fig. 10
        glyphs.Add(G("zero", '0')
            .Loop(P(0, 0), P(0, 1), P(1, 1), P(1, 0)));

        // Fig. 11, 12 and 13: Hook, Fork and Chair (U+2440 to U+2442), which also stand for the Tilde, the Underline and
        // the Grave Accent the repertoire has no graphic for (2.4.1)
        glyphs.Add(G("uni2440", 0x2440, '~')
            .SharpLine(P(0, 3.0 / 8), P(0, 0), P(0.5, 0), P(0.5, 1), P(1, 1), P(1, 5.0 / 8)));
        glyphs.Add(G("uni2442", 0x2442, '_')
            .SharpLine(P(0, 1), P(0, 0.5), P(1, 0.5), P(1, 1))
            .Line(P(0.5, 0.5), P(0.5, 0)));
        glyphs.Add(G("uni2441", 0x2441, '`')
            .SharpLine(P(0, 0), P(0, 0.5), P(1, 0.5))
            .Line(P(1, 0), P(1, 1)));

        // Fig. 14: Long Vertical Mark, square ends, from Y = 0.53 mm below the baseline to L = 3.71 mm above it (2.4.5)
        glyphs.Add(G("bar", '|')
            .Fill(builder => builder.AddRectangle(new RectangleD((OcrFont.Advance - T) / 2, -0.53 * OcrFont.UnitsPerMillimeter, T, (3.71 + 0.53) * OcrFont.UnitsPerMillimeter))));
    }
}
