using Meziantou.Framework.Pdf;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

internal sealed partial class DisplayListPdfWriter
{
    /// <summary>The step of a tiling pattern in an axis that is not repeated: the tile is painted once.</summary>
    private const double SingleTileStep = 1_000_000;

    /// <summary>The most repetitions of a repeated gradient over a shape: a shape covering more is rasterized.</summary>
    private const int MaxGradientRepetitions = 256;

    /// <summary>Whether PDF can paint a shader (the others are rasterized).</summary>
    private static bool CanPaint(DisplayShader shader) => shader switch
    {
        LinearGradientShader linear => linear.Stops.Length > 0 && (linear.Start.X != linear.End.X || linear.Start.Y != linear.End.Y),
        RadialGradientShader radial => radial.Stops.Length > 0 && radial.Spread is SpreadMode.Pad && radial.EndRadius >= 0 && radial.StartRadius >= 0,
        ImageShader image => image.SourceRect is null && image.SpreadX is SpreadMode.Repeat or SpreadMode.Decal && image.SpreadY is SpreadMode.Repeat or SpreadMode.Decal && image.Image.Width > 0 && image.Image.Height > 0,
        TiledShader tiled => tiled.SpreadX is not SpreadMode.Reflect && tiled.SpreadY is not SpreadMode.Reflect && tiled.TileSize.Width > 0 && tiled.TileSize.Height > 0 && (tiled.Content is DisplayListShader || CanPaint(tiled.Content)),
        DisplayListShader list => list.Size.Width > 0 && list.Size.Height > 0,
        _ => false,
    };

    /// <summary>
    /// Sets the color or the pattern of a paint for the fill, the stroke or both, and its opacity and blend mode. Returns
    /// false when nothing is painted; <paramref name="restore"/> is true when the graphics state was saved (the caller
    /// restores it after painting).
    /// </summary>
    /// <param name="paint">The paint.</param>
    /// <param name="fill">Whether the fill color is set.</param>
    /// <param name="stroke">Whether the stroke color is set.</param>
    /// <param name="bounds">The bounds of the shape in the coordinates of the items (the area of the soft mask of a gradient with transparent stops), or null for the clip.</param>
    /// <param name="restore">Whether the graphics state was saved.</param>
    private bool BeginPaint(DisplayPaint paint, bool fill, bool stroke, PhysicalRect? bounds, out bool restore)
    {
        restore = false;
        var alpha = float.IsNaN(paint.Color.A) ? 0d : Math.Clamp(paint.Color.A, 0, 1);
        if (alpha <= 0)
            return false;

        PdfPattern? pattern = null;
        PdfSoftMask? softMask = null;
        if (paint.Shader is { } shader)
        {
            var area = bounds ?? GetLocalClip();
            if (area is not { } shapeBounds)
                return false;

            switch (shader)
            {
                case GradientShader gradient:
                {
                    if (CreateGradient(gradient, shapeBounds, _state.ToStream) is not { } created)
                        return false;

                    pattern = created.Pattern;
                    alpha *= created.Alpha;
                    if (created.AlphaPattern is { } alphaPattern)
                    {
                        softMask = CreateSoftMask(alphaPattern, shapeBounds);
                    }

                    break;
                }

                default:
                    pattern = CreateTilingPattern(shader, _state);
                    if (pattern is null)
                        return false;

                    break;
            }
        }

        var blend = ToPdf(paint.BlendMode);
        if (alpha < 1 || blend != PdfBlendMode.Normal || softMask is not null)
        {
            _builder.SaveGraphicsState();
            restore = true;
            _builder.SetGraphicsState(softMask is null ? _owner.GetGraphicsState(alpha, blend) : new PdfExtGState { FillAlpha = alpha, StrokeAlpha = alpha, BlendMode = blend, SoftMask = softMask });
        }

        if (pattern is not null)
        {
            if (fill)
            {
                _builder.SetFillPattern(pattern);
            }

            if (stroke)
            {
                _builder.SetStrokePattern(pattern);
            }
        }
        else
        {
            var color = ToPdf(paint.Color);
            if (fill)
            {
                _builder.SetFillColor(color);
            }

            if (stroke)
            {
                _builder.SetStrokeColor(color);
            }
        }

        return true;
    }

    internal static PdfColor ToPdf(DisplayColor color) => PdfColor.FromRgb(Clamp(color.R), Clamp(color.G), Clamp(color.B));

    private static double Clamp(float value) => float.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    /// <summary>
    /// Creates the shading pattern of a gradient, in a content stream whose default space is <paramref name="toStream"/>
    /// from the coordinates of the items: the colors of its stops, the opacity when every stop has the same alpha, else the
    /// pattern of the alphas of the stops (a soft mask).
    /// </summary>
    private static GradientPatterns? CreateGradient(GradientShader gradient, PhysicalRect shapeBounds, DisplayMatrix toStream)
    {
        var stops = GetStops(gradient);
        if (stops.Count == 0)
            return null;

        var local = gradient.LocalTransform is { Matrix3d: null } transform ? transform : DisplayMatrix.Identity;
        if (gradient is LinearGradientShader linear && linear.Spread is SpreadMode.Repeat or SpreadMode.Reflect)
        {
            // The stops are repeated over the shape, then padded
            if (PdfGeometry.Invert(local) is not { } inverse)
                return null;

            var (start, end, repeated) = Repeat(linear, stops, PdfGeometry.Transform(inverse, shapeBounds));
            if (repeated is null)
                return null;

            linear = linear with { Start = start, End = end, Spread = SpreadMode.Pad };
            gradient = linear;
            stops = repeated;
        }

        var alpha = stops[0].Color.A;
        var uniformAlpha = stops.TrueForAll(stop => stop.Color.A == alpha);
        var colors = CreateFunction(stops, stop => ToPdf(stop.Color));
        var patternMatrix = PdfGeometry.ToPdf(toStream.PreConcat(local));
        var pattern = new PdfShadingPattern(CreateShading(gradient, PdfDeviceColorSpace.Rgb, colors), patternMatrix);
        if (uniformAlpha)
            return new GradientPatterns(pattern, Math.Clamp(alpha, 0, 1), AlphaPattern: null);

        // The alphas of the stops are the gray levels of the same gradient, in the soft mask (in the coordinates of the items)
        var alphas = CreateFunction(stops, stop => PdfColor.FromGray(Math.Clamp(stop.Color.A, 0, 1)));
        var alphaPattern = new PdfShadingPattern(CreateShading(gradient, PdfDeviceColorSpace.Gray, alphas), PdfGeometry.ToPdf(local));
        return new GradientPatterns(pattern, 1, alphaPattern);
    }

    private static PdfShading CreateShading(GradientShader gradient, PdfDeviceColorSpace colorSpace, PdfFunction function)
    {
        var extend = gradient.Spread == SpreadMode.Pad;
        return gradient switch
        {
            LinearGradientShader linear => PdfShading.Axial(colorSpace, linear.Start.X, linear.Start.Y, linear.End.X, linear.End.Y, function, extend, extend),
            RadialGradientShader radial => PdfShading.Radial(colorSpace, radial.StartCenter.X, radial.StartCenter.Y, radial.StartRadius, radial.EndCenter.X, radial.EndCenter.Y, radial.EndRadius, function, extend, extend),
            _ => throw new ArgumentOutOfRangeException(nameof(gradient)),
        };
    }

    private static PdfFunction CreateFunction(List<GradientStop> stops, Func<GradientStop, PdfColor> getColor)
    {
        if (stops.Count == 1)
            return PdfFunction.Gradient((0, getColor(stops[0])), (1, getColor(stops[0])));

        var values = new (double Position, PdfColor Color)[stops.Count];
        for (var i = 0; i < stops.Count; i++)
        {
            values[i] = (stops[i].Offset, getColor(stops[i]));
        }

        return PdfFunction.Gradient(values);
    }

    /// <summary>
    /// Returns the stops of a gradient from the offset 0 to the offset 1, in increasing order. The colors of the transparent
    /// stops are the colors next to them: PDF interpolates the colors and the alphas separately, CSS interpolates them
    /// premultiplied (https://drafts.csswg.org/css-images-4/#coloring-gradient-line), so a transparent stop has no color.
    /// </summary>
    private static List<GradientStop> GetStops(GradientShader gradient)
    {
        var source = gradient.Stops;
        var stops = new List<GradientStop>(source.Length + 2);
        var previous = 0d;
        foreach (var stop in source)
        {
            var offset = double.IsFinite(stop.Offset) ? Math.Clamp(stop.Offset, 0, 1) : 0;

            // The offsets increase: the stitching functions need increasing bounds
            offset = Math.Max(offset, previous);
            stops.Add(stop with { Offset = offset });
            previous = offset;
        }

        if (stops.Count == 0)
            return stops;

        if (stops[0].Offset > 0)
        {
            stops.Insert(0, stops[0] with { Offset = 0 });
        }

        if (stops[^1].Offset < 1)
        {
            stops.Add(stops[^1] with { Offset = 1 });
        }

        if (gradient.InterpolatesPremultiplied)
        {
            var result = new List<GradientStop>(stops.Count);
            for (var i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                if (stop.Color.A > 0)
                {
                    result.Add(stop);
                    continue;
                }

                var before = i > 0 && stops[i - 1].Color.A > 0 ? stops[i - 1].Color : (DisplayColor?)null;
                var after = i + 1 < stops.Count && stops[i + 1].Color.A > 0 ? stops[i + 1].Color : (DisplayColor?)null;
                if (before is { } left && after is { } right && left with { A = 0 } != right with { A = 0 })
                {
                    // Two stops at the same offset: the color on each side
                    result.Add(new GradientStop(stop.Offset, left with { A = 0 }));
                    result.Add(new GradientStop(stop.Offset, right with { A = 0 }));
                }
                else
                {
                    var color = before ?? after ?? stop.Color;
                    result.Add(new GradientStop(stop.Offset, color with { A = 0 }));
                }
            }

            stops = result;
        }

        // Equal bounds of the stitching function: a hard stop, moved by a negligible distance
        for (var i = 1; i < stops.Count - 1; i++)
        {
            if (stops[i].Offset <= stops[i - 1].Offset)
            {
                stops[i] = stops[i] with { Offset = Math.Min(1, stops[i - 1].Offset + 1e-9) };
            }
        }

        return stops;
    }

    /// <summary>
    /// Repeats the stops of a repeating linear gradient over the bounds of a shape (in the coordinates of the gradient):
    /// the gradient line covers whole periods from the first one the shape touches to the last.
    /// </summary>
    private static (PhysicalOffset Start, PhysicalOffset End, List<GradientStop>? Stops) Repeat(LinearGradientShader linear, List<GradientStop> stops, PhysicalRect bounds)
    {
        var dx = linear.End.X - linear.Start.X;
        var dy = linear.End.Y - linear.Start.Y;
        var length = (dx * dx) + (dy * dy);
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        foreach (var (x, y) in new[] { (bounds.Left, bounds.Top), (bounds.Right, bounds.Top), (bounds.Right, bounds.Bottom), (bounds.Left, bounds.Bottom) })
        {
            var t = (((x - linear.Start.X) * dx) + ((y - linear.Start.Y) * dy)) / length;
            min = Math.Min(min, t);
            max = Math.Max(max, t);
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
            return default;

        var first = Math.Floor(min);
        var last = Math.Max(first + 1, Math.Ceiling(max));
        var count = last - first;
        if (count > MaxGradientRepetitions)
            return default;

        var result = new List<GradientStop>((int)count * stops.Count);
        for (var period = 0; period < count; period++)
        {
            var reflected = linear.Spread == SpreadMode.Reflect && ((long)(first + period) & 1) != 0;
            for (var i = 0; i < stops.Count; i++)
            {
                var stop = reflected ? stops[stops.Count - 1 - i] : stops[i];
                var offset = reflected ? 1 - stop.Offset : stop.Offset;
                result.Add(stop with { Offset = (period + offset) / count });
            }
        }

        for (var i = 1; i < result.Count - 1; i++)
        {
            if (result[i].Offset <= result[i - 1].Offset)
            {
                result[i] = result[i] with { Offset = Math.Min(1, result[i - 1].Offset + 1e-9) };
            }
        }

        var start = new PhysicalOffset(linear.Start.X + (first * dx), linear.Start.Y + (first * dy));
        var end = new PhysicalOffset(linear.Start.X + (last * dx), linear.Start.Y + (last * dy));
        return (start, end, result);
    }

    /// <summary>
    /// Creates the soft mask of a gradient whose stops have different alphas: the luminosity of a group that paints the
    /// alphas over the bounds of the shape (in the coordinates of the items, the space of the graphics state).
    /// </summary>
    private PdfSoftMask CreateSoftMask(PdfShadingPattern alphaPattern, PhysicalRect bounds)
    {
        var area = PdfGeometry.Inflate(bounds, 1);
        var form = _owner.Pdf.CreateForm(ToPdfRectangle(area), matrix: null, new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Gray });
        form.CreateContentBuilder().SetFillPattern(alphaPattern).Rectangle(area.X, area.Y, area.Width, area.Height).Fill();
        return PdfSoftMask.FromLuminosity(form);
    }

    /// <summary>
    /// Creates the tiling pattern of an image, a tile or a display list shader (an SVG image, an SVG pattern): the tile is
    /// drawn with vector graphics, and repeated in the axes that repeat.
    /// </summary>
    private PdfTilingPattern? CreateTilingPattern(DisplayShader shader, WriterState state)
    {
        var local = shader.LocalTransform is { Matrix3d: null } transform ? transform : DisplayMatrix.Identity;
        var matrix = PdfGeometry.ToPdf(state.ToStream.PreConcat(local));
        var toPage = state.ToPage.PreConcat(local);
        switch (shader)
        {
            case ImageShader imageShader:
            {
                if (_owner.GetImage(imageShader.Image) is not { } image)
                    return null;

                var width = imageShader.Image.Width;
                var height = imageShader.Image.Height;
                var pattern = _owner.Pdf.CreateTilingPattern(new PdfRectangle(0, 0, width, height), GetStep(imageShader.SpreadX, width), GetStep(imageShader.SpreadY, height), isColored: true, matrix);
                pattern.CreateContentBuilder().DrawImage(image, 0, height, width, -height);
                return pattern;
            }

            case TiledShader tiled:
            {
                var tile = new PhysicalRect(0, 0, tiled.TileSize.Width, tiled.TileSize.Height);
                var spacingX = double.IsFinite(tiled.Spacing.Width) ? Math.Max(0, tiled.Spacing.Width) : 0;
                var spacingY = double.IsFinite(tiled.Spacing.Height) ? Math.Max(0, tiled.Spacing.Height) : 0;
                var pattern = _owner.Pdf.CreateTilingPattern(ToPdfRectangle(tile), GetStep(tiled.SpreadX, tile.Width + spacingX), GetStep(tiled.SpreadY, tile.Height + spacingY), isColored: true, matrix);
                var writer = new DisplayListPdfWriter(_owner, pattern.CreateContentBuilder(), new WriterState(DisplayMatrix.Identity, toPage, PdfGeometry.Transform(toPage, tile)), tagged: false);
                if (tiled.Content is DisplayListShader { LocalTransform: null } list && list.Size == tiled.TileSize)
                {
                    writer.Write(list.Content);
                }
                else
                {
                    writer.WriteItem(new DrawRectItem(tile, new DisplayPaint(DisplayColor.Black) { Shader = tiled.Content }));
                }

                return pattern;
            }

            case DisplayListShader list:
            {
                var tile = new PhysicalRect(0, 0, list.Size.Width, list.Size.Height);
                var pattern = _owner.Pdf.CreateTilingPattern(ToPdfRectangle(tile), SingleTileStep, SingleTileStep, isColored: true, matrix);
                var writer = new DisplayListPdfWriter(_owner, pattern.CreateContentBuilder(), new WriterState(DisplayMatrix.Identity, toPage, PdfGeometry.Transform(toPage, tile)), tagged: false);

                // The display list is transparent outside of its rectangle
                writer.WriteClipped(list.Content, tile);
                return pattern;
            }

            default:
                return null;
        }

        static double GetStep(SpreadMode spread, double size) => spread == SpreadMode.Repeat && size > 0 ? size : SingleTileStep;
    }

    /// <summary>Writes a display list clipped to a rectangle.</summary>
    private void WriteClipped(DisplayList displayList, PhysicalRect clip)
    {
        _builder.Rectangle(clip.X, clip.Y, clip.Width, clip.Height).Clip().EndPath();
        Write(displayList);
    }

    /// <summary>The patterns of a gradient: its colors, its opacity, and the pattern of its alphas when they are not uniform.</summary>
    private sealed record GradientPatterns(PdfShadingPattern Pattern, double Alpha, PdfShadingPattern? AlphaPattern);
}
