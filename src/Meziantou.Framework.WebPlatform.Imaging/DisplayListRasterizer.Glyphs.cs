using System.Buffers;
using Meziantou.Framework.WebPlatform.Rendering;
using AffineTransform = Meziantou.Framework.Imaging.AffineTransform;
using D = Meziantou.Framework.Imaging.Drawing;
using PointD = Meziantou.Framework.Imaging.PointD;
using T = Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.WebPlatform.Imaging;

internal sealed partial class DisplayListRasterizer
{
    /// <summary>
    /// Draws a glyph run: the glyphs of the font file of the layout, by id, at the positions computed by the text shaping. The
    /// glyphs are not hinted and are filled each on its own (the glyphs of a translucent text that overlap are composited one
    /// over the other); the glyphs a font draws with its own colors (COLR, sbix, CBDT) are drawn with them.
    /// </summary>
    private void DrawGlyphs(D.DrawingCanvas canvas, DrawGlyphsItem draw)
    {
        var run = draw.Run;
        var count = Math.Min(run.Glyphs.Length, run.Positions.Length);

        // The glyphs of a transparent text are in the display list for the paint timing only: they draw nothing
        if (count == 0 || (draw.Paint is { Shader: null, BlendMode: BlendMode.Normal } && draw.Paint.Color.IsTransparent))
            return;

        var font = run.Font;
        if (!(font.Size > 0) || font.Size > PathConversions.MaxCoordinate || _backend.Fonts.Get(font) is not { } imagingFont)
            return;

        if (CreateBrush(draw.Paint) is not { } brush)
            return;

        // The glyphs of an aliased font (Ahem) cover whole pixels, from origins rounded to the pixels (as the edges of the
        // boxes): a glyph paints the same pixels as a box of its size
        var antiAlias = draw.Paint.AntiAlias && !font.IsAliased;
        var blur = double.IsFinite(draw.Paint.BlurSigma) && draw.Paint.BlurSigma > 0 ? draw.Paint.BlurSigma : 0;

        // A run compressed horizontally (canvas maxWidth): the glyphs are scaled along the line, from their origins
        var scaleX = double.IsFinite(run.ScaleX) && run.ScaleX > 0 ? run.ScaleX : 1;

        // https://drafts.csswg.org/css-fonts-4/#font-synthesis-weight: the outline is thickened by a stroke (1/24 of the font size
        // up to 9px, 1/32 from 36px); https://drafts.csswg.org/css-fonts-4/#font-synthesis-style: the glyphs are slanted
        var boldWidth = font.SyntheticBold ? font.Size * GetBoldRatio(font.Size) : 0;
        var shear = font.SyntheticObliqueAngle == 0 || !double.IsFinite(font.SyntheticObliqueAngle) ? 0 : Math.Clamp(Math.Tan(font.SyntheticObliqueAngle * Math.PI / 180), -16, 16);
        var stroke = draw.Paint.Style == PaintStyle.Stroke ? draw.Paint.Stroke ?? new StrokeStyle(1) : null;

        var glyphs = ArrayPool<T.PositionedGlyph>.Shared.Rent(count);
        try
        {
            var glyphCount = imagingFont.GlyphCount;
            var length = 0;
            for (var i = 0; i < count; i++)
            {
                var glyph = run.Glyphs[i];
                var position = run.Positions[i];
                // The glyphs beyond the range of the coordinates of the drawing API are not drawn
                if (glyph >= glyphCount || !(Math.Abs(position.X) <= PathConversions.MaxCoordinate && Math.Abs(position.Y) <= PathConversions.MaxCoordinate))
                    continue;

                glyphs[length++] = new T.PositionedGlyph(glyph, new PointD(position.X / scaleX, position.Y));
            }

            if (length == 0)
                return;

            var segment = new ArraySegment<T.PositionedGlyph>(glyphs, 0, length);
            var synthesis = (boldWidth > 0 && stroke is null ? T.FontSynthesis.Bold : T.FontSynthesis.None) | (shear != 0 ? T.FontSynthesis.Oblique : T.FontSynthesis.None);
            var options = new T.GlyphRunOptions
            {
                Synthesis = synthesis,
                BoldWidth = boldWidth > 0 ? boldWidth : null,
                ObliqueShear = shear != 0 ? shear : null,
                Cache = _backend.GlyphCache,
            };

            var pen = stroke is null ? null : CreatePen(brush, boldWidth > 0 ? stroke with { Width = stroke.Width + boldWidth } : stroke);
            var state = new GlyphRunState(imagingFont, font.Size, segment, options, brush, pen);
            if (scaleX != 1)
            {
                Paint(canvas, antiAlias, blur, (state, scaleX), static (c, s) => c.Transform(new AffineTransform(s.scaleX, 0, 0, 1, 0, 0), s.state, static (scaled, state) => state.Draw(scaled)));
            }
            else
            {
                Paint(canvas, antiAlias, blur, state, static (c, state) => state.Draw(c));
            }
        }
        finally
        {
            ArrayPool<T.PositionedGlyph>.Shared.Return(glyphs);
        }
    }

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

    /// <summary>A glyph run to draw.</summary>
    private readonly record struct GlyphRunState(T.Font Font, double Size, ArraySegment<T.PositionedGlyph> Glyphs, T.GlyphRunOptions Options, D.Brush Brush, D.Pen? Pen)
    {
        public void Draw(D.DrawingCanvas canvas)
        {
            if (Pen is not null)
            {
                canvas.StrokeGlyphs(Pen, Font, Size, Glyphs, Options);
            }
            else
            {
                canvas.FillGlyphs(Brush, Font, Size, Glyphs, Options);
            }
        }
    }
}
