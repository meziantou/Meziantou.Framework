using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

internal sealed partial class DisplayListPdfWriter
{
    /// <summary>
    /// Draws a glyph run: the glyphs of the font file of the layout, embedded as a subset, at the positions the layout
    /// computed, with the text of the run so that it can be extracted. The glyphs of a font that cannot be embedded (an
    /// instance of a variable font, CFF2 outlines, a license that forbids it) are filled as paths, and the color glyphs
    /// (COLR, sbix, CBDT) are rasterized; both are marked with the text of the run (<c>/ActualText</c>).
    /// </summary>
    private void DrawGlyphs(DrawGlyphsItem draw)
    {
        var run = draw.Run;
        var paint = draw.Paint;
        var count = Math.Min(run.Glyphs.Length, run.Positions.Length);

        // The glyphs of a transparent text are in the display list for the paint timing only: they draw nothing
        if (count == 0 || (paint is { Shader: null, BlendMode: BlendMode.Normal } && paint.Color.IsTransparent))
            return;

        var font = run.Font;
        if (!(font.Size > 0) || !double.IsFinite(font.Size) || font.File.Faces is not { } faces || (uint)font.FaceIndex >= (uint)faces.Count)
            return;

        var openTypeFont = faces[font.FaceIndex];
        var face = openTypeFont.Face;
        var isInstance = font.Variations.Length > 0 && openTypeFont.Variations.IsVariable;
        if (isInstance)
        {
            try
            {
                face = openTypeFont.Variations.CreateInstance(font.Variations).Face;
            }
            catch (FontException)
            {
                return;
            }
        }

        var glyphCount = face.GlyphCount;
        var hasColorGlyphs = false;
        for (var i = 0; i < count; i++)
        {
            if (run.Glyphs[i] < glyphCount && face.IsColorGlyph(run.Glyphs[i]))
            {
                hasColorGlyphs = true;
                break;
            }
        }

        Mark(draw);
        if (hasColorGlyphs)
        {
            WithActualText(run.Text, () => Rasterize([draw], PdfGeometry.GetBounds(draw), firstItem: null));
            return;
        }

        var pdfFont = isInstance ? null : _owner.GetFont(font.File, font.FaceIndex);
        var scaleX = double.IsFinite(run.ScaleX) && run.ScaleX > 0 ? run.ScaleX : 1;

        // https://drafts.csswg.org/css-fonts-4/#font-synthesis-weight: the outline is thickened by a stroke;
        // https://drafts.csswg.org/css-fonts-4/#font-synthesis-style: the glyphs are slanted
        var boldWidth = font.SyntheticBold ? font.Size * GetBoldRatio(font.Size) : 0;
        var shear = font.SyntheticObliqueAngle == 0 || !double.IsFinite(font.SyntheticObliqueAngle) ? 0 : Math.Clamp(Math.Tan(font.SyntheticObliqueAngle * Math.PI / 180), -16, 16);
        var stroke = paint.Style == PaintStyle.Stroke ? paint.Stroke ?? new StrokeStyle(1) : null;
        var fills = stroke is null;
        var strokes = stroke is not null || boldWidth > 0;
        if (!BeginPaint(paint, fills, strokes, PdfGeometry.GetBounds(draw), out var restore))
            return;

        if (!restore)
        {
            _builder.SaveGraphicsState();
        }

        if (strokes)
        {
            SetStroke(stroke is null ? new StrokeStyle(boldWidth, Join: LineJoin.Round) : stroke with { Width = stroke.Width + boldWidth });
        }

        if (pdfFont is null)
        {
            WithActualText(run.Text, () => FillOutlines(face, draw, count, scaleX, shear, fills, strokes));
        }
        else
        {
            ShowGlyphs(pdfFont, run, count, glyphCount, scaleX, shear, fills, strokes);
        }

        _builder.RestoreGraphicsState();
    }

    private void ShowGlyphs(PdfFont pdfFont, GlyphRun run, int count, int glyphCount, double scaleX, double shear, bool fills, bool strokes)
    {
        // The text space has its y axis up: the text matrix flips the coordinates of the items, from the origin of the first glyph
        var origin = run.Positions[0];
        var glyphs = new List<PdfGlyph>(count);
        for (var i = 0; i < count; i++)
        {
            var glyph = run.Glyphs[i];
            var position = run.Positions[i];
            if (glyph >= glyphCount || !double.IsFinite(position.X) || !double.IsFinite(position.Y))
                continue;

            var y = origin.Y - position.Y;
            glyphs.Add(new PdfGlyph(glyph, (position.X - origin.X - (shear * y)) / scaleX, y));
        }

        if (glyphs.Count == 0)
            return;

        _builder.BeginText().SetFont(pdfFont, run.Font.Size);
        if (strokes)
        {
            _builder.SetTextRenderingMode(fills ? PdfTextRenderingMode.FillStroke : PdfTextRenderingMode.Stroke);
        }

        _builder.SetTextMatrix(new PdfMatrix(scaleX, 0, shear, -1, origin.X, origin.Y))
            .ShowGlyphs(glyphs.ToArray(), run.Text.Length > 0 ? run.Text : null)
            .EndText();
    }

    /// <summary>Fills (or strokes) the outlines of the glyphs of a run as paths.</summary>
    private void FillOutlines(FontFace face, DrawGlyphsItem draw, int count, double scaleX, double shear, bool fills, bool strokes)
    {
        var run = draw.Run;
        var sink = new PathSink(_builder);
        using var scratch = new GlyphScratch(face.Data.Environment.CreateAllocator("PdfRendering"));
        for (var i = 0; i < count; i++)
        {
            var glyph = run.Glyphs[i];
            var position = run.Positions[i];
            if (glyph >= face.GlyphCount || !double.IsFinite(position.X) || !double.IsFinite(position.Y))
                continue;

            sink.Transform = new DisplayMatrix(scaleX, 0, shear, -1, position.X, position.Y);
            try
            {
                var budget = new GlyphBudget(face.Data.Limits);
                face.DecodeOutline(glyph, run.Font.Size, sink, scratch, ref budget);
            }
            catch (FontException)
            {
                // A malformed glyph is not drawn
            }
        }

        if (!sink.HasPath)
            return;

        if (fills && strokes)
        {
            _builder.FillAndStroke();
        }
        else if (strokes)
        {
            _builder.Stroke();
        }
        else
        {
            _builder.Fill();
        }
    }

    /// <summary>Writes content that shows a text without showing its glyphs, marked with the text as its replacement text.</summary>
    private void WithActualText(string text, Action write)
    {
        if (text.Length == 0)
        {
            write();
            return;
        }

        _builder.BeginMarkedContent("Span", new PdfDictionary { [PdfName.Create("ActualText")] = PdfString.FromText(text) });
        write();
        _builder.EndMarkedContent();
    }

    /// <summary>The width of the stroke of the synthesized bold, as a fraction of the font size: 1/24 up to 9px, 1/32 from 36px.</summary>
    private static double GetBoldRatio(double size)
    {
        const double SmallSize = 9;
        const double LargeSize = 36;
        const double SmallRatio = 1d / 24;
        const double LargeRatio = 1d / 32;
        if (size <= SmallSize)
            return SmallRatio;

        if (size >= LargeSize)
            return LargeRatio;

        return SmallRatio + ((LargeRatio - SmallRatio) * (size - SmallSize) / (LargeSize - SmallSize));
    }

    /// <summary>Writes the outlines of glyphs (y up, from their origin) as the subpaths of a path, transformed to the coordinates of the items.</summary>
    private sealed class PathSink(PdfContentBuilder builder) : OutlineSink
    {
        private (double X, double Y) _current;

        public DisplayMatrix Transform { get; set; } = DisplayMatrix.Identity;

        public bool HasPath { get; private set; }

        public override void MoveTo(double x, double y)
        {
            var point = Map(x, y);
            builder.MoveTo(point.X, point.Y);
            _current = (x, y);
            HasPath = true;
        }

        public override void LineTo(double x, double y)
        {
            var point = Map(x, y);
            builder.LineTo(point.X, point.Y);
            _current = (x, y);
        }

        public override void QuadraticTo(double controlX, double controlY, double x, double y)
        {
            var (x0, y0) = _current;
            CubicTo(x0 + (2d / 3 * (controlX - x0)), y0 + (2d / 3 * (controlY - y0)), x + (2d / 3 * (controlX - x)), y + (2d / 3 * (controlY - y)), x, y);
        }

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y)
        {
            var p1 = Map(control1X, control1Y);
            var p2 = Map(control2X, control2Y);
            var p3 = Map(x, y);
            builder.CurveTo(p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y);
            _current = (x, y);
        }

        public override void Close() => builder.ClosePath();

        private PhysicalOffset Map(double x, double y) => Transform.Apply(new PhysicalOffset(x, y));
    }
}
