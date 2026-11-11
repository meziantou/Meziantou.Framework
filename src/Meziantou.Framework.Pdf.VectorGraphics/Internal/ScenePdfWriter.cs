using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf.Objects;
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
    // The depth of the contents that are not links of the page: masks, patterns and markers
    private int _hiddenDepth;

    // The tiling patterns, by paint and by the transform from their space to the space of their content stream; the
    // patterns whose content is being drawn
    private readonly Dictionary<(PatternPaint Paint, PdfMatrix Matrix), PdfTilingPattern> _tilingPatterns = [];
    private readonly HashSet<PatternPaint> _drawingPatterns = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SceneMarker> _drawingMarkers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SceneUse> _drawingUses = new(ReferenceEqualityComparer.Instance);
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
        var patterns = new HashSet<object>(ReferenceEqualityComparer.Instance);
        Visit(scene.Root);

        void Visit(SceneElement element)
        {
            if (element.Filter is not null)
                throw new NotSupportedException($"PDF has no filters: the {element.GetType().Name} has a {element.Filter.GetType().Name}, which cannot be drawn as vectors. Remove the filters of the scene, or draw it as an image.");

            if (element.Mask is { } mask)
            {
                Visit(mask.Content);
            }

            if (element is SceneShape shape)
            {
                VisitPattern(shape.Fill);
                VisitPattern(shape.Stroke);
            }

            if (element is SceneText text)
            {
                VisitPattern(text.Fill);
                VisitPattern(text.Stroke);
                VisitPattern(text.DecorationFill);
                foreach (var span in text.Spans)
                {
                    VisitPattern(span.Fill);
                    VisitPattern(span.Stroke);
                    VisitPattern(span.DecorationFill);
                }
            }

            if (element is SceneUse use && patterns.Add(use.Content))
            {
                Visit(use.Content);
            }

            switch (element)
            {
                case ScenePath path:
                    VisitMarker(path.MarkerStart);
                    VisitMarker(path.MarkerMid);
                    VisitMarker(path.MarkerEnd);
                    break;

                case SceneLine line:
                    VisitMarker(line.MarkerStart);
                    VisitMarker(line.MarkerEnd);
                    break;
            }

            if (element is SceneGroup group)
            {
                foreach (var child in group.Children)
                {
                    Visit(child);
                }
            }
        }

        void VisitPattern(ScenePaint? paint)
        {
            if (paint is PatternPaint pattern && patterns.Add(pattern))
            {
                Visit(pattern.Content);
            }
        }

        void VisitMarker(SceneMarker? marker)
        {
            if (marker is not null && patterns.Add(marker))
            {
                Visit(marker.Content);
            }
        }
    }

    private void DrawElement(SceneElement element)
    {
        var opacity = element.Opacity;
        if (!(opacity > 0))
            return;

        AddLink(element);

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

    // The link of an element is an annotation of the page over what it draws. The contents of a mask and of a pattern
    // are not elements of the page: their hyperlinks are not links of the page.
    private void AddLink(SceneElement element)
    {
        if (_hiddenDepth is not 0 || element.Hyperlink is not { } hyperlink || !Uri.TryCreate(hyperlink, UriKind.Absolute, out var uri) || Bounds.Of(element, this) is not { } linkBounds)
            return;

        var area = linkBounds.Transform(PdfMatrix.Multiply(_matrix, _streamToPage));
        if (area.IsFinite && area.Width > 0 && area.Height > 0)
        {
            var link = page.Annotations.AddLink(new PdfRectangle(area.Left, area.Top, area.Right, area.Bottom), uri);

            // In a tagged document, a link is a Link element of the figure (14.8.4.4.2)
            figure?.AddElement("Link").AddAnnotation(link);
        }
    }

    private void DrawElementContent(SceneElement element, double opacity, bool isolated)
    {
        // The transform of an element applies inside its mask, as the one of a group does
        if (element is not SceneGroup && element.Transform is { } transform)
        {
            if (!IsFinite(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F))
                return;

            Save();
            Transform(new PdfMatrix(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F));
            DrawUntransformedContent(element, opacity, isolated);
            Restore();
            return;
        }

        DrawUntransformedContent(element, opacity, isolated);
    }

    private void DrawUntransformedContent(SceneElement element, double opacity, bool isolated)
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
                DrawMarkers(line, opacity);
                break;

            case ScenePath path:
                DrawShape(path, path.Data.ToCubicCurves(), opacity, path.FillRule is SceneFillRule.EvenOdd ? PdfFillRule.EvenOdd : PdfFillRule.NonZero, canFill: true);
                DrawMarkers(path, opacity);
                break;

            case SceneText text:
                DrawText(text, opacity);
                break;

            case SceneImage image:
                DrawImage(image, opacity);
                break;

            // A use draws its content again, as a whole with the opacity of the use; a use in its own content draws nothing
            case SceneUse use when !use.IsCircular() && _drawingUses.Add(use):
                try
                {
                    if (opacity < 1 || isolated)
                    {
                        DrawTransparencyGroup([use.Content], opacity);
                    }
                    else
                    {
                        DrawElement(use.Content);
                    }
                }
                finally
                {
                    _drawingUses.Remove(use);
                }

                break;
        }
    }

    // The content of each marker is drawn on its vertex, after the shape, with the opacity of the shape. A marker drawn on
    // a shape of its own content is not drawn again; its hyperlinks are not links of the page.
    private void DrawMarkers(SceneShape shape, double opacity)
    {
        foreach (var (marker, vertex, isStart) in GetMarkers(shape))
        {
            var transform = marker.GetTransform(vertex, shape.StrokeWidth, isStart);
            if (!IsFinite(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F) || (transform.A * transform.D) - (transform.B * transform.C) == 0 || marker.Content.Children.Count is 0 || !_drawingMarkers.Add(marker))
                continue;

            Save();
            Transform(new PdfMatrix(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F));
            _hiddenDepth++;
            try
            {
                // The content is a group: its transform, its opacity and its effects apply
                if (opacity < 1)
                {
                    DrawTransparencyGroup([marker.Content], opacity);
                }
                else
                {
                    DrawElement(marker.Content);
                }
            }
            finally
            {
                _hiddenDepth--;
                _drawingMarkers.Remove(marker);
                Restore();
            }
        }
    }

    // The markers of a path or a line, with the vertex each one is drawn on, as SceneGeometry gives them to the bounds
    private static IEnumerable<(SceneMarker Marker, PathPoint Vertex, bool IsStart)> GetMarkers(SceneShape shape)
    {
        var (start, middle, end) = shape switch
        {
            ScenePath path => (path.MarkerStart, path.MarkerMid, path.MarkerEnd),
            SceneLine line => (line.MarkerStart, null, line.MarkerEnd),
            _ => (null, null, null),
        };

        if (start is null && middle is null && end is null)
            yield break;

        var vertices = shape switch
        {
            ScenePath path => path.Data.GetVertices(),
            SceneLine line => new PathBuilder().MoveTo(line.X1, line.Y1).LineTo(line.X2, line.Y2).Build().GetVertices(),
            _ => [],
        };

        for (var i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
                continue;

            if (i == 0 && start is not null)
            {
                yield return (start, vertex, true);
            }

            if (i > 0 && i < vertices.Count - 1 && middle is not null)
            {
                yield return (middle, vertex, false);
            }

            if (i == vertices.Count - 1 && end is not null)
            {
                yield return (end, vertex, false);
            }
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

        // A view box that draws nothing has no children
        SceneTransform? viewBoxTransform = null;
        if (group.ViewBox is { } viewBox && (viewBoxTransform = viewBox.GetTransform()) is null)
            return;

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

        // A view box cuts the children to its viewport, in the coordinates of the group, and maps them to it
        if (group.ViewBox is { } groupViewBox && viewBoxTransform is { } mapping)
        {
            var viewport = groupViewBox.Viewport;
            _content.Rectangle(viewport.X, viewport.Y, viewport.Width, viewport.Height).Clip().EndPath();
            Transform(new PdfMatrix(mapping.A, mapping.B, mapping.C, mapping.D, mapping.E, mapping.F));
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

        _hiddenDepth++;
        PdfForm form;
        try
        {
            form = CreateForm([mask.Content], box, new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Rgb });
        }
        finally
        {
            _hiddenDepth--;
        }

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
            SetStrokeStyle(shape.StrokeWidth, shape.LineCap, shape.LineJoin, shape.MiterLimit, shape.StrokeDashArray, shape.StrokeDashOffset);
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

    // The width, the caps, the joins and the dashes of a stroke
    private void SetStrokeStyle(double width, SceneLineCap lineCap, SceneLineJoin lineJoin, double miterLimit, IReadOnlyList<double>? dashArray, double dashOffset)
    {
        _content.SetLineWidth(width)
            .SetLineCap(lineCap switch
            {
                SceneLineCap.Round => PdfLineCap.Round,
                SceneLineCap.Square => PdfLineCap.ProjectingSquare,
                _ => PdfLineCap.Butt,
            })
            .SetLineJoin(lineJoin switch
            {
                SceneLineJoin.Round => PdfLineJoin.Round,
                SceneLineJoin.Bevel => PdfLineJoin.Bevel,
                _ => PdfLineJoin.Miter,
            })
            .SetMiterLimit(miterLimit >= 1 && double.IsFinite(miterLimit) ? miterLimit : DefaultMiterLimit);

        // SVG draws a solid line when a length of the pattern is negative or when they are all 0
        if (dashArray is { Count: > 0 } dashes && dashes.All(dash => dash >= 0 && double.IsFinite(dash)) && dashes.Sum() > 0)
        {
            // An odd number of lengths is repeated, as in SVG
            double[] pattern = dashes.Count % 2 == 0 ? [.. dashes] : [.. dashes, .. dashes];

            // The phase is brought into the pattern: a negative offset starts before it
            var offset = double.IsFinite(dashOffset) ? dashOffset : 0;
            var phase = offset % pattern.Sum();
            _content.SetDashPattern(pattern, phase < 0 ? phase + pattern.Sum() : phase);
        }
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
    private double? SetPaint(ScenePaint paint, bool stroke, SceneElement element)
    {
        switch (paint)
        {
            case SolidPaint solid:
                SetColor(solid.Color, stroke);
                return solid.Color.A / 255.0;

            case SceneGradientPaint gradient:
                return SetGradient(gradient, stroke, element);

            case PatternPaint pattern:
                return SetTilingPattern(pattern, stroke);

            default:
                return null;
        }
    }

    private double? SetGradient(SceneGradientPaint gradient, bool stroke, SceneElement element)
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
        if (gradient.Spread is not SceneGradientSpread.Pad && GetLocalBounds(element)?.Transform(inverse) is { IsFinite: true } box)
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

    // The bounds of a shape or a text in its own coordinates, inside its transform
    private Bounds? GetLocalBounds(SceneElement element)
    {
        if (Bounds.Of(element, this) is not { } bounds)
            return null;

        if (element.Transform is not { } transform)
            return bounds;

        return new PdfMatrix(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F).TryInvert(out var inverse) ? bounds.Transform(inverse) : null;
    }

    // A pattern is a tiling pattern whose cell is the tile (8.7.3); its matrix places it in the space of the current
    // content stream, as the one of a gradient. A pattern that paints with itself, or whose tile has no area, paints
    // nothing.
    private double? SetTilingPattern(PatternPaint paint, bool stroke)
    {
        var tile = paint.Tile;
        if (!(tile.Width > 0 && tile.Height > 0) || !IsFinite(tile.X, tile.Y, tile.Width, tile.Height, tile.Right, tile.Bottom) || _drawingPatterns.Contains(paint))
            return null;

        var transform = paint.Transform is { } value ? new PdfMatrix(value.A, value.B, value.C, value.D, value.E, value.F) : PdfMatrix.Identity;
        var matrix = PdfMatrix.Multiply(transform, _matrix);
        if (!IsFinite(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F))
            return null;

        if (!_tilingPatterns.TryGetValue((paint, matrix), out var pattern))
        {
            pattern = page.Document.CreateTilingPattern(new PdfRectangle(tile.X, tile.Y, tile.Right, tile.Bottom), tile.Width, tile.Height, isColored: true, matrix);
            var (outerContent, outerMatrix, outerStreamToPage) = (_content, _matrix, _streamToPage);
            _streamToPage = PdfMatrix.Multiply(matrix, _streamToPage);
            _matrix = PdfMatrix.Identity;
            _content = pattern.CreateContentBuilder();
            _drawingPatterns.Add(paint);
            _hiddenDepth++;
            try
            {
                // The content is a group: its transform, its opacity and its effects apply
                DrawElement(paint.Content);
                _content.Complete();
            }
            finally
            {
                _hiddenDepth--;
                _drawingPatterns.Remove(paint);
                (_content, _matrix, _streamToPage) = (outerContent, outerMatrix, outerStreamToPage);
            }

            _tilingPatterns.Add((paint, matrix), pattern);
        }

        SetPattern(pattern, stroke);
        return 1;
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
        if (text.Path is { } path)
        {
            DrawTextOnPath(text, path, opacity);
            return;
        }

        if (text.Text.Length is 0 || !(opacity > 0) || !IsFinite(text.X, text.Y, text.Rotation))
            return;

        var (parts, placement) = GetTextLayout(text);
        if (parts.Count is 0)
            return;

        // The glyphs are drawn upright in the flipped space, scaled to the length of the text, turned around their origin
        // by the rotation of their span, and the text is rotated clockwise around its anchor. Each part (a span, or the
        // characters of a span that a face draws) is a text object of its own, with its paints and its opacity.
        var rotation = SceneTransform.Rotate(text.Rotation, text.X, text.Y);
        var rotationMatrix = new PdfMatrix(rotation.A, rotation.B, rotation.C, rotation.D, rotation.E, rotation.F);
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var (x, baseline) = (placement.X[i], placement.Y[i]);
            if (!IsFinite(x, baseline))
                continue;

            var origin = new PdfMatrix(1, 0, 0, 1, x, baseline);
            if (part.Rotation != 0)
            {
                var glyphRotation = SceneTransform.Rotate(part.Rotation);
                origin = PdfMatrix.Multiply(new PdfMatrix(glyphRotation.A, glyphRotation.B, glyphRotation.C, glyphRotation.D, 0, 0), origin);
            }

            var matrix = PdfMatrix.Multiply(origin, rotationMatrix);
            PaintText(text, part, opacity, () =>
            {
                _content.SetTextMatrix(PdfMatrix.Multiply(new PdfMatrix(placement.Scale, 0, 0, -1, 0, 0), matrix));
                _content.SetFont(part.Font, part.Size);
                ShowPart(part);
            });
            DrawDecorations(text, part, 0, 0, placement.Width[i], matrix, opacity);
        }
    }

    // Each cluster is a text of its own, turned to the direction of the path at the middle of its advance, with the
    // middle of its advance on the path, moved across the path by the shifts of its span; the clusters whose middle is
    // beyond the ends of the path are not drawn, as in SVG. Its lines are pieces of the same length, turned with it.
    private void DrawTextOnPath(SceneText text, PathData path, double opacity)
    {
        if (text.Text.Length is 0 || !(opacity > 0) || path.IsEmpty)
            return;

        var (parts, placement) = GetTextLayout(text, layoutClusters: true);
        var length = path.GetLength();
        if (parts.Count is 0 || !(length > 0) || !double.IsFinite(length))
            return;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var (start, offset) = (placement.X[i], placement.Y[i]);
            if (!IsFinite(start, offset))
                continue;

            var clusters = new List<(TextCluster Cluster, PdfMatrix Matrix)>();
            foreach (var cluster in part.Clusters!)
            {
                var middle = start + cluster.X + (cluster.Advance / 2);
                if (middle < 0 || middle > length)
                    continue;

                var point = path.GetPointAtLength(middle);
                var rotation = SceneTransform.Rotate(point.Angle);
                var matrix = PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, 1, -cluster.Advance / 2, offset), new PdfMatrix(rotation.A, rotation.B, rotation.C, rotation.D, point.X, point.Y));
                if (IsFinite(matrix.A, matrix.B, matrix.C, matrix.D, matrix.E, matrix.F))
                {
                    clusters.Add((cluster, matrix));
                }
            }

            if (clusters.Count is 0)
                continue;

            PaintText(text, part, opacity, () =>
            {
                _content.SetFont(part.Font, part.Size);
                _content.BeginMarkedContent("Span", new PdfDictionary { ["ActualText"] = PdfString.FromText(part.Text) });
                foreach (var (cluster, matrix) in clusters)
                {
                    _content.SetTextMatrix(PdfMatrix.Multiply(new PdfMatrix(1, 0, 0, -1, 0, 0), matrix));
                    if (cluster.Glyphs is { } glyphs)
                    {
                        if (glyphs.Length > 0)
                        {
                            _content.ShowGlyphs(glyphs);
                        }
                    }
                    else
                    {
                        _content.ShowText(cluster.Text);
                    }
                }

                _content.EndMarkedContent();
            });

            foreach (var (cluster, matrix) in clusters)
            {
                DrawDecorations(text, part, 0, 0, cluster.Advance, matrix, opacity);
            }
        }
    }

    // Paints the glyphs of a part with its fill and its outline, in a text object whose content show draws, with the
    // rendering mode that fills, strokes, or does both (9.3.6). An outline painted before the fill is a text object of its
    // own, whose replacement text is empty, so that the text is extracted once.
    private void PaintText(SceneText text, TextPart part, double opacity, Action show)
    {
        Save();
        var fillAlpha = part.FillPaint is { } fillPaint ? SetPaint(fillPaint, stroke: false, text) : SetColorPaint(part.FillColor, stroke: false);
        var strokeAlpha = part.Stroke is { } strokePaint ? SetPaint(strokePaint, stroke: true, text) : null;
        fillAlpha = fillAlpha > 0 ? fillAlpha : null;
        strokeAlpha = strokeAlpha > 0 ? strokeAlpha : null;
        if (fillAlpha is null && strokeAlpha is null)
        {
            Restore();
            return;
        }

        SetAlpha((fillAlpha ?? 1) * opacity, (strokeAlpha ?? 1) * opacity);
        if (strokeAlpha is not null)
        {
            SetStrokeStyle(text.StrokeWidth, text.LineCap, text.LineJoin, text.MiterLimit, text.StrokeDashArray, text.StrokeDashOffset);
        }

        if (fillAlpha is not null && strokeAlpha is not null && TextPlacement.IsStrokeFirst(text.PaintOrder))
        {
            _content.BeginMarkedContent("Span", new PdfDictionary { ["ActualText"] = PdfString.FromText("") });
            ShowPass(PdfTextRenderingMode.Stroke);
            _content.EndMarkedContent();
            ShowPass(PdfTextRenderingMode.Fill, reset: true);
        }
        else
        {
            ShowPass(fillAlpha is null ? PdfTextRenderingMode.Stroke : strokeAlpha is null ? PdfTextRenderingMode.Fill : PdfTextRenderingMode.FillStroke);
        }

        Restore();

        // The fill is the default rendering mode: it is set after another one only
        void ShowPass(PdfTextRenderingMode mode, bool reset = false)
        {
            _content.BeginText();
            if (mode is not PdfTextRenderingMode.Fill || reset)
            {
                _content.SetTextRenderingMode(mode);
            }

            show();
            _content.EndText();
        }
    }

    // A part laid out in clusters is shown with the positions of its glyphs (an embedded font), or with an adjustment
    // after each cluster (a standard font, whose codes are single bytes and which the PDF does not shape). The spacing
    // would read as spaces: the characters are the replacement text of what is shown (14.9.4).
    private void ShowPart(TextPart part)
    {
        if (part.Clusters is not { } clusters)
        {
            _content.ShowText(part.Text);
            return;
        }

        if (clusters.Count is 0)
            return;

        _content.BeginMarkedContent("Span", new PdfDictionary { ["ActualText"] = PdfString.FromText(part.Text) });
        if (clusters[0].Glyphs is null)
        {
            var segments = new List<PdfTextSegment>(clusters.Count * 2);
            foreach (var cluster in clusters)
            {
                segments.Add(cluster.Text);
                if (cluster.Spacing != 0)
                {
                    segments.Add(-cluster.Spacing * 1000 / part.Size);
                }
            }

            _content.ShowText([.. segments]);
        }
        else
        {
            var glyphs = new List<PdfGlyph>();
            foreach (var cluster in clusters)
            {
                foreach (var glyph in cluster.Glyphs!)
                {
                    glyphs.Add(glyph with { X = cluster.X + glyph.X });
                }
            }

            _content.ShowGlyphs([.. glyphs]);
        }

        _content.EndMarkedContent();
    }

    // The lines of a part are rectangles of its paint, at the position and of the thickness the face of its span gives
    private void DrawDecorations(SceneText text, TextPart part, double x, double baseline, double width, PdfMatrix rotation, double opacity)
    {
        if ((part.Decorations & (SceneTextDecorations.Underline | SceneTextDecorations.Strikethrough | SceneTextDecorations.Overline)) is SceneTextDecorations.None || !(width > 0))
            return;

        Save();
        var alpha = part.DecorationPaint is { } paint ? SetPaint(paint, stroke: false, text) : SetColorPaint(part.DecorationColor, stroke: false);
        if (!(alpha > 0))
        {
            Restore();
            return;
        }

        SetAlpha(alpha.Value * opacity, strokeAlpha: 1);
        Transform(rotation);
        if (part.Decorations.HasFlag(SceneTextDecorations.Underline))
        {
            var top = part.Face?.UnderlinePosition ?? HelveticaUnderlinePosition;
            var thickness = part.Face?.UnderlineThickness ?? HelveticaLineThickness;
            _content.Rectangle(x, baseline - (top * part.Size), width, thickness * part.Size).Fill();
        }

        if (part.Decorations.HasFlag(SceneTextDecorations.Strikethrough))
        {
            var top = part.Face?.StrikeoutPosition ?? HelveticaStrikeoutPosition;
            var thickness = part.Face?.StrikeoutThickness ?? HelveticaLineThickness;
            _content.Rectangle(x, baseline - (top * part.Size), width, thickness * part.Size).Fill();
        }

        // An overline is at the ascent of the font, as thick as an underline, as in the image renderer
        if (part.Decorations.HasFlag(SceneTextDecorations.Overline))
        {
            var thickness = part.Face?.UnderlineThickness ?? HelveticaLineThickness;
            _content.Rectangle(x, baseline - (part.Font.Ascent / 1000 * part.Size), width, thickness * part.Size).Fill();
        }

        Restore();
    }

    // The glyphs fit in an em above the baseline and half an em below it, from the start to the end of each part; a
    // character turned around its origin stays within its advance and an em and a half of it
    private SceneBounds? GetTextBounds(SceneText text)
    {
        var (parts, placement) = GetTextLayout(text);
        SceneBounds? bounds = null;
        for (var i = 0; i < parts.Count; i++)
        {
            var (x, baseline, width, size) = (placement.X[i], placement.Y[i], placement.Width[i], parts[i].Size);
            if (!IsFinite(x, baseline, width))
                continue;

            SceneBounds box;
            if (parts[i].Rotation != 0)
            {
                var reach = Math.Abs(width) + (size * 1.5);
                box = new SceneBounds(x - reach, baseline - reach, 2 * reach, 2 * reach);
            }
            else
            {
                box = new SceneBounds(Math.Min(x, x + width), baseline - size, Math.Abs(width), size * 1.5);
            }

            bounds = bounds is { } union ? new SceneBounds(Math.Min(union.X, box.X), Math.Min(union.Y, box.Y), Math.Max(union.Right, box.Right) - Math.Min(union.X, box.X), Math.Max(union.Bottom, box.Bottom) - Math.Min(union.Y, box.Y)) : box;
        }

        return bounds;
    }

    /// <summary>A part of a text that one font draws, with its paints, its style and its width.</summary>
    /// <remarks>
    /// <see cref="Clusters"/> places the characters of a part that the PDF font cannot place by itself, such as a spaced
    /// text: its width includes the spacing. It is <see langword="null"/> otherwise. <see cref="FillPaint"/> and
    /// <see cref="DecorationPaint"/> are <see langword="null"/> when the glyphs and the lines are painted with a color.
    /// </remarks>
    internal sealed record TextPart(
        PdfFont Font, string Text, double Width, double Size,
        ScenePaint? FillPaint, SceneColor FillColor, ScenePaint? Stroke, ScenePaint? DecorationPaint, SceneColor DecorationColor,
        SceneTextDecorations Decorations, SceneFontFace? Face, List<TextCluster>? Clusters, double Rotation);

    /// <summary>The characters of a part, before they are laid out, with the metrics of the font of their span.</summary>
    private sealed record TextPiece(TextPart Part, int Span, FontFace? Shaper, double LetterSpacing, double WordSpacing, double BaselineOffset);

    /// <summary>
    /// Splits a text into the parts that one font draws (its spans, and the characters of a span that each face draws;
    /// each grapheme cluster of a span whose characters are turned; the last cluster of a text fitted to its length by its
    /// spacing, which gets no spacing) and places them (<see cref="TextPlacement"/>).
    /// </summary>
    internal (List<TextPart> Parts, TextPlacement.Result Placement) GetTextLayout(SceneText text, bool layoutClusters = false)
    {
        var rightToLeft = text.Direction is SceneTextDirection.RightToLeft;
        layoutClusters |= rightToLeft;
        var pieces = new List<TextPiece>();
        if (text.Spans.Count is 0)
        {
            AddSpan(-1, null, text.Text, text.Font, text.Decorations, text.LetterSpacing, text.WordSpacing);
        }
        else
        {
            for (var i = 0; i < text.Spans.Count; i++)
            {
                var span = text.Spans[i];
                AddSpan(i, span, span.Text, span.Font ?? text.Font, span.Decorations ?? text.Decorations, span.LetterSpacing ?? text.LetterSpacing, span.WordSpacing ?? text.WordSpacing);
            }
        }

        if (pieces.Count > 0 && TextPlacement.IsFittedBySpacing(text) && TextPlacement.GetClusters(pieces[^1].Part.Text) is { Count: > 1 } lastClusters)
        {
            var last = pieces[^1];
            pieces[^1] = last with { Part = last.Part with { Text = last.Part.Text[..^lastClusters[^1].Length] } };
            pieces.Add(last with { Part = last.Part with { Text = lastClusters[^1] } });
        }

        var parts = new List<TextPart>(pieces.Count);
        var items = new TextPlacement.Item[pieces.Count];
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            parts.Add(Layout(piece, gapSpacing: 0));
            items[i] = new TextPlacement.Item(piece.Span, parts[i].Width, TextPlacement.CountClusters(piece.Part.Text), piece.BaselineOffset);
        }

        var placement = TextPlacement.Place(text, items);
        if (placement.GapSpacing != 0)
        {
            for (var i = 0; i < pieces.Count; i++)
            {
                if (i != placement.LastItem)
                {
                    parts[i] = Layout(pieces[i], placement.GapSpacing);
                }
            }
        }

        return (parts, placement);

        TextPart Layout(TextPiece piece, double gapSpacing)
        {
            var part = piece.Part;
            var letterSpacing = NormalizeSpacing(piece.LetterSpacing + gapSpacing);
            if (layoutClusters || letterSpacing != 0 || piece.WordSpacing != 0)
            {
                var (clusters, width) = TextClusterLayout.Layout(part.Font, piece.Shaper, part.Text, part.Size, letterSpacing, piece.WordSpacing, rightToLeft);
                return part with { Width = width, Clusters = clusters };
            }

            return part with { Width = part.Font.GetTextWidth(part.Text, part.Size), Clusters = null };
        }

        void AddSpan(int index, SceneTextSpan? span, string value, SceneFont font, SceneTextDecorations decorations, double letterSpacing, double wordSpacing)
        {
            if (value.Length is 0 || !(font.Size > 0) || !double.IsFinite(font.Size))
                return;

            letterSpacing = NormalizeSpacing(letterSpacing);
            wordSpacing = NormalizeSpacing(wordSpacing);
            var (fillPaint, fillColor) = TextPlacement.GetFill(text, span);
            var (decorationPaint, decorationColor) = TextPlacement.GetDecorationFill(text, span);
            var stroke = TextPlacement.GetStroke(text, span);
            var rotation = text.Path is null ? TextPlacement.GetGlyphRotation(span) : 0;
            var standard = GetStandardFont(font);

            // The baseline of the text is placed with the metrics of the face of the span, as the image renderer does
            var spanFace = fonts?.GetFace(font, scene.FontFamily);
            var metricsFace = spanFace is null ? null : GetEmbeddedFont(spanFace).Face;
            var baselineOffset = metricsFace is null
                ? TextPlacement.GetBaselineOffset(text.Baseline, standard.Ascent / 1000 * font.Size, -standard.Descent / 1000 * font.Size, HelveticaXHeight * font.Size)
                : GetBaselineOffset(text.Baseline, metricsFace, font.Size);

            if (fonts is null)
            {
                Add(standard, null, value, null);
                return;
            }

            foreach (var run in fonts.GetRuns(value, font, scene.FontFamily))
            {
                var embedded = GetEmbeddedFont(run.Face);
                if (embedded.Font is not null)
                {
                    Add(embedded.Font, embedded.Face, value.Substring(run.Start, run.Length), spanFace);
                }
                else
                {
                    Add(standard, null, value.Substring(run.Start, run.Length), spanFace);
                }
            }

            void Add(PdfFont pdfFont, FontFace? fontFace, string runText, SceneFontFace? face)
            {
                runText = Sanitize(pdfFont, runText);
                if (runText.Length is 0)
                    return;

                var part = new TextPart(pdfFont, runText, 0, font.Size, fillPaint, fillColor, stroke, decorationPaint, decorationColor, decorations, face, null, rotation);
                if (rotation == 0)
                {
                    pieces.Add(new TextPiece(part, index, fontFace, letterSpacing, wordSpacing, baselineOffset));
                    return;
                }

                foreach (var cluster in TextPlacement.GetClusters(runText))
                {
                    pieces.Add(new TextPiece(part with { Text = cluster }, index, fontFace, letterSpacing, wordSpacing, baselineOffset));
                }
            }
        }
    }

    // The x-height of Helvetica (Helvetica.afm), in em
    private const double HelveticaXHeight = 0.523;

    // The ascent and the descent of a face, as the image renderer reads them: the typographic metrics when the face asks
    // for them, else the metrics of the horizontal header, else the typographic metrics, else the Windows metrics
    private static double GetBaselineOffset(SceneTextBaseline baseline, FontFace face, double size)
    {
        int ascender;
        int descender;
        if (face.HasOs2 && face.UseTypoMetrics)
        {
            (ascender, descender) = (face.TypoAscender, face.TypoDescender);
        }
        else if (face.HorizontalAscender != 0 || face.HorizontalDescender != 0)
        {
            (ascender, descender) = (face.HorizontalAscender, face.HorizontalDescender);
        }
        else if (face.HasOs2 && (face.TypoAscender != 0 || face.TypoDescender != 0))
        {
            (ascender, descender) = (face.TypoAscender, face.TypoDescender);
        }
        else
        {
            (ascender, descender) = (face.WinAscent, -face.WinDescent);
        }

        var scale = size / face.UnitsPerEm;
        return TextPlacement.GetBaselineOffset(baseline, ascender * scale, -descender * scale, face.XHeight * scale);
    }

    // The largest magnitude of a spacing, as in the other renderers
    private static double NormalizeSpacing(double spacing) => double.IsFinite(spacing) ? Math.Clamp(spacing, -65536, 65536) : 0;

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

    // A face that cannot be embedded (bitmap or CFF2 outlines, a license that forbids it) is replaced by Helvetica.
    // The face of the font file shapes the texts the PDF font cannot place by itself: spaced texts, texts on a path.
    private static EmbeddedFont GetEmbeddedFont(SceneFontFace face)
    {
        return EmbeddedFonts.GetValue(face, static face =>
        {
            try
            {
                // Each font has its own environment, as PdfFont.Load gives it: the face keeps its buffers
                var faces = FontLoader.Load(face.Data.Span, new PooledFontEnvironment(FontResourceLimits.Default, 64L * 1024 * 1024));
                if ((uint)face.Index >= (uint)faces.Length)
                    return new EmbeddedFont(null, null);

                var fontFace = faces[face.Index];
                return new EmbeddedFont(PdfFont.Load(face.Data.Span, face.Index), fontFace);
            }
            catch (Exception exception) when (exception is FontException or UnsupportedPdfFeatureException or ArgumentOutOfRangeException)
            {
                return new EmbeddedFont(null, null);
            }
        });
    }

    // Sets a color as a paint and returns its opacity
    private double SetColorPaint(SceneColor color, bool stroke)
    {
        SetColor(color, stroke);
        return color.A / 255.0;
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

    private sealed record EmbeddedFont(PdfFont? Font, FontFace? Face);

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

        /// <summary>Gets a rectangle that contains what an element draws, in the coordinates of its parent, or null when it draws nothing.</summary>
        public static Bounds? Of(SceneElement element, ScenePdfWriter writer)
        {
            return element.GetBounds(writer.GetTextBounds) is { } bounds ? new Bounds(bounds.X, bounds.Y, bounds.Right, bounds.Bottom) : null;
        }
    }
}
