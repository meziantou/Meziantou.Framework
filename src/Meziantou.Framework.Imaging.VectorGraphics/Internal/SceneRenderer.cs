using Meziantou.Framework.VectorGraphics;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.VectorGraphics.Internal;

/// <summary>Draws the primitives of a <see cref="Scene"/> with the canvas of <c>Meziantou.Framework.Imaging</c>. Hyperlinks are ignored.</summary>
internal static class SceneRenderer
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SceneImage, DecodedImage> DecodedImages = [];

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

    private static void DrawElement(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        if (element.Opacity <= 0)
            return;

        if (element.Opacity < 1)
        {
            canvas.Layer(element.Opacity, (element, context), static (canvas, state) => DrawContent(canvas, state.element, state.context));
            return;
        }

        DrawContent(canvas, element, context);
    }

    private static void DrawContent(DrawingCanvas canvas, SceneElement element, DrawContext context)
    {
        switch (element)
        {
            case SceneGroup group:
                DrawGroup(canvas, group, context);
                break;

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

            canvas.Clip(vector, (group, context), static (canvas, state) => DrawTransformed(canvas, state.group, state.context), group.ClipRule is SceneFillRule.EvenOdd ? FillRule.EvenOdd : FillRule.NonZero);
            return;
        }

        DrawTransformed(canvas, group, context);
    }

    private static void DrawImage(DrawingCanvas canvas, SceneImage image)
    {
        if (!(image.Width > 0) || !(image.Height > 0) || !double.IsFinite(image.X) || !double.IsFinite(image.Y) || !double.IsFinite(image.Width) || !double.IsFinite(image.Height))
            return;

        if (DecodedImages.GetValue(image, static image => Decode(image)).Image is { } decoded)
        {
            canvas.DrawImage(decoded.Frames[0], new RectangleD(image.X, image.Y, image.Width, image.Height));
        }
    }

    // An image that cannot be decoded is not drawn
    private static DecodedImage Decode(SceneImage image)
    {
        try
        {
            return new DecodedImage(Image.Load(image.Data.Span));
        }
        catch (ImageException)
        {
            return new DecodedImage(null);
        }
    }

    private sealed record DecodedImage(Image? Image);

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
        if (text.Text.Length is 0)
            return;

        var layout = context.Fonts is { } fonts
            ? TextLayout.Create(text.Text, new TextStyle(SceneFontCache.GetStack(fonts, text.Font, context.FontFamily), text.Font.Size))
            : TextLayout.Create(text.Text, context.Options.GetFont(text.Font), text.Font.Size);
        var x = text.Anchor switch
        {
            SceneTextAnchor.Middle => text.X - (layout.Width / 2),
            SceneTextAnchor.End => text.X - layout.Width,
            _ => text.X,
        };

        // The scene positions a text by its baseline; a layout is drawn from its top-left corner
        var origin = new PointD(x, text.Y - layout.Baseline);
        var brush = ToBrush(text.Color);
        if (text.Rotation == 0)
        {
            canvas.FillText(brush, layout, origin);
            return;
        }

        canvas.Transform(AffineTransform.CreateRotation(text.Rotation, new PointD(text.X, text.Y)), (brush, layout, origin), static (canvas, state) => canvas.FillText(state.brush, state.layout, state.origin));
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
        };
    }

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
                    [.. gradient.Stops.Select(stop => new GradientStop(stop.Offset, Rgba64.FromRgba32(new Rgba32(stop.Color.R, stop.Color.G, stop.Color.B, stop.Color.A))))]),
                _ => throw new NotSupportedException($"The paint '{paint.GetType()}' is not supported."),
            };
            brushes.Add(paint, brush);
            return brush;
        }
    }
}
