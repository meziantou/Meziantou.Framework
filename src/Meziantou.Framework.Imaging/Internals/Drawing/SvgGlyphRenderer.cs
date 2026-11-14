using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Draws the glyphs a font describes with SVG documents (the <c>SVG </c> table) into images, with the drawing of the library.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "SVG - Scalable vector graphics table". The element of the glyph is drawn as a <c>use</c>
/// element of a new document would draw it: without the styles and the transformations of its ancestors, with the
/// transformation of the <c>viewBox</c>, <c>width</c> and <c>height</c> of the root element (its viewport is the em
/// square, and is never clipped). A unit of the document is a font unit, and its y axis goes down from the baseline.
/// </para>
/// <para>
/// The subset is what the glyphs of the fonts use, and what the specification requires: the shapes (<c>path</c>,
/// <c>rect</c>, <c>circle</c>, <c>ellipse</c>, <c>line</c>, <c>polyline</c>, <c>polygon</c>), <c>g</c>, <c>use</c>,
/// fills and strokes with their opacity, colors (with <c>currentColor</c>, the color of the text, which is black: the
/// image of a glyph is made once for every brush), the colors of the palettes of the font (<c>var(--colorN, fallback)</c>),
/// linear and radial gradients, clipping paths in user space, the opacity of a group, and the PNG and JPEG images of
/// <c>image</c> elements. The elements the specification forbids, and the optional ones (masks, patterns, filters,
/// markers, stylesheets), draw nothing.
/// </para>
/// <para>
/// The glyph is drawn once per size, into an image of the pixels it covers, which becomes a <see cref="ColorGlyph"/>
/// like the images of <c>sbix</c> and <c>CBDT</c>. A document that is not well-formed, or that has no element for the
/// glyph, gives no glyph: the outline of the glyph is drawn instead. A value that is not valid is ignored, as SVG says.
/// Every element read, every element drawn (the ones <c>use</c> repeats included) and every character of path data is
/// charged to the <see cref="GlyphBudget"/> of the glyph.
/// </para>
/// </remarks>
internal static class SvgGlyphRenderer
{
    // The largest image of a glyph: a larger glyph is drawn at a lower resolution and scaled when it is drawn.
    private const int MaxImageSide = 4096;
    private const long MaxImagePixels = 16L * 1024 * 1024;

    // How deep the use elements and the references of gradients can nest
    private const int MaxDepth = 32;

    /// <summary>Draws the SVG glyph of a font at a size, and makes the display list that draws its image.</summary>
    /// <param name="face">The font.</param>
    /// <param name="glyph">The glyph and its document.</param>
    /// <param name="palette">The palette the colors of the font come from.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <param name="budget">The complexity budget of the glyph.</param>
    /// <returns><see langword="null"/> when the document cannot be read or does not describe the glyph.</returns>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit, or its document the size limit.</exception>
    public static ColorGlyph? CreateGlyph(FontFace face, in SvgGlyph glyph, int palette, double size, ref GlyphBudget budget)
    {
        ReadOnlyMemory<byte> bytes;
        try
        {
            bytes = face.GetSvgDocument(glyph);
        }
        catch (InvalidFontException)
        {
            return null;
        }

        var document = SvgGlyphDocument.Read(bytes, ref budget);
        if (document?.GetElementById("glyph" + glyph.GlyphId.ToString(System.Globalization.CultureInfo.InvariantCulture)) is not { IsForeign: false } element)
            return null;

        var builder = new Builder(face, document, (uint)palette < (uint)face.PaletteCount ? palette : 0, budget);
        try
        {
            var ops = builder.Build(element);
            budget = builder.Budget;
            return Rasterize(face, ops, size);
        }
        catch (ArgumentException)
        {
            // A value of the document the drawing of the library does not accept (a coordinate beyond its limits).
            budget = builder.Budget;
            return null;
        }
        finally
        {
            builder.DisposeImages();
        }
    }

    private static ColorGlyph Rasterize(FontFace face, List<Op> ops, double size)
    {
        var bounds = Bounds.Empty;
        AddBounds(ops, ref bounds);

        // The viewport of the document is not clipped, but nothing is drawn beyond an em around the box of the font.
        double em = face.UnitsPerEm;
        bounds = bounds.Intersect(new Bounds(face.XMin - em, -face.YMax - em, face.XMax + em, -face.YMin + em));
        var scale = size / face.UnitsPerEm;
        if (bounds.IsEmpty || !(scale > 0))
            return ColorGlyph.FromImage(new ColorImage(1, 1, new byte[4]), 0, 0, 1, 1);

        var (left, top, width, height) = GetPixels(bounds, scale);
        var pixels = (long)width * height;
        if (width > MaxImageSide || height > MaxImageSide || pixels > MaxImagePixels)
        {
            scale *= Math.Min(Math.Min((double)MaxImageSide / width, (double)MaxImageSide / height), Math.Sqrt((double)MaxImagePixels / pixels));
            (left, top, width, height) = GetPixels(bounds, scale);
            width = Math.Min(width, MaxImageSide);
            height = Math.Min(height, MaxImageSide);
        }

        var transform = AffineTransform.CreateScale(scale, scale).Then(AffineTransform.CreateTranslation(-left, -top));
        using var image = new Image<Rgba32>(width, height, new Rgba32(0, 0, 0, 0));
        image.Draw((ops, transform), (canvas, state) => canvas.Transform(state.transform, state.ops, Draw));
        var data = new byte[width * height * 4];
        image.Frames[0].CopyPixelBytesTo(data);

        // The image in font units, with the y axis up
        return ColorGlyph.FromImage(new ColorImage(width, height, data), left / scale, -top / scale, width / scale, height / scale);

        static (int Left, int Top, int Width, int Height) GetPixels(Bounds bounds, double scale)
        {
            var left = Math.Floor(bounds.Left * scale);
            var top = Math.Floor(bounds.Top * scale);
            var width = Math.Max(1, Math.Ceiling(bounds.Right * scale) - left);
            var height = Math.Max(1, Math.Ceiling(bounds.Bottom * scale) - top);
            return ((int)left, (int)top, (int)Math.Min(width, int.MaxValue / 8), (int)Math.Min(height, int.MaxValue / 8));
        }
    }

    private static void AddBounds(List<Op> ops, ref Bounds bounds)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case ShapeOp shape:
                    var box = shape.Fill is not null ? shape.Path.Bounds : RectangleD.Empty;
                    if (shape.Pen is not null)
                    {
                        var stroke = shape.Path.GetBounds(shape.Pen);
                        box = box.IsEmpty ? stroke : RectangleD.Union(box, stroke);
                    }

                    bounds = bounds.Union(Bounds.From(shape.Transform.TransformBounds(box)));
                    break;

                case ImageOp image:
                    bounds = bounds.Union(Bounds.From(image.Transform.TransformBounds(image.Destination)));
                    break;

                case GroupOp group:
                    var inner = Bounds.Empty;
                    AddBounds(group.Children, ref inner);
                    if (group.Clip is not null)
                    {
                        inner = inner.Intersect(Bounds.From(group.Clip.Bounds));
                    }

                    bounds = bounds.Union(inner);
                    break;
            }
        }
    }

    private static void Draw(DrawingCanvas canvas, List<Op> ops)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case ShapeOp shape:
                    canvas.Transform(shape.Transform, shape, DrawShape);
                    break;

                case ImageOp image:
                    canvas.Transform(image.Transform, image, (canvas, image) => canvas.DrawImage(image.Image.Frames[0], image.Destination));
                    break;

                case GroupOp group when group.Clip is not null:
                    canvas.Clip(group.Clip, group, DrawLayer, group.ClipRule);
                    break;

                case GroupOp group:
                    DrawLayer(canvas, group);
                    break;
            }
        }
    }

    private static void DrawShape(DrawingCanvas canvas, ShapeOp shape)
    {
        if (shape.Fill is not null)
        {
            canvas.Fill(shape.Fill, shape.Path, shape.FillRule);
        }

        if (shape.Pen is not null)
        {
            canvas.Stroke(shape.Pen, shape.Path);
        }
    }

    private static void DrawLayer(DrawingCanvas canvas, GroupOp group)
    {
        if (group.Opacity < 1)
        {
            canvas.Layer(group.Opacity, group.Children, Draw);
        }
        else
        {
            Draw(canvas, group.Children);
        }
    }

    /// <summary>Turns the elements of a glyph into operations of the drawing of the library, in the space of the glyph.</summary>
    private sealed class Builder(FontFace face, SvgGlyphDocument document, int palette, GlyphBudget budget)
    {
        private readonly HashSet<SvgElement> _activeUses = new(ReferenceEqualityComparer.Instance);
        private readonly List<Image<Rgba32>> _images = [];
        private GlyphBudget _budget = budget;

        public GlyphBudget Budget => _budget;

        /// <summary>Gets the size of the viewport of the document, the em square: the reference of the percentages.</summary>
        public double Viewport => face.UnitsPerEm;

        public List<Op> Build(SvgElement element)
        {
            var root = document.Root;
            var ops = new List<Op>();
            var transform = GetViewportTransform(root);

            // The root element is the glyph: its content is drawn, with its own styles. Else the element is drawn alone.
            if (ReferenceEquals(element, root))
            {
                var style = Style.Initial.Inherit(root, this);
                AddGroup(root, style, transform, ops, depth: 0, (builder, element, style, transform, ops, depth) => builder.AddChildren(element, style, transform, ops, depth));
            }
            else
            {
                AddElement(element, Style.Initial, transform, ops, depth: 0);
            }

            return ops;
        }

        public void DisposeImages()
        {
            foreach (var image in _images)
            {
                image.Dispose();
            }

            _images.Clear();
        }

        /// <summary>
        /// The transformation of the viewport of the root element: the em square, unless <c>width</c> and
        /// <c>height</c> give another size, which a <c>viewBox</c> is fitted into (<c>preserveAspectRatio</c>).
        /// </summary>
        private AffineTransform GetViewportTransform(SvgElement root)
        {
            var width = SvgValues.TryParseLength(root.GetAttribute("width"), Viewport, out var w) && w > 0 ? w : Viewport;
            var height = SvgValues.TryParseLength(root.GetAttribute("height"), Viewport, out var h) && h > 0 ? h : Viewport;
            var viewBox = SvgValues.ParseNumberList(root.GetAttribute("viewBox"));
            if (viewBox.Count != 4 || !(viewBox[2] > 0) || !(viewBox[3] > 0))
                return AffineTransform.CreateScale(width / Viewport, height / Viewport);

            return FitViewBox(viewBox[0], viewBox[1], viewBox[2], viewBox[3], width, height, root.GetAttribute("preserveAspectRatio"));
        }

        /// <summary>Fits a box into a viewport, as <c>preserveAspectRatio</c> says (<c>none</c>, or <c>meet</c> or <c>slice</c> with an alignment).</summary>
        private static AffineTransform FitViewBox(double x, double y, double width, double height, double viewportWidth, double viewportHeight, string? preserveAspectRatio)
        {
            var scaleX = viewportWidth / width;
            var scaleY = viewportHeight / height;
            var value = preserveAspectRatio.AsSpan().Trim();
            if (value.StartsWith("defer", StringComparison.Ordinal))
            {
                value = value[5..].TrimStart();
            }

            var space = value.IndexOf(' ');
            var align = space < 0 ? value : value[..space];
            var isSlice = space >= 0 && value[(space + 1)..].Trim().SequenceEqual("slice");
            if (align.SequenceEqual("none"))
                return AffineTransform.CreateTranslation(-x, -y).Then(AffineTransform.CreateScale(scaleX, scaleY));

            var scale = isSlice ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            var alignX = 0.5;
            var alignY = 0.5;
            if (align.Length == 8)
            {
                alignX = align[..4] switch { "xMin" => 0, "xMax" => 1, _ => 0.5 };
                alignY = align[4..] switch { "YMin" => 0, "YMax" => 1, _ => 0.5 };
            }

            var offsetX = (viewportWidth - (width * scale)) * alignX;
            var offsetY = (viewportHeight - (height * scale)) * alignY;
            return AffineTransform.CreateTranslation(-x, -y).Then(AffineTransform.CreateScale(scale, scale)).Then(AffineTransform.CreateTranslation(offsetX, offsetY));
        }

        private void AddChildren(SvgElement element, Style style, AffineTransform transform, List<Op> ops, int depth)
        {
            foreach (var child in element.Children)
            {
                AddElement(child, style, transform, ops, depth);
            }
        }

        private void AddElement(SvgElement element, Style parentStyle, AffineTransform parentTransform, List<Op> ops, int depth)
        {
            _budget.Charge(1);
            if (element.IsForeign || element.GetAttribute("display") is "none")
                return;

            var style = parentStyle.Inherit(element, this);
            var transform = SvgValues.ParseTransform(element.GetAttribute("transform")).Then(parentTransform);
            switch (element.Name)
            {
                case "g":
                case "svg":
                    AddGroup(element, style, transform, ops, depth, (builder, element, style, transform, ops, depth) => builder.AddChildren(element, style, transform, ops, depth));
                    break;

                case "use":
                    AddUse(element, style, transform, ops, depth);
                    break;

                case "image":
                    AddGroup(element, style, transform, ops, depth, (builder, element, style, transform, ops, depth) => builder.AddImage(element, style, transform, ops));
                    break;

                default:
                    if (IsShape(element.Name))
                    {
                        AddGroup(element, style, transform, ops, depth, (builder, element, style, transform, ops, depth) => builder.AddShape(element, style, transform, ops));
                    }

                    break;
            }
        }

        private static bool IsShape(string name) => name is "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon";

        /// <summary>Adds what an element draws, in a group when it has an opacity or a clipping path.</summary>
        private void AddGroup(SvgElement element, Style style, AffineTransform transform, List<Op> ops, int depth, Action<Builder, SvgElement, Style, AffineTransform, List<Op>, int> add)
        {
            var opacity = SvgValues.TryParseNumberOrPercentage(element.GetAttribute("opacity"), 1, out var value) ? Math.Clamp(value, 0, 1) : 1;
            if (opacity == 0)
                return;

            VectorPath? clip = null;
            var clipRule = FillRule.NonZero;
            if (element.GetAttribute("clip-path") is { } clipPath && document.GetElementByUrl(clipPath, out _) is { Name: "clipPath", IsForeign: false } clipElement
                && !TryBuildClip(clipElement, transform, depth, out clip, out clipRule))
            {
                return;
            }

            if (opacity == 1 && clip is null)
            {
                add(this, element, style, transform, ops, depth);
                return;
            }

            var group = new GroupOp(opacity, clip, clipRule);
            add(this, element, style, transform, group.Children, depth);
            if (group.Children.Count > 0)
            {
                ops.Add(group);
            }
        }

        private void AddUse(SvgElement element, Style style, AffineTransform transform, List<Op> ops, int depth)
        {
            // A use element that refers to itself or to one of its ancestors is an error: it draws nothing.
            if (depth >= MaxDepth || document.GetElementByIri(element.GetAttribute("href")) is not { IsForeign: false } target || IsAncestorOrSelf(target, element) || !_activeUses.Add(target))
                return;

            try
            {
                var x = SvgValues.TryParseLength(element.GetAttribute("x"), Viewport, out var valueX) ? valueX : 0;
                var y = SvgValues.TryParseLength(element.GetAttribute("y"), Viewport, out var valueY) ? valueY : 0;
                var translated = AffineTransform.CreateTranslation(x, y).Then(transform);
                AddGroup(element, style, translated, ops, depth, (builder, _, style, transform, ops, depth) =>
                {
                    // A symbol is drawn only through a use element, as a group.
                    if (target.Name == "symbol")
                    {
                        if (target.GetAttribute("display") is not "none")
                        {
                            builder.AddChildren(target, style.Inherit(target, builder), SvgValues.ParseTransform(target.GetAttribute("transform")).Then(transform), ops, depth + 1);
                        }
                    }
                    else
                    {
                        builder.AddElement(target, style, transform, ops, depth + 1);
                    }
                });
            }
            finally
            {
                _activeUses.Remove(target);
            }
        }

        private static bool IsAncestorOrSelf(SvgElement candidate, SvgElement element)
        {
            for (var current = element; current is not null; current = current.Parent)
            {
                if (ReferenceEquals(current, candidate))
                    return true;
            }

            return false;
        }

        private void AddShape(SvgElement element, Style style, AffineTransform transform, List<Op> ops)
        {
            if (!style.IsVisible || CreatePath(element) is not { } path)
                return;

            var fill = element.Name is "line" ? null : CreateBrush(style.Fill, style.FillOpacity, path);
            Pen? pen = null;
            if (style.StrokeWidth > 0 && CreateBrush(style.Stroke, style.StrokeOpacity, path) is { } strokeBrush)
            {
                pen = new Pen(strokeBrush, style.StrokeWidth)
                {
                    LineCap = style.LineCap,
                    LineJoin = style.LineJoin,
                    MiterLimit = style.MiterLimit,
                    DashPattern = style.DashPattern,
                    DashOffset = style.DashPattern.Length > 0 ? style.DashOffset : 0,
                };
            }

            if (fill is not null || pen is not null)
            {
                _budget.Charge(1);
                ops.Add(new ShapeOp(path, transform, fill, style.FillRule, pen));
            }
        }

        /// <summary>Makes the outline of a shape element, in its user space; <see langword="null"/> for a shape that draws nothing.</summary>
        private VectorPath? CreatePath(SvgElement element)
        {
            var builder = new VectorPathBuilder();
            switch (element.Name)
            {
                case "path":
                    var data = element.GetAttribute("d");
                    if (data is null)
                        return null;

                    _budget.Charge(data.Length);
                    return VectorPath.TryParseSvgPathData(data, out var path) ? path : null;

                case "rect":
                    var x = GetLength(element, "x");
                    var y = GetLength(element, "y");
                    var width = GetLength(element, "width");
                    var height = GetLength(element, "height");
                    if (!(width > 0 && height > 0))
                        return null;

                    // SVG 1.1, "The rect element": a missing radius is the other one, and both are at most half the side.
                    var hasRx = SvgValues.TryParseLength(element.GetAttribute("rx"), Viewport, out var rx) && rx >= 0;
                    var hasRy = SvgValues.TryParseLength(element.GetAttribute("ry"), Viewport, out var ry) && ry >= 0;
                    if (!hasRx)
                    {
                        rx = hasRy ? ry : 0;
                    }

                    if (!hasRy)
                    {
                        ry = rx;
                    }

                    rx = Math.Min(rx, width / 2);
                    ry = Math.Min(ry, height / 2);
                    var rectangle = new RectangleD(x, y, width, height);
                    return (rx > 0 && ry > 0 ? builder.AddRoundedRectangle(rectangle, rx, ry) : builder.AddRectangle(rectangle)).Build();

                case "circle":
                    var r = GetLength(element, "r");
                    return r > 0 ? builder.AddCircle(new PointD(GetLength(element, "cx"), GetLength(element, "cy")), r).Build() : null;

                case "ellipse":
                    var radiusX = GetLength(element, "rx");
                    var radiusY = GetLength(element, "ry");
                    return radiusX > 0 && radiusY > 0 ? builder.AddEllipse(new PointD(GetLength(element, "cx"), GetLength(element, "cy")), radiusX, radiusY).Build() : null;

                case "line":
                    return builder.MoveTo(GetLength(element, "x1"), GetLength(element, "y1")).LineTo(GetLength(element, "x2"), GetLength(element, "y2")).Build();

                case "polyline":
                case "polygon":
                    // An odd number of coordinates draws the points before the last one.
                    var points = SvgValues.ParseNumberList(element.GetAttribute("points"));
                    _budget.Charge(points.Count);
                    if (points.Count < 4)
                        return null;

                    builder.MoveTo(points[0], points[1]);
                    for (var i = 2; i + 1 < points.Count; i += 2)
                    {
                        builder.LineTo(points[i], points[i + 1]);
                    }

                    if (element.Name == "polygon")
                    {
                        builder.Close();
                    }

                    return builder.Build();

                default:
                    return null;
            }
        }

        private double GetLength(SvgElement element, string attribute) => SvgValues.TryParseLength(element.GetAttribute(attribute), Viewport, out var value) ? value : 0;

        /// <summary>Adds an image element: an image of a format the library reads, in a <c>data:</c> URL.</summary>
        private void AddImage(SvgElement element, Style style, AffineTransform transform, List<Op> ops)
        {
            var width = GetLength(element, "width");
            var height = GetLength(element, "height");
            if (!style.IsVisible || !(width > 0 && height > 0) || element.GetAttribute("href") is not { } href)
                return;

            var url = href.AsSpan().Trim();
            var comma = url.IndexOf(',');
            if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || comma < 0 || !url[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
                return;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(url[(comma + 1)..].ToString());
            }
            catch (FormatException)
            {
                return;
            }

            _budget.Charge(bytes.Length);
            Image<Rgba32> image;
            try
            {
                image = Image.Load<Rgba32>(bytes.AsSpan(), new ImageDecodeOptions { Configuration = ImageFontEnvironment.GetConfiguration(face), FrameLimit = 1 });
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnknownImageFormatException or UnsupportedImageFeatureException or ImageResourceLimitException)
            {
                return;
            }

            _images.Add(image);
            var x = GetLength(element, "x");
            var y = GetLength(element, "y");
            var fit = FitViewBox(0, 0, image.Width, image.Height, width, height, element.GetAttribute("preserveAspectRatio"));
            var destination = fit.TransformBounds(new RectangleD(0, 0, image.Width, image.Height));
            destination = new RectangleD(destination.X + x, destination.Y + y, destination.Width, destination.Height);

            // The image is clipped to its viewport when preserveAspectRatio slices it.
            var viewport = new VectorPathBuilder().AddRectangle(new RectangleD(x, y, width, height)).Build().Transform(transform);
            var group = new GroupOp(1, viewport, FillRule.NonZero);
            group.Children.Add(new ImageOp(image, destination, transform));
            ops.Add(group);
        }

        /// <summary>Makes the brush of a paint for a shape, with an opacity; <see langword="null"/> for a paint that draws nothing.</summary>
        private Brush? CreateBrush(Paint paint, double opacity, VectorPath path)
        {
            return paint.Kind switch
            {
                PaintKind.Color when paint.Alpha * opacity > 0 => new SolidBrush(ToColor(paint.Red, paint.Green, paint.Blue, paint.Alpha * opacity)),
                PaintKind.Gradient => CreateGradientBrush(paint.Gradient!, opacity, path),
                _ => null,
            };
        }

        private static Rgba64 ToColor(double red, double green, double blue, double alpha)
            => new(ToSample(red), ToSample(green), ToSample(blue), ToSample(alpha));

        private static ushort ToSample(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue, MidpointRounding.AwayFromZero);

        /// <summary>Makes the brush of a gradient element, with the attributes and the stops of the gradients it refers to.</summary>
        private Brush? CreateGradientBrush(SvgElement gradient, double opacity, VectorPath path)
        {
            var isRadial = gradient.Name == "radialGradient";
            var chain = new List<SvgElement>();
            for (var current = gradient; current is not null && chain.Count < MaxDepth && !chain.Contains(current); current = document.GetElementByIri(current.GetAttribute("href")))
            {
                if (current.IsForeign || current.Name is not ("linearGradient" or "radialGradient"))
                    break;

                _budget.Charge(1);
                chain.Add(current);
            }

            string? Get(string attribute, bool isGeometry)
            {
                foreach (var element in chain)
                {
                    if ((!isGeometry || element.Name == gradient.Name) && element.GetAttribute(attribute) is { } value)
                        return value;
                }

                return null;
            }

            var stops = CreateStops(chain.FirstOrDefault(element => element.Children.Exists(child => child.Name == "stop")), opacity);
            if (stops.Count == 0)
                return null;

            if (stops.Count == 1)
                return new SolidBrush(stops[0].Color);

            // The coordinates are fractions of the box of the shape, unless the gradient is in user space.
            var isBoundingBox = Get("gradientUnits", isGeometry: false) is not "userSpaceOnUse";
            var unitsTransform = AffineTransform.Identity;
            var reference = Viewport;
            if (isBoundingBox)
            {
                var box = path.GetTightBounds();
                if (!(box.Width > 0 && box.Height > 0))
                    return null;

                unitsTransform = new AffineTransform(box.Width, 0, 0, box.Height, box.X, box.Y);
                reference = 1;
            }

            double Coordinate(string attribute, double defaultValue) => SvgValues.TryParseNumberOrPercentage(Get(attribute, isGeometry: true), reference, out var value) ? value : defaultValue;

            var brushTransform = SvgValues.ParseTransform(Get("gradientTransform", isGeometry: false)).Then(unitsTransform);
            var spread = Get("spreadMethod", isGeometry: false) switch
            {
                "reflect" => GradientSpreadMode.Reflect,
                "repeat" => GradientSpreadMode.Repeat,
                _ => GradientSpreadMode.Pad,
            };

            var stopArray = CollectionsMarshal.AsSpan(stops);
            if (isRadial)
            {
                var cx = Coordinate("cx", 0.5 * reference);
                var cy = Coordinate("cy", 0.5 * reference);
                var r = Coordinate("r", 0.5 * reference);
                var fx = Coordinate("fx", cx);
                var fy = Coordinate("fy", cy);
                if (r < 0)
                    return null;

                if (r == 0)
                    return new SolidBrush(stops[^1].Color);

                // SVG 1.1: a focal point outside the circle is moved onto it.
                var distance = Math.Sqrt(((fx - cx) * (fx - cx)) + ((fy - cy) * (fy - cy)));
                if (distance > r * 0.999)
                {
                    fx = cx + ((fx - cx) * r * 0.999 / distance);
                    fy = cy + ((fy - cy) * r * 0.999 / distance);
                }

                return new RadialGradientBrush(new PointD(cx, cy), r, stopArray)
                {
                    FocalPoint = new PointD(fx, fy),
                    SpreadMode = spread,
                    Transform = brushTransform,
                };
            }

            var x1 = Coordinate("x1", 0);
            var y1 = Coordinate("y1", 0);
            var x2 = Coordinate("x2", reference);
            var y2 = Coordinate("y2", 0);
            if (x1 == x2 && y1 == y2)
                return new SolidBrush(stops[^1].Color);

            return new LinearGradientBrush(new PointD(x1, y1), new PointD(x2, y2), stopArray)
            {
                SpreadMode = spread,
                Transform = brushTransform,
            };
        }

        /// <summary>Reads the stops of a gradient: an offset is clamped to [0, 1] and is never less than the one before it.</summary>
        private List<GradientStop> CreateStops(SvgElement? gradient, double opacity)
        {
            var stops = new List<GradientStop>();
            if (gradient is null)
                return stops;

            double previous = 0;
            foreach (var stop in gradient.Children)
            {
                if (stop.Name != "stop" || stop.IsForeign)
                    continue;

                _budget.Charge(1);
                var offset = SvgValues.TryParseNumberOrPercentage(stop.GetAttribute("offset"), 1, out var value) ? Math.Clamp(value, 0, 1) : 0;
                offset = Math.Max(offset, previous);
                previous = offset;
                var color = ParsePaint(stop.GetAttribute("stop-color"), Paint.Black, Paint.Black) is { Kind: PaintKind.Color } paint ? paint : Paint.Black;
                var stopOpacity = SvgValues.TryParseNumberOrPercentage(stop.GetAttribute("stop-opacity"), 1, out var alpha) ? Math.Clamp(alpha, 0, 1) : 1;
                stops.Add(new GradientStop(offset, ToColor(color.Red, color.Green, color.Blue, color.Alpha * stopOpacity * opacity)));
            }

            return stops;
        }

        /// <summary>Makes the clipping path an element refers to, in the space of the glyph.</summary>
        /// <returns><see langword="false"/> when the path clips everything; <paramref name="result"/> is <see langword="null"/> when it clips nothing.</returns>
        private bool TryBuildClip(SvgElement clipPath, AffineTransform transform, int depth, out VectorPath? result, out FillRule clipRule)
        {
            clipRule = FillRule.NonZero;
            result = null;

            // A clipping path in the box of the element is not supported: the element is not clipped.
            if (clipPath.GetAttribute("clipPathUnits") is "objectBoundingBox")
                return true;

            var clipTransform = SvgValues.ParseTransform(clipPath.GetAttribute("transform")).Then(transform);
            foreach (var child in clipPath.Children)
            {
                _budget.Charge(1);
                var element = child;
                var childTransform = clipTransform;
                if (child.IsForeign || child.GetAttribute("display") is "none")
                    continue;

                if (child.Name == "use")
                {
                    if (depth >= MaxDepth || document.GetElementByIri(child.GetAttribute("href")) is not { IsForeign: false } target || !IsShape(target.Name))
                        continue;

                    var x = GetLength(child, "x");
                    var y = GetLength(child, "y");
                    childTransform = AffineTransform.CreateTranslation(x, y).Then(SvgValues.ParseTransform(child.GetAttribute("transform")).Then(clipTransform));
                    element = target;
                }

                if (!IsShape(element.Name) || element.GetAttribute("visibility") is "hidden" or "collapse" || CreatePath(element) is not { } path)
                    continue;

                path = path.Transform(SvgValues.ParseTransform(element.GetAttribute("transform")).Then(childTransform));
                var rule = element.GetAttribute("clip-rule") ?? child.GetAttribute("clip-rule") ?? clipPath.GetAttribute("clip-rule");
                if (result is null)
                {
                    result = path;
                    clipRule = rule is "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;
                }
                else
                {
                    result = result.Combine(path, PathCombineMode.Union, clipRule);
                    clipRule = FillRule.NonZero;
                }
            }

            return result is not null;
        }

        /// <summary>
        /// Reads a paint: <c>none</c>, a color, <c>currentColor</c>, a gradient (<c>url(#id)</c> with a fallback), or a
        /// color of the palette (<c>var(--colorN, fallback)</c>).
        /// </summary>
        /// <returns>The paint, or <paramref name="inherited"/> when the value is not valid.</returns>
        public Paint ParsePaint(string? text, Paint inherited, Paint currentColor)
        {
            if (text is null)
                return inherited;

            var value = text.AsSpan().Trim();
            if (value.SequenceEqual("none"))
                return Paint.None;

            if (value.SequenceEqual("inherit"))
                return inherited;

            // The context paints (deprecated) are the color of the text, like currentColor.
            if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase) || value.SequenceEqual("context-fill") || value.SequenceEqual("context-stroke"))
                return currentColor;

            if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                var element = document.GetElementByUrl(value, out var fallback);
                if (element is { IsForeign: false, Name: "linearGradient" or "radialGradient" })
                    return new Paint(PaintKind.Gradient, 0, 0, 0, 1, element);

                return fallback.IsEmpty ? Paint.None : ParsePaint(fallback.ToString(), inherited, currentColor);
            }

            if (value.StartsWith("var(", StringComparison.OrdinalIgnoreCase) && value is [.., ')'])
            {
                var arguments = value[4..^1];
                var comma = arguments.IndexOf(',');
                var name = (comma < 0 ? arguments : arguments[..comma]).Trim();
                if (name.StartsWith("--color", StringComparison.Ordinal) && int.TryParse(name[7..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var entry)
                    && face.TryGetPaletteColor(palette, entry, out var red, out var green, out var blue, out var alpha))
                {
                    return new Paint(PaintKind.Color, red / 255.0, green / 255.0, blue / 255.0, alpha / 255.0, Gradient: null);
                }

                return comma < 0 ? inherited : ParsePaint(arguments[(comma + 1)..].ToString(), inherited, currentColor);
            }

            return SvgValues.TryParseColor(value, out var r, out var g, out var b) ? new Paint(PaintKind.Color, r, g, b, 1, Gradient: null) : inherited;
        }
    }

    /// <summary>The properties an element inherits from its parent.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Style(
        Paint Color,
        Paint Fill,
        double FillOpacity,
        FillRule FillRule,
        Paint Stroke,
        double StrokeOpacity,
        double StrokeWidth,
        LineCap LineCap,
        LineJoin LineJoin,
        double MiterLimit,
        double[] DashPatternArray,
        double DashOffset,
        bool IsVisible)
    {
        // The color of the text: an image of a glyph is made for every brush, so it is black.
        public static Style Initial { get; } = new(Paint.Black, Paint.Black, 1, FillRule.NonZero, Paint.None, 1, 1, LineCap.Butt, LineJoin.Miter, 4, [], 0, IsVisible: true);

        public ReadOnlySpan<double> DashPattern => DashPatternArray;

        public Style Inherit(SvgElement element, Builder builder)
        {
            if (element.Attributes.Count == 0)
                return this;

            var color = builder.ParsePaint(element.GetAttribute("color"), Color, Color) is { Kind: PaintKind.Color } value ? value : Color;
            var dashArray = DashPatternArray;
            if (element.GetAttribute("stroke-dasharray") is { } dashes)
            {
                var values = SvgValues.ParseNumberList(dashes);
                dashArray = dashes.Trim() is "none" || values.Count == 0 || values.Exists(value => value < 0) || values.TrueForAll(value => value == 0) ? [] : [.. values];
            }

            return this with
            {
                Color = color,
                Fill = builder.ParsePaint(element.GetAttribute("fill"), Fill, color),
                FillOpacity = GetOpacity(element, "fill-opacity", FillOpacity),
                FillRule = element.GetAttribute("fill-rule") switch
                {
                    "evenodd" => FillRule.EvenOdd,
                    "nonzero" => FillRule.NonZero,
                    _ => FillRule,
                },
                Stroke = builder.ParsePaint(element.GetAttribute("stroke"), Stroke, color),
                StrokeOpacity = GetOpacity(element, "stroke-opacity", StrokeOpacity),
                StrokeWidth = SvgValues.TryParseLength(element.GetAttribute("stroke-width"), builder.Viewport, out var width) && width >= 0 ? width : StrokeWidth,
                LineCap = element.GetAttribute("stroke-linecap") switch
                {
                    "butt" => LineCap.Butt,
                    "round" => LineCap.Round,
                    "square" => LineCap.Square,
                    _ => LineCap,
                },
                LineJoin = element.GetAttribute("stroke-linejoin") switch
                {
                    "miter" => LineJoin.Miter,
                    "round" => LineJoin.Round,
                    "bevel" => LineJoin.Bevel,
                    _ => LineJoin,
                },
                MiterLimit = SvgValues.TryParseNumber(element.GetAttribute("stroke-miterlimit"), out var limit) && limit >= 1 ? limit : MiterLimit,
                DashPatternArray = dashArray,
                DashOffset = SvgValues.TryParseLength(element.GetAttribute("stroke-dashoffset"), builder.Viewport, out var offset) ? offset : DashOffset,
                IsVisible = element.GetAttribute("visibility") switch
                {
                    "visible" => true,
                    "hidden" or "collapse" => false,
                    _ => IsVisible,
                },
            };

            static double GetOpacity(SvgElement element, string attribute, double inherited)
                => SvgValues.TryParseNumberOrPercentage(element.GetAttribute(attribute), 1, out var value) ? Math.Clamp(value, 0, 1) : inherited;
        }
    }

    private enum PaintKind
    {
        None,
        Color,
        Gradient,
    }

    /// <summary>A paint: nothing, a color (with the opacity of a color of the palette), or a gradient element.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Paint(PaintKind Kind, double Red, double Green, double Blue, double Alpha, SvgElement? Gradient)
    {
        public static Paint None => default;

        public static Paint Black => new(PaintKind.Color, 0, 0, 0, 1, Gradient: null);
    }

    /// <summary>A box in the space of the glyph, which can be empty.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Bounds(double Left, double Top, double Right, double Bottom)
    {
        public static Bounds Empty => new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

        public bool IsEmpty => !(Right > Left && Bottom > Top);

        public static Bounds From(RectangleD rectangle) => rectangle.IsEmpty && rectangle.Width == 0 && rectangle.Height == 0 ? Empty : new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

        public Bounds Union(Bounds other) => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));

        public Bounds Intersect(Bounds other) => new(Math.Max(Left, other.Left), Math.Max(Top, other.Top), Math.Min(Right, other.Right), Math.Min(Bottom, other.Bottom));
    }

    private abstract class Op;

    /// <summary>A shape: its outline in its user space, the transformation to the space of the glyph, its fill and its stroke.</summary>
    private sealed class ShapeOp(VectorPath path, AffineTransform transform, Brush? fill, FillRule fillRule, Pen? pen) : Op
    {
        public VectorPath Path { get; } = path;

        public AffineTransform Transform { get; } = transform;

        public Brush? Fill { get; } = fill;

        public FillRule FillRule { get; } = fillRule;

        public Pen? Pen { get; } = pen;
    }

    private sealed class ImageOp(Image<Rgba32> image, RectangleD destination, AffineTransform transform) : Op
    {
        public Image<Rgba32> Image { get; } = image;

        public RectangleD Destination { get; } = destination;

        public AffineTransform Transform { get; } = transform;
    }

    /// <summary>A group with an opacity, or clipped by a path in the space of the glyph.</summary>
    private sealed class GroupOp(double opacity, VectorPath? clip, FillRule clipRule) : Op
    {
        public double Opacity { get; } = opacity;

        public VectorPath? Clip { get; } = clip;

        public FillRule ClipRule { get; } = clipRule;

        public List<Op> Children { get; } = [];
    }
}
