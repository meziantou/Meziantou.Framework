using Meziantou.Framework.WebPlatform.Rendering;
using AffineTransform = Meziantou.Framework.Imaging.AffineTransform;
using D = Meziantou.Framework.Imaging.Drawing;
using RectangleD = Meziantou.Framework.Imaging.RectangleD;
using Rgba64 = Meziantou.Framework.Imaging.Rgba64;

namespace Meziantou.Framework.WebPlatform.Imaging;

internal sealed partial class DisplayListRasterizer
{
    /// <summary>The number of stops added between two stops whose alphas differ, for the gradients interpolated without premultiplied alpha.</summary>
    private const int StraightAlphaSteps = 16;

    /// <summary>
    /// Creates the brush of a paint, or returns null when nothing is painted (e.g. an image shader whose image cannot be
    /// decoded). The alpha of the color of a paint with a shader is the opacity of the shader.
    /// </summary>
    private D.Brush? CreateBrush(DisplayPaint paint)
    {
        var (composite, blend) = GetModes(paint.BlendMode);
        if (paint.Shader is not { } shader)
            return new D.SolidBrush(ColorSpaces.ToRgba64(paint.Color, _colorSpace)) { CompositeMode = composite, BlendMode = blend };

        var opacity = float.IsNaN(paint.Color.A) ? 0 : Math.Clamp(paint.Color.A, 0, 1);
        try
        {
            return CreateShaderBrush(shader, opacity, composite, blend);
        }
        catch (ArgumentException)
        {
            // A shader the drawing API cannot paint (e.g. a gradient whose geometry is degenerate) paints nothing
            return null;
        }
    }

    private D.Brush? CreateShaderBrush(DisplayShader shader, double opacity, D.CompositeMode composite = D.CompositeMode.SourceOver, D.BlendMode blend = D.BlendMode.Normal)
    {
        var transform = shader.LocalTransform is { Matrix3d: null } local ? PathConversions.ToAffine(local) : AffineTransform.Identity;
        switch (shader)
        {
            case LinearGradientShader linear:
                return new D.LinearGradientBrush(ToPoint(linear.Start), ToPoint(linear.End), GetStops(linear))
                {
                    SpreadMode = ToSpreadMode(linear.Spread),
                    Transform = transform,
                    Opacity = opacity,
                    CompositeMode = composite,
                    BlendMode = blend,
                };

            case RadialGradientShader radial:
                return new D.RadialGradientBrush(ToPoint(radial.EndCenter), Math.Max(0, radial.EndRadius), GetStops(radial))
                {
                    FocalPoint = ToPoint(radial.StartCenter),
                    FocalRadius = Math.Max(0, radial.StartRadius),
                    SpreadMode = ToSpreadMode(radial.Spread),
                    Transform = transform,
                    Opacity = opacity,
                    CompositeMode = composite,
                    BlendMode = blend,
                };

            case ConicGradientShader conic:
            {
                // The start angle is in radians, clockwise from the x axis (the y axis points down)
                var start = double.IsFinite(conic.StartAngle) ? conic.StartAngle * 180 / Math.PI : 0;
                return new D.SweepGradientBrush(ToPoint(conic.Center), start, start + 360, GetStops(conic))
                {
                    SpreadMode = ToSpreadMode(conic.Spread),
                    Transform = transform,
                    Opacity = opacity,
                    CompositeMode = composite,
                    BlendMode = blend,
                };
            }

            case ImageShader imageShader:
            {
                var image = _backend.GetImage(imageShader.Image);
                if (image is null)
                    return null;

                // The shader is in the natural pixels of the image, which are scaled to its decoded pixels
                var (pixelScaleX, pixelScaleY) = GetPixelScale(imageShader.Image, image);
                var scale = GetScaleFactors();
                var (localScaleX, localScaleY) = GetScaleFactors(transform);
                var deviceScaleX = scale.X * localScaleX / pixelScaleX;
                var deviceScaleY = scale.Y * localScaleY / pixelScaleY;
                var frame = image.GetFrame(_colorSpace);
                var toNatural = pixelScaleX != 1 || pixelScaleY != 1 ? new AffineTransform(1 / pixelScaleX, 0, 0, 1 / pixelScaleY, 0, 0) : AffineTransform.Identity;
                Meziantou.Framework.Imaging.Rectangle? region = null;
                if (imageShader.SourceRect is { } sourceRect)
                {
                    // The pixels of the area, at their position in the image (the pixels around the area are not sampled)
                    var left = Math.Clamp((int)Math.Floor(sourceRect.Left * pixelScaleX), 0, image.Width);
                    var top = Math.Clamp((int)Math.Floor(sourceRect.Top * pixelScaleY), 0, image.Height);
                    var right = Math.Clamp((int)Math.Ceiling(sourceRect.Right * pixelScaleX), 0, image.Width);
                    var bottom = Math.Clamp((int)Math.Ceiling(sourceRect.Bottom * pixelScaleY), 0, image.Height);
                    if (right <= left || bottom <= top)
                        return null;

                    region = new Meziantou.Framework.Imaging.Rectangle(left, top, right - left, bottom - top);
                }

                return new D.ImageBrush(frame)
                {
                    Transform = toNatural.Then(transform),
                    SourceRegion = region,
                    WrapModeX = ToWrapMode(imageShader.SpreadX),
                    WrapModeY = ToWrapMode(imageShader.SpreadY),
                    Sampling = GetSampling(imageShader.Sampling, deviceScaleX, deviceScaleY),
                    Opacity = opacity,
                    CompositeMode = composite,
                    BlendMode = blend,
                };
            }

            case TiledShader tiled:
            {
                var width = tiled.TileSize.Width;
                var height = tiled.TileSize.Height;
                if (!IsTileSize(width) || !IsTileSize(height))
                    return null;

                var content = tiled.Content;
                if (content is not DisplayListShader { LocalTransform: null } && CreateShaderBrush(content, 1) is null)
                    return null;

                var tileSize = tiled.TileSize;
                return CreatePattern(new RectangleD(0, 0, width, height), Math.Max(0, PathConversions.Clamp(tiled.Spacing.Width)), Math.Max(0, PathConversions.Clamp(tiled.Spacing.Height)), ToWrapMode(tiled.SpreadX), ToWrapMode(tiled.SpreadY), transform, canvas => DrawTile(canvas, content, tileSize), opacity, composite, blend);
            }

            case DisplayListShader list:
            {
                // The display list in a rectangle of its size (transparent outside of it)
                if (!IsTileSize(list.Size.Width) || !IsTileSize(list.Size.Height))
                    return null;

                var content = list.Content;
                return CreatePattern(new RectangleD(0, 0, list.Size.Width, list.Size.Height), 0, 0, D.ImageWrapMode.Transparent, D.ImageWrapMode.Transparent, transform, canvas => DrawNested(canvas, content, DisplayMatrix.Identity), opacity, composite, blend, list.IsCanvasRecording);
            }

            case FilteredShader filtered:
            {
                // The filter() image function: the content in a rectangle of its size, filtered, and clipped to the rectangle
                if (!IsTileSize(filtered.Size.Width) || !IsTileSize(filtered.Size.Height))
                    return null;

                var content = CreateShaderBrush(filtered.Content, 1);
                if (content is null)
                    return null;

                var filter = CreateFilter(filtered.Filters);
                var rect = new RectangleD(0, 0, filtered.Size.Width, filtered.Size.Height);
                return CreatePattern(rect, 0, 0, D.ImageWrapMode.Transparent, D.ImageWrapMode.Transparent, transform, canvas =>
                {
                    if (filter is null)
                    {
                        canvas.FillRectangle(content, rect);
                        return;
                    }

                    canvas.Filter(filter, canvas => canvas.FillRectangle(content, rect));
                }, opacity, composite, blend);
            }

            default:
                return null;
        }

        static bool IsTileSize(double value) => value > 0 && value <= PathConversions.MaxCoordinate;
    }

    /// <summary>
    /// Creates the brush of a pattern: a tile (and its spacing) drawn by a callback, repeated. The drawing of a canvas
    /// (<paramref name="isCanvasBitmap"/>) is drawn on an image of 8-bit pixels, as the bitmaps of the canvases.
    /// </summary>
    private static D.PatternBrush CreatePattern(RectangleD tile, double spacingX, double spacingY, D.ImageWrapMode wrapX, D.ImageWrapMode wrapY, AffineTransform transform, D.DrawingAction content, double opacity, D.CompositeMode composite, D.BlendMode blend, bool isCanvasBitmap = false)
        => new(tile, content)
        {
            SpacingX = spacingX,
            SpacingY = spacingY,
            WrapModeX = wrapX,
            WrapModeY = wrapY,
            Transform = transform,
            Opacity = opacity,
            CompositeMode = composite,
            BlendMode = blend,
            TilePrecision = isCanvasBitmap ? D.PatternTilePrecision.EightBit : D.PatternTilePrecision.Canvas,
        };

    /// <summary>Draws the content of a tile of a pattern.</summary>
    private void DrawTile(D.DrawingCanvas canvas, DisplayShader content, Css.Layout.PhysicalSize tileSize)
    {
        if (content is DisplayListShader { LocalTransform: null } list && list.Size == tileSize)
        {
            // A display list (an SVG image, an SVG pattern) is drawn in the tile directly
            DrawNested(canvas, list.Content, DisplayMatrix.Identity);
            return;
        }

        if (CreateShaderBrush(content, 1) is { } brush)
        {
            canvas.FillRectangle(brush, new RectangleD(0, 0, tileSize.Width, tileSize.Height));
        }
    }

    /// <summary>The stops of a gradient, in the color space of the bitmap.</summary>
    private D.GradientStop[] GetStops(GradientShader gradient)
    {
        var stops = gradient.Stops;
        if (stops.Length == 0)
            return [new D.GradientStop(0, default(Rgba64)), new D.GradientStop(1, default(Rgba64))];

        var result = new List<D.GradientStop>(stops.Length);
        var previousOffset = 0d;
        for (var i = 0; i < stops.Length; i++)
        {
            // The offsets are between 0 and 1, in increasing order
            var offset = double.IsNaN(stops[i].Offset) ? previousOffset : Math.Clamp(stops[i].Offset, previousOffset, 1);
            var color = stops[i].Color;
            if (!gradient.InterpolatesPremultiplied && i > 0 && result.Count > 0)
            {
                // The colors are interpolated without premultiplied alpha (the canvas and SVG gradients): the drawing API
                // interpolates premultiplied colors, so the interpolation of the straight colors is sampled by more stops
                var previous = stops[i - 1].Color;
                if (previous.A != color.A && offset > previousOffset)
                {
                    for (var step = 1; step < StraightAlphaSteps; step++)
                    {
                        var t = (float)step / StraightAlphaSteps;
                        var mixed = new DisplayColor(Lerp(previous.R, color.R, t), Lerp(previous.G, color.G, t), Lerp(previous.B, color.B, t), Lerp(previous.A, color.A, t));
                        result.Add(new D.GradientStop(previousOffset + ((offset - previousOffset) * t), ColorSpaces.ToRgba64(mixed, _colorSpace)));
                    }
                }
            }

            result.Add(new D.GradientStop(offset, ColorSpaces.ToRgba64(color, _colorSpace)));
            previousOffset = offset;
        }

        return [.. result];

        static float Lerp(float from, float to, float t) => from + ((to - from) * t);
    }

    private static D.GradientSpreadMode ToSpreadMode(SpreadMode spread) => spread switch
    {
        SpreadMode.Repeat => D.GradientSpreadMode.Repeat,
        SpreadMode.Reflect => D.GradientSpreadMode.Reflect,
        SpreadMode.Decal => D.GradientSpreadMode.Transparent,
        _ => D.GradientSpreadMode.Pad,
    };

    private static D.ImageWrapMode ToWrapMode(SpreadMode spread) => spread switch
    {
        SpreadMode.Repeat => D.ImageWrapMode.Repeat,
        SpreadMode.Reflect => D.ImageWrapMode.Reflect,
        SpreadMode.Decal => D.ImageWrapMode.Transparent,
        _ => D.ImageWrapMode.Pad,
    };

    private static (double X, double Y) GetScaleFactors(AffineTransform transform)
        => (Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12)), Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22)));

    /// <summary>The shortest pattern of dashes of the drawing API.</summary>
    private const double MinDashPatternLength = 1d / 1024;

    /// <summary>Creates the pen of a stroke.</summary>
    private static D.Pen CreatePen(D.Brush brush, StrokeStyle stroke)
    {
        var width = double.IsFinite(stroke.Width) && stroke.Width >= 0 ? Math.Min(stroke.Width, PathConversions.MaxCoordinate) : 1;
        double[] dashes = [];
        if (stroke.Dashes is { Length: > 0 } values && Array.TrueForAll(values, static value => double.IsFinite(value) && value >= 0))
        {
            var length = 0d;
            foreach (var value in values)
            {
                length += value;
            }

            if (length >= MinDashPatternLength && double.IsFinite(length))
            {
                dashes = values;
            }
        }

        return new D.Pen(brush, width)
        {
            LineCap = stroke.Cap switch
            {
                LineCap.Round => D.LineCap.Round,
                LineCap.Square => D.LineCap.Square,
                _ => D.LineCap.Butt,
            },
            LineJoin = stroke.Join switch
            {
                LineJoin.Round => D.LineJoin.Round,
                LineJoin.Bevel => D.LineJoin.Bevel,
                _ => D.LineJoin.Miter,
            },
            MiterLimit = double.IsFinite(stroke.MiterLimit) ? Math.Clamp(stroke.MiterLimit, 1, 1_048_576) : 4,
            DashPattern = dashes,
            DashOffset = dashes.Length > 0 && double.IsFinite(stroke.DashOffset) ? stroke.DashOffset : 0,
        };
    }
}
