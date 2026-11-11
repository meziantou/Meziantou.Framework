using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Pdf.VectorGraphics.Internal;

/// <summary>Writes the primitives of a <see cref="Scene"/> in the content of a PDF page.</summary>
/// <remarks>
/// The drawing is placed by a transform that flips the y axis, so the coordinates of the scene are written as they are.
/// The writer keeps the transform of the current content stream (<see cref="_matrix"/>) because the patterns of the
/// gradients are placed in the space of the stream, not in the current user space (ISO 32000-2, 8.7.2).
/// </remarks>
internal sealed class ScenePdfWriter(PdfPage page, PdfContentBuilder content, Scene scene, SceneFontCollection? fonts)
{
    // The miter limit of SVG (the default of PDF is 10)
    private const double MiterLimit = 4;

    private static readonly ConditionalWeakTable<SceneFontFace, EmbeddedFont> EmbeddedFonts = [];

    private readonly Stack<PdfMatrix> _saved = new();
    private PdfContentBuilder _content = content;

    // The transform from the current user space to the space of the current content stream, and from that space to the page
    private PdfMatrix _matrix = PdfMatrix.Identity;
    private PdfMatrix _streamToPage = PdfMatrix.Identity;

    public void Draw(PdfPoint position, double scale)
    {
        Save();
        Transform(new PdfMatrix(scale, 0, 0, -scale, position.X, position.Y));
        if (scene.Background is { A: > 0 } background && IsFinite(scene.Width, scene.Height))
        {
            Save();
            SetAlpha(background.A / 255.0, strokeAlpha: 1);
            _content.SetFillColor(ToColor(background)).Rectangle(0, 0, scene.Width, scene.Height).Fill();
            Restore();
        }

        foreach (var child in scene.Root.Children)
        {
            DrawElement(child);
        }

        Restore();
    }

    private void DrawElement(SceneElement element)
    {
        var opacity = element.Opacity;
        if (!(opacity > 0))
            return;

        opacity = Math.Min(opacity, 1);
        switch (element)
        {
            case SceneGroup group:
                DrawGroup(group, opacity);
                break;

            case SceneRectangle rectangle when rectangle.Width > 0 && rectangle.Height > 0:
                DrawShape(rectangle, new PathBuilder().AddRoundedRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, rectangle.CornerRadius).Build(), opacity, PdfFillRule.NonZero, canFill: true);
                break;

            case SceneCircle circle when circle.Radius > 0:
                DrawShape(circle, new PathBuilder().AddEllipse(circle.CenterX, circle.CenterY, circle.Radius, circle.Radius).Build(), opacity, PdfFillRule.NonZero, canFill: true);
                break;

            case SceneLine line:
                DrawShape(line, new PathBuilder().MoveTo(line.X1, line.Y1).LineTo(line.X2, line.Y2).Build(), opacity, PdfFillRule.NonZero, canFill: false);
                break;

            case ScenePath path:
                DrawShape(path, path.Data.ToCubicCurves(), opacity, path.FillRule is SceneFillRule.EvenOdd ? PdfFillRule.EvenOdd : PdfFillRule.NonZero, canFill: true);
                break;

            case SceneText text:
                DrawText(text, opacity);
                break;
        }
    }

    private void DrawGroup(SceneGroup group, double opacity)
    {
        if (group.Children.Count is 0)
            return;

        if (group.Clip is { } clip && !IsFinite(clip.X, clip.Y, clip.Width, clip.Height))
            return;

        if (group.Transform is { } transform && !IsFinite(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F))
            return;

        if (group.Hyperlink is { } hyperlink && Uri.TryCreate(hyperlink, UriKind.Absolute, out var uri) && Bounds.Of(group, this) is { } linkBounds)
        {
            var area = linkBounds.Transform(PdfMatrix.Multiply(_matrix, _streamToPage));
            if (area.IsFinite && area.Width > 0 && area.Height > 0)
            {
                page.Annotations.AddLink(new PdfRectangle(area.Left, area.Top, area.Right, area.Bottom), uri);
            }
        }

        Save();
        if (group.Clip is { } groupClip)
        {
            _content.Rectangle(groupClip.X, groupClip.Y, groupClip.Width, groupClip.Height).Clip().EndPath();
        }

        if (group.Transform is { } groupTransform)
        {
            Transform(new PdfMatrix(groupTransform.A, groupTransform.B, groupTransform.C, groupTransform.D, groupTransform.E, groupTransform.F));
        }

        if (opacity < 1)
        {
            DrawTransparencyGroup(group.Children, opacity);
        }
        else
        {
            foreach (var child in group.Children)
            {
                DrawElement(child);
            }
        }

        Restore();
    }

    // The opacity of a group applies to the group as a whole: its children are drawn in a transparency group (11.4.7)
    private void DrawTransparencyGroup(IList<SceneElement> children, double opacity)
    {
        Bounds? bounds = null;
        foreach (var child in children)
        {
            bounds = Bounds.Union(bounds, Bounds.Of(child, this));
        }

        if (bounds is not { IsFinite: true, Width: > 0, Height: > 0 } box)
            return;

        var form = page.Document.CreateForm(new PdfRectangle(box.Left, box.Top, box.Right, box.Bottom), matrix: null, new PdfTransparencyGroup());
        var (outerContent, outerMatrix, outerStreamToPage) = (_content, _matrix, _streamToPage);
        _streamToPage = PdfMatrix.Multiply(_matrix, _streamToPage);
        _matrix = PdfMatrix.Identity;
        _content = form.CreateContentBuilder();
        foreach (var child in children)
        {
            DrawElement(child);
        }

        _content.Complete();
        (_content, _matrix, _streamToPage) = (outerContent, outerMatrix, outerStreamToPage);
        SetAlpha(opacity, opacity);
        _content.DrawForm(form);
    }

    private void DrawShape(SceneShape shape, PathData path, double opacity, PdfFillRule fillRule, bool canFill)
    {
        var fill = canFill ? shape.Fill : null;
        var stroke = shape.StrokeWidth > 0 && double.IsFinite(shape.StrokeWidth) ? shape.Stroke : null;
        if ((fill is null && stroke is null) || path.IsEmpty || !IsFinite(path))
            return;

        Save();
        var fillAlpha = fill is null ? null : SetPaint(fill, stroke: false);
        var strokeAlpha = stroke is null ? null : SetPaint(stroke, stroke: true);
        if (fillAlpha is null && strokeAlpha is null)
        {
            Restore();
            return;
        }

        SetAlpha((fillAlpha ?? 1) * opacity, (strokeAlpha ?? 1) * opacity);
        if (strokeAlpha is not null)
        {
            _content.SetLineWidth(shape.StrokeWidth)
                .SetLineCap(shape.LineCap switch
                {
                    SceneLineCap.Round => PdfLineCap.Round,
                    SceneLineCap.Square => PdfLineCap.ProjectingSquare,
                    _ => PdfLineCap.Butt,
                })
                .SetLineJoin(shape.LineJoin switch
                {
                    SceneLineJoin.Round => PdfLineJoin.Round,
                    SceneLineJoin.Bevel => PdfLineJoin.Bevel,
                    _ => PdfLineJoin.Miter,
                })
                .SetMiterLimit(MiterLimit);

            // SVG draws a solid line when a length of the pattern is negative or when they are all 0
            if (shape.StrokeDashArray is { Count: > 0 } dashes && dashes.All(dash => dash >= 0 && double.IsFinite(dash)) && dashes.Sum() > 0)
            {
                // An odd number of lengths is repeated, as in SVG
                double[] pattern = dashes.Count % 2 == 0 ? [.. dashes] : [.. dashes, .. dashes];
                _content.SetDashPattern(pattern);
            }
        }

        AppendPath(path);
        if (fillAlpha is not null && strokeAlpha is not null)
        {
            _content.FillAndStroke(fillRule);
        }
        else if (fillAlpha is not null)
        {
            _content.Fill(fillRule);
        }
        else
        {
            _content.Stroke();
        }

        Restore();
    }

    private void AppendPath(PathData path)
    {
        var (startX, startY) = (0.0, 0.0);
        var hasCurrentPoint = false;
        foreach (var command in path.Commands)
        {
            if (command.Kind is not (PathCommandKind.MoveTo or PathCommandKind.Close) && !hasCurrentPoint)
            {
                // A segment after the end of a subpath starts a new one at the start of the closed subpath
                _content.MoveTo(startX, startY);
                hasCurrentPoint = true;
            }

            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    _content.MoveTo(command.X, command.Y);
                    (startX, startY) = (command.X, command.Y);
                    hasCurrentPoint = true;
                    break;

                case PathCommandKind.LineTo:
                    _content.LineTo(command.X, command.Y);
                    break;

                case PathCommandKind.CubicTo:
                    _content.CurveTo(command.X1, command.Y1, command.X2, command.Y2, command.X, command.Y);
                    break;

                case PathCommandKind.Close when hasCurrentPoint:
                    _content.ClosePath();
                    hasCurrentPoint = false;
                    break;
            }
        }
    }

    // Sets a paint and returns its opacity, or null when it paints nothing
    private double? SetPaint(ScenePaint paint, bool stroke)
    {
        switch (paint)
        {
            case SolidPaint solid:
                SetColor(solid.Color, stroke);
                return solid.Color.A / 255.0;

            case LinearGradientPaint gradient:
                var stops = GetStops(gradient);
                if (stops.Count is 0)
                    return null;

                var opacity = stops.Average(stop => stop.Color.A / 255.0);
                if (stops.Count is 1 || (gradient.X1 == gradient.X2 && gradient.Y1 == gradient.Y2) || !IsFinite(gradient.X1, gradient.Y1, gradient.X2, gradient.Y2))
                {
                    // SVG paints a gradient of one stop, or of no length, with the color of its last stop
                    SetColor(stops[^1].Color, stroke);
                    return opacity;
                }

                var function = PdfFunction.Gradient([.. stops.Select(stop => (stop.Offset, ToColor(stop.Color)))]);
                var shading = PdfShading.Axial(PdfDeviceColorSpace.Rgb, gradient.X1, gradient.Y1, gradient.X2, gradient.Y2, function, extendStart: true, extendEnd: true);
                var pattern = new PdfShadingPattern(shading, _matrix);
                if (stroke)
                {
                    _content.SetStrokePattern(pattern);
                }
                else
                {
                    _content.SetFillPattern(pattern);
                }

                return opacity;

            default:
                return null;
        }
    }

    // The stops as SVG reads them: the offsets clamped to [0, 1] and never less than the one before. PDF needs
    // increasing bounds from 0 to 1: the stops of a hard edge are moved apart a little, and the ends are added.
    private static List<(double Offset, SceneColor Color)> GetStops(LinearGradientPaint gradient)
    {
        var stops = new List<(double Offset, SceneColor Color)>();
        foreach (var stop in gradient.Stops)
        {
            var offset = double.IsNaN(stop.Offset) ? 0 : Math.Clamp(stop.Offset, 0, 1);
            if (stops.Count > 0 && offset <= stops[^1].Offset)
            {
                offset = stops[^1].Offset + 1e-6;
            }

            stops.Add((offset, stop.Color));
        }

        if (stops.Count < 2)
            return stops;

        if (stops[0].Offset > 0)
        {
            stops.Insert(0, (0, stops[0].Color));
        }

        if (stops[^1].Offset < 1)
        {
            stops.Add((1, stops[^1].Color));
        }

        return stops;
    }

    private void DrawText(SceneText text, double opacity)
    {
        var size = text.Font.Size;
        var alpha = text.Color.A / 255.0 * opacity;
        if (text.Text.Length is 0 || !(size > 0) || !(alpha > 0) || !IsFinite(text.X, text.Y, size, text.Rotation))
            return;

        var runs = GetTextRuns(text);
        if (runs.Count is 0)
            return;

        var width = runs.Sum(run => run.Width);
        var startX = text.Anchor switch
        {
            SceneTextAnchor.Middle => text.X - (width / 2),
            SceneTextAnchor.End => text.X - width,
            _ => text.X,
        };

        // The glyphs are drawn upright in the flipped space, and rotated clockwise around the anchor
        var rotation = SceneTransform.Rotate(text.Rotation, text.X, text.Y);
        var matrix = PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, -1, startX, text.Y), new PdfMatrix(rotation.A, rotation.B, rotation.C, rotation.D, rotation.E, rotation.F));
        Save();
        SetColor(text.Color, stroke: false);
        SetAlpha(alpha, strokeAlpha: 1);
        _content.BeginText().SetTextMatrix(matrix);
        foreach (var run in runs)
        {
            _content.SetFont(run.Font, size).ShowText(run.Text);
        }

        _content.EndText();
        Restore();
    }

    /// <summary>Splits a text into the parts that one font draws, with their widths.</summary>
    internal List<(PdfFont Font, string Text, double Width)> GetTextRuns(SceneText text)
    {
        var runs = new List<(PdfFont Font, string Text, double Width)>();
        var standard = GetStandardFont(text.Font);
        if (fonts is null)
        {
            Add(standard, text.Text);
            return runs;
        }

        foreach (var run in fonts.GetRuns(text.Text, text.Font, scene.FontFamily))
        {
            Add(GetEmbeddedFont(run.Face) ?? standard, text.Text.Substring(run.Start, run.Length));
        }

        return runs;

        void Add(PdfFont font, string value)
        {
            value = Sanitize(font, value);
            if (value.Length > 0)
            {
                runs.Add((font, value, font.GetTextWidth(value, text.Font.Size)));
            }
        }
    }

    // A grapheme cluster the font cannot show is drawn as a question mark
    private static string Sanitize(PdfFont font, string text)
    {
        if (font.CanEncode(text))
            return text;

        var replacement = font.CanEncode("?") ? "?" : "";
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var range in Unicode.EnumerateGraphemeClusters(text))
        {
            var cluster = text[range];
            builder.Append(font.CanEncode(cluster) ? cluster : replacement);
        }

        return builder.ToString();
    }

    private static PdfFont GetStandardFont(SceneFont font)
    {
        return PdfFont.GetStandardFont((font.Bold, font.Italic) switch
        {
            (true, true) => PdfStandardFont.HelveticaBoldOblique,
            (true, false) => PdfStandardFont.HelveticaBold,
            (false, true) => PdfStandardFont.HelveticaOblique,
            _ => PdfStandardFont.Helvetica,
        });
    }

    // A face that cannot be embedded (bitmap or CFF2 outlines, a license that forbids it) is replaced by Helvetica
    private static PdfFont? GetEmbeddedFont(SceneFontFace face)
    {
        return EmbeddedFonts.GetValue(face, static face =>
        {
            try
            {
                return new EmbeddedFont(PdfFont.Load(face.Data.Span, face.Index));
            }
            catch (Exception exception) when (exception is FontException or UnsupportedPdfFeatureException or ArgumentOutOfRangeException)
            {
                return new EmbeddedFont(null);
            }
        }).Font;
    }

    private void SetColor(SceneColor color, bool stroke)
    {
        if (stroke)
        {
            _content.SetStrokeColor(ToColor(color));
        }
        else
        {
            _content.SetFillColor(ToColor(color));
        }
    }

    private void SetAlpha(double fillAlpha, double strokeAlpha)
    {
        if (fillAlpha < 1 || strokeAlpha < 1)
        {
            _content.SetGraphicsState(new PdfExtGState { FillAlpha = fillAlpha, StrokeAlpha = strokeAlpha });
        }
    }

    private void Save()
    {
        _content.SaveGraphicsState();
        _saved.Push(_matrix);
    }

    private void Restore()
    {
        _content.RestoreGraphicsState();
        _matrix = _saved.Pop();
    }

    private void Transform(PdfMatrix matrix)
    {
        _content.Transform(matrix);
        _matrix = PdfMatrix.Multiply(matrix, _matrix);
    }

    private static PdfColor ToColor(SceneColor color) => PdfColor.FromRgb24(color.R, color.G, color.B);

    private static bool IsFinite(params ReadOnlySpan<double> values)
    {
        foreach (var value in values)
        {
            if (!double.IsFinite(value))
                return false;
        }

        return true;
    }

    private static bool IsFinite(PathData path) => path.Commands.All(command => IsFinite(command.X, command.Y, command.X1, command.Y1, command.X2, command.Y2));

    private sealed record EmbeddedFont(PdfFont? Font);

    /// <summary>A rectangle aligned with the axes, to place the links and the transparency groups.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;

        public double Height => Bottom - Top;

        public bool IsFinite => ScenePdfWriter.IsFinite(Left, Top, Right, Bottom);

        public static Bounds? Union(Bounds? first, Bounds? second)
        {
            if (first is not { } a)
                return second;

            if (second is not { } b)
                return a;

            return new Bounds(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
        }

        public Bounds Transform(PdfMatrix matrix)
        {
            var (x1, y1) = matrix.Transform(Left, Top);
            var (x2, y2) = matrix.Transform(Right, Top);
            var (x3, y3) = matrix.Transform(Left, Bottom);
            var (x4, y4) = matrix.Transform(Right, Bottom);
            return new Bounds(Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)), Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)), Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)), Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
        }

        private Bounds Inflate(double amount) => amount > 0 ? new Bounds(Left - amount, Top - amount, Right + amount, Bottom + amount) : this;

        /// <summary>Gets a rectangle that contains what an element draws, in the coordinates of its parent, or null when it draws nothing.</summary>
        public static Bounds? Of(SceneElement element, ScenePdfWriter writer)
        {
            if (!(element.Opacity > 0))
                return null;

            // The strokes extend by half their width, and the miters by twice it at most (the miter limit is 4)
            var stroke = element is SceneShape { Stroke: not null } shape && shape.StrokeWidth > 0 ? shape.StrokeWidth * 2 : 0;
            switch (element)
            {
                case SceneGroup group:
                    Bounds? children = null;
                    foreach (var child in group.Children)
                    {
                        children = Union(children, Of(child, writer));
                    }

                    if (children is not { } box)
                        return null;

                    if (group.Transform is { } transform)
                    {
                        box = box.Transform(new PdfMatrix(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F));
                    }

                    if (group.Clip is { } clip)
                    {
                        box = new Bounds(Math.Max(box.Left, clip.X), Math.Max(box.Top, clip.Y), Math.Min(box.Right, clip.Right), Math.Min(box.Bottom, clip.Bottom));
                        if (!(box.Width > 0) || !(box.Height > 0))
                            return null;
                    }

                    return box;

                case SceneRectangle rectangle:
                    return new Bounds(rectangle.X, rectangle.Y, rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height).Inflate(stroke);

                case SceneCircle circle:
                    return new Bounds(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius, circle.CenterX + circle.Radius, circle.CenterY + circle.Radius).Inflate(stroke);

                case SceneLine line:
                    return new Bounds(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Max(line.X1, line.X2), Math.Max(line.Y1, line.Y2)).Inflate(stroke);

                case ScenePath path:
                    // A cubic curve stays in the convex hull of its control points
                    Bounds? points = null;
                    foreach (var command in path.Data.ToCubicCurves().Commands)
                    {
                        if (command.Kind is PathCommandKind.Close)
                            continue;

                        points = Union(points, new Bounds(command.X, command.Y, command.X, command.Y));
                        if (command.Kind is PathCommandKind.CubicTo)
                        {
                            points = Union(points, new Bounds(Math.Min(command.X1, command.X2), Math.Min(command.Y1, command.Y2), Math.Max(command.X1, command.X2), Math.Max(command.Y1, command.Y2)));
                        }
                    }

                    return points?.Inflate(stroke);

                case SceneText text when text.Text.Length > 0 && text.Font.Size > 0:
                    // The glyphs fit in an em above the baseline and half an em below it, from the start to the end of the text
                    var width = writer.GetTextRuns(text).Sum(run => run.Width);
                    var start = text.Anchor switch
                    {
                        SceneTextAnchor.Middle => text.X - (width / 2),
                        SceneTextAnchor.End => text.X - width,
                        _ => text.X,
                    };
                    var rotation = SceneTransform.Rotate(text.Rotation, text.X, text.Y);
                    return new Bounds(start, text.Y - text.Font.Size, start + width, text.Y + (text.Font.Size / 2))
                        .Transform(new PdfMatrix(rotation.A, rotation.B, rotation.C, rotation.D, rotation.E, rotation.F));

                default:
                    return null;
            }
        }
    }
}
