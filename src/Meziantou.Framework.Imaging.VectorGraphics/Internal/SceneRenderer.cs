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

        DrawFiltered(canvas, element, context, static (canvas, state) => DrawContent(canvas, state.element, state.context));
    }

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
                    if (GetBounds(element, context) is not { } region)
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
    private static RectangleD? GetBounds(SceneElement element, DrawContext context)
    {
        var bounds = element.GetBounds(text =>
        {
            // The box of each part, and the ink of its glyphs, which overhang their advance (italic letters) and rise
            // above the ascent (marks), at the position the part is drawn
            var parts = CreateTextParts(text, context);
            if (parts.Count is 0)
                return null;

            var width = parts.Sum(part => part.Layout.Width);
            var x = text.Anchor switch
            {
                SceneTextAnchor.Middle => text.X - (width / 2),
                SceneTextAnchor.End => text.X - width,
                _ => text.X,
            };

            RectangleD? area = null;
            foreach (var (layout, _) in parts)
            {
                var origin = new PointD(x, text.Y - layout.Baseline);
                var box = new RectangleD(origin.X, origin.Y, layout.Width, layout.Height);
                if (!layout.InkBounds.IsEmpty)
                {
                    box = RectangleD.Union(box, layout.InkBounds.Offset(origin.X, origin.Y));
                }

                area = area is { } union ? RectangleD.Union(union, box) : box;
                x += layout.Width;
            }

            return area is { } value ? new SceneBounds(value.X, value.Y, value.Width, value.Height) : null;
        });

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

                    break;
                }

            case SceneText text:
                DrawText(canvas, text, context);
                break;

            case SceneImage image:
                DrawImage(canvas, image);
                break;
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
            canvas.Transform(new AffineTransform(transform.A, transform.B, transform.C, transform.D, transform.E, transform.F), (group, context), static (canvas, state) => DrawChildren(canvas, state.group, state.context));
            return;
        }

        DrawChildren(canvas, group, context);
    }

    private static void DrawText(DrawingCanvas canvas, SceneText text, DrawContext context)
    {
        var parts = CreateTextParts(text, context);
        if (parts.Count is 0)
            return;

        var width = parts.Sum(part => part.Layout.Width);
        var x = text.Anchor switch
        {
            SceneTextAnchor.Middle => text.X - (width / 2),
            SceneTextAnchor.End => text.X - width,
            _ => text.X,
        };

        if (text.Rotation == 0)
        {
            DrawTextParts(canvas, parts, x, text.Y);
            return;
        }

        canvas.Transform(AffineTransform.CreateRotation(text.Rotation, new PointD(text.X, text.Y)), (parts, x, text.Y), static (canvas, state) => DrawTextParts(canvas, state.parts, state.x, state.Y));
    }

    // The scene positions a text by its baseline; a layout is drawn from its top-left corner. The parts are drawn one
    // after the other, as the measurers measure them.
    private static void DrawTextParts(DrawingCanvas canvas, List<(TextLayout Layout, Brush Brush)> parts, double x, double baseline)
    {
        foreach (var (layout, brush) in parts)
        {
            canvas.FillText(brush, layout, new PointD(x, baseline - layout.Baseline));
            x += layout.Width;
        }
    }

    /// <summary>Lays out a text: one layout, or one per span, each with its own style, so that no kerning crosses the spans.</summary>
    private static List<(TextLayout Layout, Brush Brush)> CreateTextParts(SceneText text, DrawContext context)
    {
        var parts = new List<(TextLayout Layout, Brush Brush)>();
        if (text.Text.Length is 0)
            return parts;

        if (text.Spans.Count is 0)
        {
            var decorations = ToDecorations(text.Decorations);
            var layout = context.Fonts is null && decorations is TextDecorations.None
                ? TextLayout.Create(text.Text, context.Options.GetFont(text.Font), text.Font.Size)
                : TextLayout.Create(text.Text, CreateStyle(text.Font, decorations, context));
            parts.Add((layout, ToBrush(text.Color)));
            return parts;
        }

        foreach (var span in text.Spans)
        {
            var font = span.Font ?? text.Font;
            if (span.Text.Length > 0 && font.Size > 0 && double.IsFinite(font.Size))
            {
                parts.Add((TextLayout.Create(span.Text, CreateStyle(font, ToDecorations(span.Decorations ?? text.Decorations), context)), ToBrush(span.Color ?? text.Color)));
            }
        }

        return parts;
    }

    private static TextStyle CreateStyle(SceneFont font, TextDecorations decorations, DrawContext context, Brush? brush = null)
    {
        FontStack fonts = context.Fonts is { } collection ? SceneFontCache.GetStack(collection, font, context.FontFamily) : context.Options.GetFont(font);
        return new TextStyle(fonts, font.Size) { Decorations = decorations, Brush = brush };
    }

    private static TextDecorations ToDecorations(SceneTextDecorations decorations)
    {
        return (decorations.HasFlag(SceneTextDecorations.Underline) ? TextDecorations.Underline : TextDecorations.None)
            | (decorations.HasFlag(SceneTextDecorations.Strikethrough) ? TextDecorations.Strikethrough : TextDecorations.None);
    }

    private static Pen CreatePen(SceneShape shape, DrawContext context)
    {
        return new Pen(context.GetBrush(shape.Stroke!), shape.StrokeWidth)
        {
            DashPattern = shape.StrokeDashArray is { Count: > 0 } dashes ? [.. dashes] : default,
            DashOffset = double.IsFinite(shape.StrokeDashOffset) ? shape.StrokeDashOffset : 0,
            MiterLimit = shape.MiterLimit >= 1 && double.IsFinite(shape.MiterLimit) ? Math.Min(shape.MiterLimit, 1048576) : 4,
            LineCap = shape.LineCap switch
            {
                SceneLineCap.Round => LineCap.Round,
                SceneLineCap.Square => LineCap.Square,
                _ => LineCap.Butt,
            },
            LineJoin = shape.LineJoin switch
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

        public Brush GetBrush(ScenePaint paint)
        {
            if (brushes.TryGetValue(paint, out var brush))
                return brush;

            brush = paint switch
            {
                SolidPaint solid => ToBrush(solid.Color),
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
    }
}
