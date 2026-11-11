using System.Diagnostics;
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
internal sealed class ScenePdfWriter(PdfPage page, PdfContentBuilder content, Scene scene, SceneFontCollection? fonts, PdfStructureElement? figure)
{
    // The miter limit of SVG (the default of PDF is 10)
    private const double DefaultMiterLimit = 4;

    // The decorations of Helvetica, in em: its underline (Helvetica.afm: centered 100 units below the baseline, 50 units
    // thick), and the strikeout of Arial, which has its widths
    private const double HelveticaUnderlinePosition = -0.075;
    private const double HelveticaStrikeoutPosition = 0.3;
    private const double HelveticaLineThickness = 0.05;

    private static readonly ConditionalWeakTable<SceneFontFace, EmbeddedFont> EmbeddedFonts = [];
    private static readonly ConditionalWeakTable<SceneImage, LoadedImage> Images = [];

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

    /// <summary>Throws when a scene has what PDF cannot draw: a filter.</summary>
    /// <exception cref="NotSupportedException">An element has a filter.</exception>
    public static void ThrowIfUnsupported(Scene scene)
    {
        Visit(scene.Root);

        static void Visit(SceneElement element)
        {
            if (element.Filter is not null)
                throw new NotSupportedException($"PDF has no filters: the {element.GetType().Name} has a {element.Filter.GetType().Name}, which cannot be drawn as vectors. Remove the filters of the scene, or draw it as an image.");

            if (element.Mask is { } mask)
            {
                Visit(mask.Content);
            }

            if (element is SceneGroup group)
            {
                foreach (var child in group.Children)
                {
                    Visit(child);
                }
            }
        }
    }

    private void DrawElement(SceneElement element)
    {
        var opacity = element.Opacity;
        if (!(opacity > 0))
            return;

        opacity = Math.Min(opacity, 1);

        // A mask and a blend mode are set in the graphics state of the element (11.6.4.3, 11.3.5)
        var hasState = element.Mask is not null || element.BlendMode is not SceneBlendMode.Normal;
        if (hasState)
        {
            Save();
            if (element.Mask is { } mask && !SetMask(mask))
            {
                Restore();
                return;
            }

            if (element.BlendMode is not SceneBlendMode.Normal)
            {
                _content.SetGraphicsState(new PdfExtGState { BlendMode = ToBlendMode(element.BlendMode) });
            }
        }

        DrawElementContent(element, opacity, isolated: hasState);
        if (hasState)
        {
            Restore();
        }
    }

    private void DrawElementContent(SceneElement element, double opacity, bool isolated)
    {
        switch (element)
        {
            case SceneGroup group:
                DrawGroup(group, opacity, isolated);
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

            case SceneImage image:
                DrawImage(image, opacity);
                break;
        }
    }

    private void DrawImage(SceneImage image, double opacity)
    {
        if (!(image.Width > 0) || !(image.Height > 0) || !IsFinite(image.X, image.Y, image.Width, image.Height))
            return;

        if (Images.GetValue(image, static image => LoadImage(image)).Image is not { } pdfImage)
            return;

        // The image space has its first row at the top: in the flipped space, the image is placed from its bottom edge upward
        Save();
        SetAlpha(opacity, opacity);
        _content.DrawImage(pdfImage, image.X, image.Y + image.Height, image.Width, -image.Height);
        Restore();
    }

    // An image that cannot be decoded is not drawn
    private static LoadedImage LoadImage(SceneImage image)
    {
        try
        {
            return new LoadedImage(PdfImage.Load(image.Data.Span));
        }
        catch (Exception exception) when (exception is ArgumentException or Imaging.ImageException)
        {
            return new LoadedImage(null);
        }
    }

    private void DrawGroup(SceneGroup group, double opacity, bool isolated)
    {
        if (group.Children.Count is 0)
            return;

        if (group.Clip is { } clip && !IsFinite(clip.X, clip.Y, clip.Width, clip.Height))
            return;

        if (group.Transform is { } transform && !IsFinite(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F))
            return;

        // An empty clip path leaves nothing visible, as in SVG
        var clipPath = group.ClipPath?.ToCubicCurves();
        if (clipPath is not null && (clipPath.IsEmpty || !IsFinite(clipPath)))
            return;

        if (group.Hyperlink is { } hyperlink && Uri.TryCreate(hyperlink, UriKind.Absolute, out var uri) && Bounds.Of(group, this) is { } linkBounds)
        {
            var area = linkBounds.Transform(PdfMatrix.Multiply(_matrix, _streamToPage));
            if (area.IsFinite && area.Width > 0 && area.Height > 0)
            {
                var link = page.Annotations.AddLink(new PdfRectangle(area.Left, area.Top, area.Right, area.Bottom), uri);

                // In a tagged document, a link is a Link element of the figure (14.8.4.4.2)
                figure?.AddElement("Link").AddAnnotation(link);
            }
        }

        Save();
        if (group.Clip is { } groupClip)
        {
            _content.Rectangle(groupClip.X, groupClip.Y, groupClip.Width, groupClip.Height).Clip().EndPath();
        }

        if (clipPath is not null)
        {
            AppendPath(clipPath);
            _content.Clip(group.ClipRule is SceneFillRule.EvenOdd ? PdfFillRule.EvenOdd : PdfFillRule.NonZero).EndPath();
        }

        if (group.Transform is { } groupTransform)
        {
            Transform(new PdfMatrix(groupTransform.A, groupTransform.B, groupTransform.C, groupTransform.D, groupTransform.E, groupTransform.F));
        }

        // The opacity, the mask and the blend mode apply to the group as a whole: it is drawn in a transparency group
        if (opacity < 1 || isolated)
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

        var form = CreateForm(children, box, new PdfTransparencyGroup());
        SetAlpha(opacity, opacity);
        _content.DrawForm(form);
    }

    // Draws elements in a form whose space is the current user space
    private PdfForm CreateForm(IEnumerable<SceneElement> elements, Bounds box, PdfTransparencyGroup group)
    {
        var form = page.Document.CreateForm(new PdfRectangle(box.Left, box.Top, box.Right, box.Bottom), matrix: null, group);
        var (outerContent, outerMatrix, outerStreamToPage) = (_content, _matrix, _streamToPage);
        _streamToPage = PdfMatrix.Multiply(_matrix, _streamToPage);
        _matrix = PdfMatrix.Identity;
        _content = form.CreateContentBuilder();
        foreach (var element in elements)
        {
            DrawElement(element);
        }

        _content.Complete();
        (_content, _matrix, _streamToPage) = (outerContent, outerMatrix, outerStreamToPage);
        return form;
    }

    // The content of a mask is drawn in a group whose luminosity or alpha is the soft mask: outside of it, the
    // luminosity of the black backdrop and the alpha are 0, so nothing is drawn. Returns false when the mask shows nothing.
    private bool SetMask(SceneMask mask)
    {
        if (Bounds.Of(mask.Content, this) is not { IsFinite: true, Width: > 0, Height: > 0 } box)
            return false;

        var form = CreateForm([mask.Content], box, new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Rgb });
        _content.SetGraphicsState(new PdfExtGState { SoftMask = mask.Mode is SceneMaskMode.Alpha ? PdfSoftMask.FromAlpha(form) : PdfSoftMask.FromLuminosity(form) });
        return true;
    }

    private static PdfBlendMode ToBlendMode(SceneBlendMode mode) => mode switch
    {
        SceneBlendMode.Multiply => PdfBlendMode.Multiply,
        SceneBlendMode.Screen => PdfBlendMode.Screen,
        SceneBlendMode.Overlay => PdfBlendMode.Overlay,
        SceneBlendMode.Darken => PdfBlendMode.Darken,
        SceneBlendMode.Lighten => PdfBlendMode.Lighten,
        SceneBlendMode.ColorDodge => PdfBlendMode.ColorDodge,
        SceneBlendMode.ColorBurn => PdfBlendMode.ColorBurn,
        SceneBlendMode.HardLight => PdfBlendMode.HardLight,
        SceneBlendMode.SoftLight => PdfBlendMode.SoftLight,
        SceneBlendMode.Difference => PdfBlendMode.Difference,
        SceneBlendMode.Exclusion => PdfBlendMode.Exclusion,
        SceneBlendMode.Hue => PdfBlendMode.Hue,
        SceneBlendMode.Saturation => PdfBlendMode.Saturation,
        SceneBlendMode.Color => PdfBlendMode.Color,
        SceneBlendMode.Luminosity => PdfBlendMode.Luminosity,
        _ => PdfBlendMode.Normal,
    };

    private void DrawShape(SceneShape shape, PathData path, double opacity, PdfFillRule fillRule, bool canFill)
    {
        var fill = canFill ? shape.Fill : null;
        var stroke = shape.StrokeWidth > 0 && double.IsFinite(shape.StrokeWidth) ? shape.Stroke : null;
        if ((fill is null && stroke is null) || path.IsEmpty || !IsFinite(path))
            return;

        Save();
        var fillAlpha = fill is null ? null : SetPaint(fill, stroke: false, shape);
        var strokeAlpha = stroke is null ? null : SetPaint(stroke, stroke: true, shape);
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
                .SetMiterLimit(shape.MiterLimit >= 1 && double.IsFinite(shape.MiterLimit) ? shape.MiterLimit : DefaultMiterLimit);

            // SVG draws a solid line when a length of the pattern is negative or when they are all 0
            if (shape.StrokeDashArray is { Count: > 0 } dashes && dashes.All(dash => dash >= 0 && double.IsFinite(dash)) && dashes.Sum() > 0)
            {
                // An odd number of lengths is repeated, as in SVG
                double[] pattern = dashes.Count % 2 == 0 ? [.. dashes] : [.. dashes, .. dashes];

                // The phase is brought into the pattern: a negative offset starts before it
                var offset = double.IsFinite(shape.StrokeDashOffset) ? shape.StrokeDashOffset : 0;
                var phase = offset % pattern.Sum();
                _content.SetDashPattern(pattern, phase < 0 ? phase + pattern.Sum() : phase);
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
    private double? SetPaint(ScenePaint paint, bool stroke, SceneShape shape)
    {
        switch (paint)
        {
            case SolidPaint solid:
                SetColor(solid.Color, stroke);
                return solid.Color.A / 255.0;

            case SceneGradientPaint gradient:
                return SetGradient(gradient, stroke, shape);

            default:
                return null;
        }
    }

    private double? SetGradient(SceneGradientPaint gradient, bool stroke, SceneShape shape)
    {
        var stops = GetStops(gradient.Stops);
        if (stops.Count is 0)
            return null;

        var opacity = stops.Average(stop => stop.Color.A / 255.0);
        var transform = gradient.Transform is { } value ? new PdfMatrix(value.A, value.B, value.C, value.D, value.E, value.F) : PdfMatrix.Identity;
        if (!IsFinite(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F) || !transform.TryInvert(out var inverse))
            return null;

        var degenerate = stops.Count is 1 || gradient switch
        {
            LinearGradientPaint linear => (linear.X1 == linear.X2 && linear.Y1 == linear.Y2) || !IsFinite(linear.X1, linear.Y1, linear.X2, linear.Y2),
            RadialGradientPaint radial => !(radial.Radius > 0) || !IsFinite(radial.CenterX, radial.CenterY, radial.Radius, radial.FocalX, radial.FocalY),
            _ => true,
        };

        if (degenerate)
        {
            // SVG paints a gradient of one stop, or of no length or radius, with the color of its last stop
            SetColor(stops[^1].Color, stroke);
            return opacity;
        }

        // Repeat and reflect: the stops are repeated over the periods the shape covers, in the space of the gradient
        var (first, last) = (0, 1);
        if (gradient.Spread is not SceneGradientSpread.Pad && Bounds.Of(shape, this)?.Transform(inverse) is { IsFinite: true } box)
        {
            var range = GetRange(gradient, box);
            if (range is { } periods)
            {
                (first, last) = periods;
                stops = Repeat(stops, first, last, gradient.Spread is SceneGradientSpread.Reflect);
            }
        }

        var function = PdfFunction.Gradient([.. stops.Select(stop => (stop.Offset, ToColor(stop.Color)))]);
        var shading = gradient switch
        {
            LinearGradientPaint linear => PdfShading.Axial(
                PdfDeviceColorSpace.Rgb,
                linear.X1 + (first * (linear.X2 - linear.X1)), linear.Y1 + (first * (linear.Y2 - linear.Y1)),
                linear.X1 + (last * (linear.X2 - linear.X1)), linear.Y1 + (last * (linear.Y2 - linear.Y1)),
                function, extendStart: true, extendEnd: true),

            // The circles go from the focal point, of radius 0, to the circle of the gradient (8.7.4.5.4, type 3)
            RadialGradientPaint radial => PdfShading.Radial(
                PdfDeviceColorSpace.Rgb,
                radial.FocalX, radial.FocalY, 0,
                radial.FocalX + (last * (radial.CenterX - radial.FocalX)), radial.FocalY + (last * (radial.CenterY - radial.FocalY)), last * radial.Radius,
                function, extendStart: true, extendEnd: true),
            _ => throw new UnreachableException(),
        };

        SetPattern(new PdfShadingPattern(shading, PdfMatrix.Multiply(transform, _matrix)), stroke);
        return opacity;
    }

    // The most periods a repeated gradient is drawn with; beyond them, it is padded
    private const int MaxPeriods = 256;

    // The periods of the gradient the corners of a rectangle reach: a linear gradient is 0 at its start and 1 at its
    // end, a radial gradient 0 at its focal point and 1 on its circle. A radial gradient whose focal point is not
    // inside its circle is padded.
    private static (int First, int Last)? GetRange(SceneGradientPaint gradient, Bounds box)
    {
        ReadOnlySpan<(double X, double Y)> corners = [(box.Left, box.Top), (box.Right, box.Top), (box.Left, box.Bottom), (box.Right, box.Bottom)];
        var (min, max) = (double.PositiveInfinity, double.NegativeInfinity);
        foreach (var (x, y) in corners)
        {
            double t;
            if (gradient is LinearGradientPaint linear)
            {
                var (dx, dy) = (linear.X2 - linear.X1, linear.Y2 - linear.Y1);
                t = (((x - linear.X1) * dx) + ((y - linear.Y1) * dy)) / ((dx * dx) + (dy * dy));
            }
            else
            {
                // The circle of a parameter t is centered t of the way from the focal point to the center, of radius t r:
                // the parameter of a point is the positive root of |q - t d|^2 = (t r)^2
                var radial = (RadialGradientPaint)gradient;
                var (dx, dy) = (radial.CenterX - radial.FocalX, radial.CenterY - radial.FocalY);
                var (qx, qy) = (x - radial.FocalX, y - radial.FocalY);
                var a = (dx * dx) + (dy * dy) - (radial.Radius * radial.Radius);
                if (a >= 0)
                    return null;

                var b = -2 * ((qx * dx) + (qy * dy));
                var c = (qx * qx) + (qy * qy);
                t = (-b - Math.Sqrt((b * b) - (4 * a * c))) / (2 * a);
            }

            (min, max) = (Math.Min(min, t), Math.Max(max, t));
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
            return null;

        var first = gradient is RadialGradientPaint ? 0 : (int)Math.Clamp(Math.Floor(min), -MaxPeriods, MaxPeriods);
        var last = (int)Math.Clamp(Math.Ceiling(max), first + 1, first + MaxPeriods);
        return (first, last);
    }

    // The stops of the periods from first to last, their offsets brought back to [0, 1]
    private static List<(double Offset, SceneColor Color)> Repeat(List<(double Offset, SceneColor Color)> stops, int first, int last, bool reflect)
    {
        var repeated = new List<(double Offset, SceneColor Color)>((last - first) * stops.Count);
        for (var period = first; period < last; period++)
        {
            var reversed = reflect && (period & 1) != 0;
            for (var i = 0; i < stops.Count; i++)
            {
                var stop = reversed ? stops[^(i + 1)] : stops[i];
                var offset = (period + (reversed ? 1 - stop.Offset : stop.Offset) - first) / (double)(last - first);
                if (repeated.Count > 0 && offset <= repeated[^1].Offset)
                {
                    offset = repeated[^1].Offset + 1e-9;
                }

                repeated.Add((offset, stop.Color));
            }
        }

        return repeated;
    }

    private void SetPattern(PdfPattern pattern, bool stroke)
    {
        if (stroke)
        {
            _content.SetStrokePattern(pattern);
        }
        else
        {
            _content.SetFillPattern(pattern);
        }
    }

    private static List<(double Offset, SceneColor Color)> GetStops(IReadOnlyList<SceneGradientStop> gradientStops)
    {
        var stops = new List<(double Offset, SceneColor Color)>();
        foreach (var stop in gradientStops)
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
        if (text.Text.Length is 0 || !(opacity > 0) || !IsFinite(text.X, text.Y, text.Rotation))
            return;

        var textParts = GetTextParts(text);
        if (textParts.Count is 0)
            return;

        var width = textParts.Sum(part => part.Width);
        var startX = text.Anchor switch
        {
            SceneTextAnchor.Middle => text.X - (width / 2),
            SceneTextAnchor.End => text.X - width,
            _ => text.X,
        };

        // The glyphs are drawn upright in the flipped space, and rotated clockwise around the anchor. Each part (a span,
        // or the characters of a span that a face draws) is a text object of its own, with its color and its opacity.
        var rotation = SceneTransform.Rotate(text.Rotation, text.X, text.Y);
        var rotationMatrix = new PdfMatrix(rotation.A, rotation.B, rotation.C, rotation.D, rotation.E, rotation.F);
        var offset = 0.0;
        foreach (var part in textParts)
        {
            var alpha = part.Color.A / 255.0 * opacity;
            if (alpha > 0)
            {
                Save();
                SetColor(part.Color, stroke: false);
                SetAlpha(alpha, strokeAlpha: 1);
                _content.BeginText().SetTextMatrix(PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, -1, startX + offset, text.Y), rotationMatrix));
                _content.SetFont(part.Font, part.Size).ShowText(part.Text);
                _content.EndText();
                DrawDecorations(part, startX + offset, text.Y, rotationMatrix);
                Restore();
            }

            offset += part.Width;
        }
    }

    // The lines of a part are rectangles of its color, at the position and of the thickness the face of its span gives
    private void DrawDecorations(TextPart part, double x, double baseline, PdfMatrix rotation)
    {
        if ((part.Decorations & (SceneTextDecorations.Underline | SceneTextDecorations.Strikethrough)) is SceneTextDecorations.None || !(part.Width > 0))
            return;

        Save();
        Transform(rotation);
        if (part.Decorations.HasFlag(SceneTextDecorations.Underline))
        {
            var top = part.Face?.UnderlinePosition ?? HelveticaUnderlinePosition;
            var thickness = part.Face?.UnderlineThickness ?? HelveticaLineThickness;
            _content.Rectangle(x, baseline - (top * part.Size), part.Width, thickness * part.Size).Fill();
        }

        if (part.Decorations.HasFlag(SceneTextDecorations.Strikethrough))
        {
            var top = part.Face?.StrikeoutPosition ?? HelveticaStrikeoutPosition;
            var thickness = part.Face?.StrikeoutThickness ?? HelveticaLineThickness;
            _content.Rectangle(x, baseline - (top * part.Size), part.Width, thickness * part.Size).Fill();
        }

        Restore();
    }

    /// <summary>A part of a text that one font draws, with its style and its width.</summary>
    internal sealed record TextPart(PdfFont Font, string Text, double Width, double Size, SceneColor Color, SceneTextDecorations Decorations, SceneFontFace? Face);

    /// <summary>Splits a text into the parts that one font draws: its spans, and the characters of a span that each face draws.</summary>
    internal List<TextPart> GetTextParts(SceneText text)
    {
        var parts = new List<TextPart>();
        if (text.Spans.Count is 0)
        {
            AddSpan(text.Text, text.Font, text.Color, text.Decorations);
        }
        else
        {
            foreach (var span in text.Spans)
            {
                AddSpan(span.Text, span.Font ?? text.Font, span.Color ?? text.Color, span.Decorations ?? text.Decorations);
            }
        }

        return parts;

        void AddSpan(string value, SceneFont font, SceneColor color, SceneTextDecorations decorations)
        {
            if (value.Length is 0 || !(font.Size > 0) || !double.IsFinite(font.Size))
                return;

            var standard = GetStandardFont(font);
            if (fonts is null)
            {
                Add(standard, value, null);
                return;
            }

            var face = fonts.GetFace(font, scene.FontFamily);
            foreach (var run in fonts.GetRuns(value, font, scene.FontFamily))
            {
                Add(GetEmbeddedFont(run.Face) ?? standard, value.Substring(run.Start, run.Length), face);
            }

            void Add(PdfFont pdfFont, string runText, SceneFontFace? spanFace)
            {
                runText = Sanitize(pdfFont, runText);
                if (runText.Length > 0)
                {
                    parts.Add(new TextPart(pdfFont, runText, pdfFont.GetTextWidth(runText, font.Size), font.Size, color, decorations, spanFace));
                }
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
        return PdfFont.GetStandardFont((font.IsBold, font.Italic) switch
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

    private sealed record LoadedImage(PdfImage? Image);

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

        // A cubic curve stays in the convex hull of its control points
        private static Bounds? OfPath(PathData data)
        {
            Bounds? points = null;
            foreach (var command in data.ToCubicCurves().Commands)
            {
                if (command.Kind is PathCommandKind.Close)
                    continue;

                points = Union(points, new Bounds(command.X, command.Y, command.X, command.Y));
                if (command.Kind is PathCommandKind.CubicTo)
                {
                    points = Union(points, new Bounds(Math.Min(command.X1, command.X2), Math.Min(command.Y1, command.Y2), Math.Max(command.X1, command.X2), Math.Max(command.Y1, command.Y2)));
                }
            }

            return points;
        }

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
                    }

                    if (group.ClipPath is { } clipPath)
                    {
                        if (OfPath(clipPath) is not { } clipBox)
                            return null;

                        box = new Bounds(Math.Max(box.Left, clipBox.Left), Math.Max(box.Top, clipBox.Top), Math.Min(box.Right, clipBox.Right), Math.Min(box.Bottom, clipBox.Bottom));
                    }

                    if (!(box.Width > 0) || !(box.Height > 0))
                        return null;

                    return box;

                case SceneImage image:
                    return new Bounds(image.X, image.Y, image.X + image.Width, image.Y + image.Height);

                case SceneRectangle rectangle:
                    return new Bounds(rectangle.X, rectangle.Y, rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height).Inflate(stroke);

                case SceneCircle circle:
                    return new Bounds(circle.CenterX - circle.Radius, circle.CenterY - circle.Radius, circle.CenterX + circle.Radius, circle.CenterY + circle.Radius).Inflate(stroke);

                case SceneLine line:
                    return new Bounds(Math.Min(line.X1, line.X2), Math.Min(line.Y1, line.Y2), Math.Max(line.X1, line.X2), Math.Max(line.Y1, line.Y2)).Inflate(stroke);

                case ScenePath path:
                    return OfPath(path.Data)?.Inflate(stroke);

                case SceneText text when text.Text.Length > 0:
                    // The glyphs fit in an em above the baseline and half an em below it, from the start to the end of the text
                    var parts = writer.GetTextParts(text);
                    var width = parts.Sum(part => part.Width);
                    var size = parts.Count is 0 ? text.Font.Size : parts.Max(part => part.Size);
                    var start = text.Anchor switch
                    {
                        SceneTextAnchor.Middle => text.X - (width / 2),
                        SceneTextAnchor.End => text.X - width,
                        _ => text.X,
                    };
                    var rotation = SceneTransform.Rotate(text.Rotation, text.X, text.Y);
                    return new Bounds(start, text.Y - size, start + width, text.Y + (size / 2))
                        .Transform(new PdfMatrix(rotation.A, rotation.B, rotation.C, rotation.D, rotation.E, rotation.F));

                default:
                    return null;
            }
        }
    }
}
