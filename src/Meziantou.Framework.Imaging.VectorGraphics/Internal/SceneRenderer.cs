using System.Runtime.InteropServices;
using Meziantou.Framework.VectorGraphics;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.VectorGraphics.Internal;

/// <summary>Draws the primitives of a <see cref="Scene"/> with the canvas of <c>Meziantou.Framework.Imaging</c>. Hyperlinks are ignored.</summary>
internal static class SceneRenderer
{

    public static void Draw(DrawingCanvas canvas, Scene scene, PointD position, SceneImageOptions options)
    {
        var context = new DrawContext(options, CreateBrushCache()) { Fonts = scene.Fonts ?? options.Fonts, FontFamily = scene.FontFamily };
        var transform = AffineTransform.CreateScale(options.Scale, options.Scale).Then(AffineTransform.CreateTranslation(position.X, position.Y));
        canvas.Transform(transform, (scene, context), static (canvas, state) =>
        {
            if (state.scene.Background is { } background)
            {
                canvas.FillRectangle(ToBrush(background), new RectangleD(0, 0, state.scene.Width, state.scene.Height));
            }

            DrawChildren(canvas, state.scene.Root, state.context);
        });
    }

    private static readonly Brush Transparent = new SolidBrush(new Rgba32(0, 0, 0, 0));

    internal static Brush ToBrush(SceneColor color) => new SolidBrush(new Rgba32(color.R, color.G, color.B, color.A));

    internal static VectorPath? ToVectorPath(PathData data)
    {
        if (data.IsEmpty)
            return null;

        var builder = new VectorPathBuilder();
        foreach (var command in data.Commands)
        {
            switch (command.Kind)
            {
                case PathCommandKind.MoveTo:
                    builder.MoveTo(command.X, command.Y);
                    break;
                case PathCommandKind.LineTo:
                    builder.LineTo(command.X, command.Y);
                    break;
                case PathCommandKind.CubicTo:
                    builder.CubicBezierTo(command.X1, command.Y1, command.X2, command.Y2, command.X, command.Y);
                    break;
                case PathCommandKind.QuadraticTo:
                    builder.QuadraticBezierTo(command.X1, command.Y1, command.X, command.Y);
                    break;
                case PathCommandKind.ArcTo:
                    builder.ArcTo(command.X1, command.Y1, command.X2, command.LargeArc, command.Sweep, command.X, command.Y);
                    break;
                case PathCommandKind.Close:
                    builder.Close();
                    break;
            }
        }

        return builder.Build();
    }

    private static Dictionary<ScenePaint, Brush> CreateBrushCache() => new(ReferenceEqualityComparer.Instance);

    private static void DrawChildren(DrawingCanvas canvas, SceneGroup group, DrawContext context)
    {
        foreach (var child in group.Children)
        {
            DrawElement(canvas, child, context);
        }
    }

    // The effects apply in the order of SVG: the filter, the clips, the mask, then the opacity and the blend mode
    private static void DrawElement(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        if (element.Opacity <= 0)
            return;

        var blendMode = ToBlendMode(element.BlendMode);
        if (element.Opacity < 1 || blendMode is not BlendMode.Normal)
        {
            canvas.Layer(Math.Min(element.Opacity, 1), (element, context), static (canvas, state) => DrawMasked(canvas, state.element, state.context), CompositeMode.SourceOver, blendMode);
            return;
        }

        DrawMasked(canvas, element, context);
    }

    private static void DrawMasked(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        if (element.Mask is { } mask)
        {
            canvas.Mask(
                (element, mask, context),
                static (canvas, state) => DrawElement(canvas, state.mask.Content, state.context),
                static (canvas, state) => DrawUnmasked(canvas, state.element, state.context),
                mask.Mode is SceneMaskMode.Alpha ? MaskMode.Alpha : MaskMode.Luminance);
            return;
        }

        DrawUnmasked(canvas, element, context);
    }

    // A group applies its clips before its filter, and its transform after it
    private static void DrawUnmasked(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        if (element is SceneGroup group)
        {
            DrawGroup(canvas, group, context);
            return;
        }

        DrawFiltered(canvas, element, context, static (canvas, state) => DrawTransformedContent(canvas, state.element, state.context));
    }

    // The transform of an element applies inside its filter, its clips and its mask, as for a group
    private static void DrawTransformedContent(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        if (element.Transform is { } transform)
        {
            if (!IsFinite(transform))
                return;

            canvas.Transform(ToTransform(transform), (element, context), static (canvas, state) => DrawContent(canvas, state.element, state.context));
            return;
        }

        DrawContent(canvas, element, context);
    }

    private static bool IsFinite(SceneTransform transform) => double.IsFinite(transform.A) && double.IsFinite(transform.B) && double.IsFinite(transform.C) && double.IsFinite(transform.D) && double.IsFinite(transform.E) && double.IsFinite(transform.F);

    private static void DrawFiltered(DrawingCanvas canvas, SceneElement element, DrawContext context, DrawingAction<(SceneElement element, DrawContext context)> action)
    {
        switch (element.Filter)
        {
            case SceneBlurFilter blur when blur.StandardDeviation > 0 && double.IsFinite(blur.StandardDeviation):
                canvas.Blur(Math.Min(blur.StandardDeviation, MaxBlur), (element, context), action);
                break;

            case SceneColorMatrixFilter colorMatrix:
                // A matrix with a constant alpha gives a color to the transparent pixels: as the filter region of SVG,
                // it applies to the bounds of the element only, not to the whole canvas
                if (colorMatrix.Values[19] != 0)
                {
                    // The filter applies inside the clips of the element: its region is what it draws before them
                    if (GetBounds(element, context, applyClips: false) is not { } region)
                        break;

                    canvas.Clip(region.Inflate(1), (element, context, colorMatrix, action), static (canvas, state) => canvas.Filter(new ColorMatrix([.. state.colorMatrix.Values]), (state.element, state.context), state.action));
                    break;
                }

                canvas.Filter(new ColorMatrix([.. colorMatrix.Values]), (element, context), action);
                break;

            case SceneDropShadowFilter shadow:
                var deviation = shadow.StandardDeviation > 0 && double.IsFinite(shadow.StandardDeviation) ? Math.Min(shadow.StandardDeviation, MaxBlur) : 0;
                var offset = new PointD(double.IsFinite(shadow.OffsetX) ? shadow.OffsetX : 0, double.IsFinite(shadow.OffsetY) ? shadow.OffsetY : 0);
                canvas.Shadow(Rgba64.FromRgba32(new Rgba32(shadow.Color.R, shadow.Color.G, shadow.Color.B, shadow.Color.A)), offset, deviation, (element, context), action);
                break;

            default:
                action(canvas, (element, context));
                break;
        }
    }

    /// <summary>Gets a rectangle that contains what an element draws, with the filters of its children, in the coordinates of its parent.</summary>
    private static RectangleD? GetBounds(SceneElement element, DrawContext context, bool applyClips)
    {
        var bounds = element.GetBounds(text =>
        {
            // The box of each part, and the ink of its glyphs, which overhang their advance (italic letters) and rise
            // above the ascent (marks), at the position the part is drawn. A character turned around its origin stays
            // within its advance and its height of it.
            if (CreateTextPlan(text, context) is not { } plan)
                return null;

            RectangleD? area = null;
            var placement = plan.Placement;
            for (var i = 0; i < plan.Parts.Count; i++)
            {
                var (layout, _, _, rotation) = plan.Parts[i];
                var (x, baseline, width) = (placement.X[i], placement.Y[i], placement.Width[i]);
                if (!double.IsFinite(x) || !double.IsFinite(baseline) || !double.IsFinite(width))
                    continue;

                var top = baseline - layout.Baseline;
                RectangleD box;
                if (rotation != 0)
                {
                    var reach = Math.Abs(width) + layout.Height;
                    box = new RectangleD(x - reach, baseline - reach, 2 * reach, 2 * reach);
                }
                else
                {
                    box = new RectangleD(Math.Min(x, x + width), top, Math.Abs(width), layout.Height);
                    if (!layout.InkBounds.IsEmpty)
                    {
                        var ink = layout.InkBounds;
                        box = RectangleD.Union(box, new RectangleD(x + (ink.X * placement.Scale), top + ink.Y, ink.Width * placement.Scale, ink.Height));
                    }
                }

                area = area is { } union ? RectangleD.Union(union, box) : box;
            }

            return area is { } value ? new SceneBounds(value.X, value.Y, value.Width, value.Height) : null;
        }, applyClips);

        return bounds is { } value ? new RectangleD(value.X, value.Y, value.Width, value.Height) : null;
    }

    // The largest standard deviation of the blurs of the canvas
    private const double MaxBlur = 10000;

    private static BlendMode ToBlendMode(SceneBlendMode mode) => mode switch
    {
        SceneBlendMode.Multiply => BlendMode.Multiply,
        SceneBlendMode.Screen => BlendMode.Screen,
        SceneBlendMode.Overlay => BlendMode.Overlay,
        SceneBlendMode.Darken => BlendMode.Darken,
        SceneBlendMode.Lighten => BlendMode.Lighten,
        SceneBlendMode.ColorDodge => BlendMode.ColorDodge,
        SceneBlendMode.ColorBurn => BlendMode.ColorBurn,
        SceneBlendMode.HardLight => BlendMode.HardLight,
        SceneBlendMode.SoftLight => BlendMode.SoftLight,
        SceneBlendMode.Difference => BlendMode.Difference,
        SceneBlendMode.Exclusion => BlendMode.Exclusion,
        SceneBlendMode.Hue => BlendMode.Hue,
        SceneBlendMode.Saturation => BlendMode.Saturation,
        SceneBlendMode.Color => BlendMode.Color,
        SceneBlendMode.Luminosity => BlendMode.Luminosity,
        _ => BlendMode.Normal,
    };

    private static void DrawContent(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        switch (element)
        {

            case SceneRectangle rectangle:
                {
                    var bounds = new RectangleD(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
                    if (rectangle.Width <= 0 || rectangle.Height <= 0)
                    {
                        if (rectangle.Stroke is not null && (rectangle.Width > 0 || rectangle.Height > 0))
                        {
                            canvas.StrokeLine(CreatePen(rectangle, context), new PointD(rectangle.X, rectangle.Y), new PointD(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height));
                        }

                        break;
                    }

                    if (rectangle.Fill is { } fill)
                    {
                        if (rectangle.CornerRadius > 0)
                        {
                            canvas.FillRoundedRectangle(context.GetBrush(fill), bounds, rectangle.CornerRadius);
                        }
                        else
                        {
                            canvas.FillRectangle(context.GetBrush(fill), bounds);
                        }
                    }

                    if (rectangle.Stroke is not null)
                    {
                        if (rectangle.CornerRadius > 0)
                        {
                            canvas.StrokeRoundedRectangle(CreatePen(rectangle, context), bounds, rectangle.CornerRadius);
                        }
                        else
                        {
                            canvas.StrokeRectangle(CreatePen(rectangle, context), bounds);
                        }
                    }

                    break;
                }

            case SceneCircle circle:
                {
                    if (circle.Radius <= 0)
                        break;

                    var center = new PointD(circle.CenterX, circle.CenterY);
                    if (circle.Fill is { } fill)
                    {
                        canvas.FillCircle(context.GetBrush(fill), center, circle.Radius);
                    }

                    if (circle.Stroke is not null)
                    {
                        canvas.StrokeCircle(CreatePen(circle, context), center, circle.Radius);
                    }

                    break;
                }

            case SceneLine line:
                if (line.Stroke is not null)
                {
                    canvas.StrokeLine(CreatePen(line, context), new PointD(line.X1, line.Y1), new PointD(line.X2, line.Y2));
                }

                DrawMarkers(canvas, line, context);
                break;

            case ScenePath path:
                {
                    if (ToVectorPath(path.Data) is not { } vector)
                        break;

                    if (path.Fill is { } fill)
                    {
                        canvas.Fill(context.GetBrush(fill), vector, path.FillRule is SceneFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
                    }

                    if (path.Stroke is not null)
                    {
                        canvas.Stroke(CreatePen(path, context), vector);
                    }

                    DrawMarkers(canvas, path, context);
                    break;
                }

            case SceneText text:
                DrawText(canvas, text, context);
                break;

            case SceneImage image:
                DrawImage(canvas, image);
                break;

            // A use draws its content again; a use in its own content draws nothing
            case SceneUse use when !use.IsCircular() && context.EnterUse(use):
                try
                {
                    DrawElement(canvas, use.Content, context);
                }
                finally
                {
                    context.ExitUse(use);
                }

                break;
        }
    }

    // The content of each marker is drawn on its vertex, after the shape, as a group with its own transform and effects. A marker drawn on a shape of its own content
    // is not drawn again.
    private static void DrawMarkers(DrawingCanvas canvas, SceneShape shape, DrawContext context)
    {
        foreach (var (marker, vertex, isStart) in GetMarkers(shape))
        {
            var transform = marker.GetTransform(vertex, shape.StrokeWidth, isStart);
            if (!IsFinite(transform) || (transform.A * transform.D) - (transform.B * transform.C) == 0 || !context.EnterMarker(marker))
                continue;

            try
            {
                canvas.Transform(ToTransform(transform), (marker, context), static (canvas, state) => DrawElement(canvas, state.marker.Content, state.context));
            }
            finally
            {
                context.ExitMarker(marker);
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

    private static void DrawGroup(DrawingCanvas canvas, SceneGroup group, DrawContext context)
    {
        if (group.Clip is { } clip)
        {
            canvas.Clip(new RectangleD(clip.X, clip.Y, clip.Width, clip.Height), (group, context), static (canvas, state) => DrawClipPath(canvas, state.group, state.context));
            return;
        }

        DrawClipPath(canvas, group, context);
    }

    private static void DrawClipPath(DrawingCanvas canvas, SceneGroup group, DrawContext context)
    {
        if (group.ClipPath is { } clipPath)
        {
            // An empty clip path leaves nothing visible, as in SVG
            if (ToVectorPath(clipPath) is not { } vector)
                return;

            canvas.Clip(vector, (group, context), static (canvas, state) => DrawGroupFiltered(canvas, state.group, state.context), group.ClipRule is SceneFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
            return;
        }

        DrawGroupFiltered(canvas, group, context);
    }

    private static void DrawGroupFiltered(DrawingCanvas canvas, SceneGroup group, DrawContext context)
    {
        DrawFiltered(canvas, group, context, static (canvas, state) => DrawTransformed(canvas, (SceneGroup)state.element, state.context));
    }

    private static void DrawImage(DrawingCanvas canvas, SceneImage image)
    {
        if (!(image.Width > 0) || !(image.Height > 0) || !double.IsFinite(image.X) || !double.IsFinite(image.Y) || !double.IsFinite(image.Width) || !double.IsFinite(image.Height))
            return;

        // Each draw decodes its own image: drawing takes an exclusive lease on the pixels of its source, so an image
        // shared by concurrent renders would fail. The canvas reads the pixels during the call.
        Image decoded;
        try
        {
            decoded = Image.Load(image.Data.Span);
        }
        catch (ImageException)
        {
            // An image that cannot be decoded is not drawn
            return;
        }

        using (decoded)
        {
            canvas.DrawImage(decoded.Frames[0], new RectangleD(image.X, image.Y, image.Width, image.Height));
        }
    }

    private static void DrawTransformed(DrawingCanvas canvas, SceneGroup group, DrawContext context)
    {
        if (group.Transform is { } transform)
        {
            canvas.Transform(new AffineTransform(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F), (group, context), static (canvas, state) => DrawViewBox(canvas, state.group, state.context));
            return;
        }

        DrawViewBox(canvas, group, context);
    }

    // A view box cuts the children to its viewport and maps them to it; a view box that draws nothing has no children
    private static void DrawViewBox(DrawingCanvas canvas, SceneGroup group, DrawContext context)
    {
        if (group.ViewBox is not { } viewBox)
        {
            DrawChildren(canvas, group, context);
            return;
        }

        if (viewBox.GetTransform() is not { } transform)
            return;

        var viewport = viewBox.Viewport;
        canvas.Clip(new RectangleD(viewport.X, viewport.Y, viewport.Width, viewport.Height), (group, context, transform), static (canvas, state) =>
            canvas.Transform(ToTransform(state.transform), (state.group, state.context), static (canvas, state) => DrawChildren(canvas, state.group, state.context)));
    }

    private static void DrawText(DrawingCanvas canvas, SceneText text, DrawContext context)
    {
        if (CreateTextPlan(text, context) is not { } plan)
            return;

        if (text.Path is { } path)
        {
            DrawTextOnPath(canvas, text, path, plan);
            return;
        }

        if (text.Rotation == 0)
        {
            DrawTextParts(canvas, text, plan);
            return;
        }

        canvas.Transform(AffineTransform.CreateRotation(text.Rotation, new PointD(text.X, text.Y)), (text, plan), static (canvas, state) => DrawTextParts(canvas, state.text, state.plan));
    }

    // The parts follow the path from where TextPlacement puts them, their baseline moved across it by their shifts. The
    // characters beyond the ends of the path are not drawn, as in SVG.
    private static void DrawTextOnPath(DrawingCanvas canvas, SceneText text, PathData path, TextPlan plan)
    {
        if (ToVectorPath(path) is not { } vector)
            return;

        var strokeFirst = TextPlacement.IsStrokeFirst(text.PaintOrder);
        for (var i = 0; i < plan.Parts.Count; i++)
        {
            var part = plan.Parts[i];
            var (start, offset) = (plan.Placement.X[i], -plan.Placement.Y[i]);
            if (!(Math.Abs(start) <= MaxPathOffset) || !(Math.Abs(offset) <= MaxPathOffset))
                continue;

            var options = new TextPathOptions { StartOffset = start, BaselineOffset = offset, Overflow = TextPathOverflow.Clip };
            if (strokeFirst && part.Stroke is { } firstStroke)
            {
                canvas.StrokeText(firstStroke, part.Layout, vector, options);
            }

            canvas.FillText(part.Fill, part.Layout, vector, options);
            if (!strokeFirst && part.Stroke is { } lastStroke)
            {
                canvas.StrokeText(lastStroke, part.Layout, vector, options);
            }
        }
    }

    // The largest distance along a path of the canvas, and the largest coordinate of a text
    private const double MaxPathOffset = 16777216;

    // The scene positions a text by its baseline; a layout is drawn from its top-left corner. Each part is drawn where
    // TextPlacement puts it, scaled to the length of the text and turned around its origin on the baseline.
    private static void DrawTextParts(DrawingCanvas canvas, SceneText text, TextPlan plan)
    {
        var strokeFirst = TextPlacement.IsStrokeFirst(text.PaintOrder);
        var scale = plan.Placement.Scale;
        for (var i = 0; i < plan.Parts.Count; i++)
        {
            var part = plan.Parts[i];
            var (x, baseline) = (plan.Placement.X[i], plan.Placement.Y[i]);
            if (!(Math.Abs(x) <= MaxPathOffset) || !(Math.Abs(baseline) <= MaxPathOffset))
                continue;

            var origin = new PointD(x, baseline - part.Layout.Baseline);
            if (part.Rotation == 0 && scale == 1)
            {
                DrawTextPart(canvas, part, origin, strokeFirst);
                continue;
            }

            var center = new PointD(x, baseline);
            var transform = AffineTransform.CreateScale(scale, 1, center).Then(AffineTransform.CreateRotation(part.Rotation, center));
            canvas.Transform(transform, (part, origin, strokeFirst), static (canvas, state) => DrawTextPart(canvas, state.part, state.origin, state.strokeFirst));
        }
    }

    // The fill draws the lines of the text too; the outline is drawn before or after it
    private static void DrawTextPart(DrawingCanvas canvas, TextPart part, PointD origin, bool strokeFirst)
    {
        if (strokeFirst && part.Stroke is { } firstStroke)
        {
            canvas.StrokeText(firstStroke, part.Layout, origin);
        }

        canvas.FillText(part.Fill, part.Layout, origin);
        if (!strokeFirst && part.Stroke is { } lastStroke)
        {
            canvas.StrokeText(lastStroke, part.Layout, origin);
        }
    }

    /// <summary>A part of a text: characters that one layout draws, with the paint of their glyphs, their outline and the rotation of their glyph.</summary>
    private sealed record TextPart(TextLayout Layout, Brush Fill, Pen? Stroke, double Rotation);

    /// <summary>The parts of a text, and where <see cref="TextPlacement"/> puts them.</summary>
    private sealed record TextPlan(List<TextPart> Parts, TextPlacement.Result Placement);

    /// <summary>The characters of a span drawn with one layout, before they are laid out.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TextPiece(int Span, string Text, SceneFont Font, SceneTextDecorations Decorations, Spacing Spacing, Brush Fill, Brush? DecorationFill, Pen? Stroke, double Rotation);

    private static readonly TextLayoutOptions RightToLeftOptions = new() { Direction = TextDirection.RightToLeft };

    /// <summary>
    /// Lays out a text: one layout per span, each with its own style, so that no kerning crosses the spans; one per
    /// grapheme cluster of a span whose characters are turned; the last cluster of a text fitted to its length by its
    /// spacing on its own, as it gets no spacing.
    /// </summary>
    private static TextPlan? CreateTextPlan(SceneText text, DrawContext context)
    {
        if (text.Text.Length is 0)
            return null;

        var pieces = new List<TextPiece>();
        if (text.Spans.Count is 0)
        {
            AddPieces(-1, null, text.Text, text.Font, text.Decorations, new Spacing(text.LetterSpacing, text.WordSpacing));
        }
        else
        {
            for (var i = 0; i < text.Spans.Count; i++)
            {
                var span = text.Spans[i];
                var font = span.Font ?? text.Font;
                if (span.Text.Length > 0 && font.Size > 0 && double.IsFinite(font.Size))
                {
                    AddPieces(i, span, span.Text, font, span.Decorations ?? text.Decorations, new Spacing(span.LetterSpacing ?? text.LetterSpacing, span.WordSpacing ?? text.WordSpacing));
                }
            }
        }

        if (pieces.Count is 0)
            return null;

        if (TextPlacement.IsFittedBySpacing(text) && TextPlacement.GetClusters(pieces[^1].Text) is { Count: > 1 } lastClusters)
        {
            var last = pieces[^1];
            pieces[^1] = last with { Text = last.Text[..^lastClusters[^1].Length] };
            pieces.Add(last with { Text = lastClusters[^1] });
        }

        var direction = text.Direction is SceneTextDirection.RightToLeft ? TextDirection.RightToLeft : TextDirection.Auto;
        var layouts = new TextLayout[pieces.Count];
        var items = new TextPlacement.Item[pieces.Count];
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            layouts[i] = CreateLayout(piece, gapSpacing: 0, direction, context);
            var metrics = GetFonts(piece.Font, context).Primary.GetMetrics(piece.Font.Size);
            items[i] = new TextPlacement.Item(piece.Span, layouts[i].Width, TextPlacement.CountClusters(piece.Text), TextPlacement.GetBaselineOffset(text.Baseline, metrics.Ascent, metrics.Descent, metrics.XHeight));
        }

        var placement = TextPlacement.Place(text, items);
        var parts = new List<TextPart>(pieces.Count);
        for (var i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            var layout = placement.GapSpacing != 0 && i != placement.LastItem ? CreateLayout(piece, placement.GapSpacing, direction, context) : layouts[i];
            parts.Add(new TextPart(layout, piece.Fill, piece.Stroke, piece.Rotation));
        }

        return new TextPlan(parts, placement);

        void AddPieces(int index, SceneTextSpan? span, string value, SceneFont font, SceneTextDecorations decorations, Spacing spacing)
        {
            var (fillPaint, fillColor) = TextPlacement.GetFill(text, span);
            var fill = fillPaint is null ? ToBrush(fillColor) : context.GetBrush(fillPaint);
            var (decorationPaint, decorationColor) = TextPlacement.GetDecorationFill(text, span);
            var decorationFill = TextPlacement.IsSamePaint((decorationPaint, decorationColor), (fillPaint, fillColor))
                ? null
                : decorationPaint is null ? ToBrush(decorationColor) : context.GetBrush(decorationPaint);
            var stroke = TextPlacement.GetStroke(text, span) is { } strokePaint ? CreateTextPen(text, strokePaint, context) : null;
            var rotation = text.Path is null ? TextPlacement.GetGlyphRotation(span) : 0;
            if (rotation == 0)
            {
                pieces.Add(new TextPiece(index, value, font, decorations, spacing, fill, decorationFill, stroke, rotation));
                return;
            }

            foreach (var cluster in TextPlacement.GetClusters(value))
            {
                pieces.Add(new TextPiece(index, cluster, font, decorations, spacing, fill, decorationFill, stroke, rotation));
            }
        }
    }

    private static TextLayout CreateLayout(TextPiece piece, double gapSpacing, TextDirection direction, DrawContext context)
    {
        var decorations = ToDecorations(piece.Decorations);
        var spacing = gapSpacing == 0 ? piece.Spacing : new Spacing(piece.Spacing.Letter + gapSpacing, piece.Spacing.Word);
        if (context.Fonts is null && decorations is TextDecorations.None && spacing.IsNone && piece.DecorationFill is null && direction is TextDirection.Auto)
            return TextLayout.Create(piece.Text, context.Options.GetFont(piece.Font), piece.Font.Size);

        var style = CreateStyle(GetFonts(piece.Font, context), piece.Font.Size, decorations, spacing, piece.DecorationFill);
        return TextLayout.Create(piece.Text, style, direction is TextDirection.RightToLeft ? RightToLeftOptions : null);
    }

    private static FontStack GetFonts(SceneFont font, DrawContext context) => context.Fonts is { } collection ? SceneFontCache.GetStack(collection, font, context.FontFamily) : context.Options.GetFont(font);

    // A letter spacing turns the optional ligatures off, as in CSS: the space goes between the letters they would join
    private static readonly FontFeature[] NoLigatures = [new("liga", 0), new("clig", 0), new("dlig", 0)];

    /// <summary>Creates the style of a text: <see cref="ImageTextMeasurer"/> measures the texts with the styles that draw them.</summary>
    internal static TextStyle CreateStyle(FontStack fonts, double size, TextDecorations decorations, Spacing spacing, Brush? decorationBrush = null)
    {
        return new TextStyle(fonts, size)
        {
            Decorations = decorations,
            DecorationBrush = decorationBrush,
            LetterSpacing = spacing.Letter,
            WordSpacing = spacing.Word,
            Features = spacing.Letter is 0 ? [] : NoLigatures,
        };
    }

    private static TextDecorations ToDecorations(SceneTextDecorations decorations)
    {
        return (decorations.HasFlag(SceneTextDecorations.Underline) ? TextDecorations.Underline : TextDecorations.None)
            | (decorations.HasFlag(SceneTextDecorations.Strikethrough) ? TextDecorations.Strikethrough : TextDecorations.None)
            | (decorations.HasFlag(SceneTextDecorations.Overline) ? TextDecorations.Overline : TextDecorations.None);
    }

    /// <summary>The letter spacing and the word spacing of a text: 0 when they are not finite, within the limits of <see cref="TextStyle"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Spacing
    {
        private const double MaxSpacing = 65536;

        public Spacing(double letter, double word)
        {
            Letter = Normalize(letter);
            Word = Normalize(word);
        }

        public double Letter { get; }

        public double Word { get; }

        public bool IsNone => Letter is 0 && Word is 0;

        private static double Normalize(double value) => double.IsFinite(value) ? Math.Clamp(value, -MaxSpacing, MaxSpacing) : 0;
    }

    private static Pen CreatePen(SceneShape shape, DrawContext context)
    {
        return CreatePen(context.GetBrush(shape.Stroke!), shape.StrokeWidth, shape.StrokeDashArray, shape.StrokeDashOffset, shape.MiterLimit, shape.LineCap, shape.LineJoin);
    }

    // The outline of the glyphs of a text, with the width, the dashes and the joins of the text
    private static Pen CreateTextPen(SceneText text, ScenePaint stroke, DrawContext context)
    {
        return CreatePen(context.GetBrush(stroke), text.StrokeWidth, text.StrokeDashArray, text.StrokeDashOffset, text.MiterLimit, text.LineCap, text.LineJoin);
    }

    private static Pen CreatePen(Brush brush, double width, IReadOnlyList<double>? dashArray, double dashOffset, double miterLimit, SceneLineCap lineCap, SceneLineJoin lineJoin)
    {
        return new Pen(brush, width)
        {
            DashPattern = dashArray is { Count: > 0 } dashes ? [.. dashes] : default,
            DashOffset = double.IsFinite(dashOffset) ? dashOffset : 0,
            MiterLimit = miterLimit >= 1 && double.IsFinite(miterLimit) ? Math.Min(miterLimit, 1048576) : 4,
            LineCap = lineCap switch
            {
                SceneLineCap.Round => LineCap.Round,
                SceneLineCap.Square => LineCap.Square,
                _ => LineCap.Butt,
            },
            LineJoin = lineJoin switch
            {
                SceneLineJoin.Round => LineJoin.Round,
                SceneLineJoin.Bevel => LineJoin.Bevel,
                _ => LineJoin.Miter,
            },
        };
    }

    // A radius of 0 paints the color of the last stop, as in SVG; an invalid gradient paints nothing
    private static Brush CreateRadialBrush(RadialGradientPaint gradient)
    {
        if (gradient.Stops.Count is 0 || !double.IsFinite(gradient.CenterX) || !double.IsFinite(gradient.CenterY) || !double.IsFinite(gradient.FocalX) || !double.IsFinite(gradient.FocalY) || !double.IsFinite(gradient.Radius) || gradient.Radius < 0)
            return new SolidBrush(new Rgba32(0, 0, 0, 0));

        if (gradient.Radius == 0)
            return ToBrush(gradient.Stops[^1].Color);

        return new RadialGradientBrush(
            new PointD(gradient.CenterX, gradient.CenterY),
            gradient.Radius,
            [.. gradient.Stops.Select(stop => new GradientStop(stop.Offset, Rgba64.FromRgba32(new Rgba32(stop.Color.R, stop.Color.G, stop.Color.B, stop.Color.A))))])
        {
            FocalPoint = new PointD(gradient.FocalX, gradient.FocalY),
            SpreadMode = ToSpreadMode(gradient.Spread),
            Transform = ToTransform(gradient.Transform),
        };
    }

    private static GradientSpreadMode ToSpreadMode(SceneGradientSpread spread) => spread switch
    {
        SceneGradientSpread.Repeat => GradientSpreadMode.Repeat,
        SceneGradientSpread.Reflect => GradientSpreadMode.Reflect,
        _ => GradientSpreadMode.Pad,
    };

    private static AffineTransform ToTransform(SceneTransform? transform) => transform is { } value ? new AffineTransform(value.A, value.B, value.C, value.D, value.E, value.F) : AffineTransform.Identity;

    internal sealed class DrawContext(SceneImageOptions options, Dictionary<ScenePaint, Brush> brushes)
    {
        public SceneImageOptions Options { get; } = options;

        /// <summary>Gets the fonts that draw the texts, or <see langword="null"/> to draw them with the fonts of each style of <see cref="Options"/>.</summary>
        public SceneFontCollection? Fonts { get; init; }

        /// <summary>Gets the family of the texts that name none (<see cref="Scene.FontFamily"/>).</summary>
        public string? FontFamily { get; init; }

        // The patterns whose content is being drawn: a pattern cannot paint with itself; the same for the markers
        private readonly HashSet<PatternPaint> _drawingPatterns = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<SceneMarker> _drawingMarkers = new(ReferenceEqualityComparer.Instance);

        private readonly HashSet<SceneUse> _drawingUses = new(ReferenceEqualityComparer.Instance);

        public bool EnterUse(SceneUse use) => _drawingUses.Add(use);

        public void ExitUse(SceneUse use) => _drawingUses.Remove(use);

        public bool EnterMarker(SceneMarker marker) => _drawingMarkers.Add(marker);

        public void ExitMarker(SceneMarker marker) => _drawingMarkers.Remove(marker);

        public Brush GetBrush(ScenePaint paint)
        {
            if (paint is PatternPaint drawing && _drawingPatterns.Contains(drawing))
                return Transparent;

            if (brushes.TryGetValue(paint, out var brush))
                return brush;

            brush = paint switch
            {
                SolidPaint solid => ToBrush(solid.Color),
                PatternPaint pattern => CreatePatternBrush(pattern),
                RadialGradientPaint radial => CreateRadialBrush(radial),
                LinearGradientPaint gradient => new LinearGradientBrush(
                    new PointD(gradient.X1, gradient.Y1),
                    new PointD(gradient.X2, gradient.Y2),
                    [.. gradient.Stops.Select(stop => new GradientStop(stop.Offset, Rgba64.FromRgba32(new Rgba32(stop.Color.R, stop.Color.G, stop.Color.B, stop.Color.A))))])
                {
                    SpreadMode = ToSpreadMode(gradient.Spread),
                    Transform = ToTransform(gradient.Transform),
                },
                _ => throw new NotSupportedException($"The paint '{paint.GetType()}' is not supported."),
            };
            brushes.Add(paint, brush);
            return brush;
        }

        // The largest magnitude of a coordinate of the tile of a pattern brush
        private const double MaxTileCoordinate = 16777216;

        // A tile without area, or out of the limits of the canvas, paints nothing
        private Brush CreatePatternBrush(PatternPaint pattern)
        {
            var tile = pattern.Tile;
            ReadOnlySpan<double> coordinates = [tile.X, tile.Y, tile.Width, tile.Height, tile.Right, tile.Bottom];
            foreach (var coordinate in coordinates)
            {
                if (!double.IsFinite(coordinate) || Math.Abs(coordinate) > MaxTileCoordinate)
                    return Transparent;
            }

            if (!(tile.Width > 0 && tile.Height > 0) || (pattern.Transform is { } transform && !IsFinite(transform)))
                return Transparent;

            return new PatternBrush(new RectangleD(tile.X, tile.Y, tile.Width, tile.Height), canvas => DrawPattern(canvas, pattern))
            {
                Transform = ToTransform(pattern.Transform),
            };
        }

        private void DrawPattern(DrawingCanvas canvas, PatternPaint pattern)
        {
            if (!_drawingPatterns.Add(pattern))
                return;

            try
            {
                DrawElement(canvas, pattern.Content, this);
            }
            finally
            {
                _drawingPatterns.Remove(pattern);
            }
        }
    }
}
