using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.Imaging.Barcodes.FontGenerator;

/// <summary>
/// The OCR-B characters of ANSI X3.49-1975 (the American counterpart of ECMA-11 and ISO 1073-2), Size I: the centerlines
/// traced from the 4:1 illustrations of the standard (<see cref="TracedGlyphs"/>), painted with the nominal stroke width
/// of 2.2: 0.35 mm, and 0.31 mm for the small letters.
/// </summary>
internal static partial class OcrB
{
    public static OcrFont Font { get; } = new()
    {
        FileName = "OcrB.ttf",
        FamilyName = "Meziantou OCR-B",
        PostScriptName = "MeziantouOCR-B",
        Standard = "ANSI X3.49-1975 (OCR-B)",
        Glyphs = CreateGlyphs(),
    };

    private static List<GlyphDesign> CreateGlyphs()
    {
        var glyphs = new List<GlyphDesign>();
        foreach (var traced in TracedGlyphs)
        {
            var codePoint = char.ConvertToUtf32(traced.Character, 0);
            var design = new GlyphDesign($"uni{codePoint:X4}", [codePoint], traced.StrokeWidth);
            // The illustration of the vertical line is broken (not to scale): it is one straight stroke from its lowest point
            // to its highest one
            if (traced.Character == "|")
            {
                var xs = traced.Strokes.SelectMany(stroke => stroke.Where((_, i) => i % 2 == 0)).ToArray();
                var ys = traced.Strokes.SelectMany(stroke => stroke.Where((_, i) => i % 2 == 1)).ToArray();
                design.Line(new PointD(xs.Average(), ys.Min()), new PointD(xs.Average(), ys.Max()));
                glyphs.Add(design);
                continue;
            }

            foreach (var stroke in traced.Strokes)
            {
                var points = new PointD[stroke.Length / 2];
                for (var i = 0; i < points.Length; i++)
                {
                    points[i] = new PointD(stroke[2 * i], stroke[(2 * i) + 1]);
                }

                if (points.Length > 2 && points[0] == points[^1])
                {
                    design.Loop(points[..^1]);
                }
                else
                {
                    design.Line(points);
                }
            }

            foreach (var outline in traced.Outlines)
            {
                design.Fill(builder =>
                {
                    builder.MoveTo(outline[0], outline[1]);
                    for (var i = 2; i < outline.Length; i += 2)
                    {
                        builder.LineTo(outline[i], outline[i + 1]);
                    }

                    builder.Close();
                });
            }

            glyphs.Add(design);
        }

        return glyphs;
    }
}

/// <summary>A traced character: its centerlines and the outlines of its areas that are not strokes (dots, wedges), as x, y pairs in font units.</summary>
internal sealed record TracedGlyph(string Character, double StrokeWidth, int[][] Strokes, int[][] Outlines);
