using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The paint servers of SVG (https://svgwg.org/svg2-draft/pservers.html): the linear and radial gradients and the patterns, with
// the attributes inherited through href (https://svgwg.org/svg2-draft/pservers.html#PaintServerTemplates).
internal sealed partial class Painter
{
    /// <summary>The number of colors interpolated in linearRGB between two stops of a gradient with color-interpolation: linearRGB.</summary>
    private const int LinearRgbInterpolationSteps = 16;

    /// <summary>The maximum nesting of the elements painted by feImage primitives.</summary>
    private const int MaxFilterImageDepth = 4;

    /// <summary>The number of elements being painted for feImage primitives.</summary>
    private int _filterImageDepth;

    /// <summary>
    /// Creates the paint of a paint server element for an element, in the user space of the element (without opacity). Returns false
    /// when the element is not a paint server (the fallback of the paint is used); the paint is null when nothing is painted (e.g. a
    /// gradient without stops, an objectBoundingBox paint server of an element without width or height).
    /// </summary>
    private bool TryCreatePaintServer(Element server, Element element, Func<PhysicalRect?> getBoundingBox, SvgPaintContext context, out DisplayPaint? paint)
    {
        paint = null;

        // A paint server whose transform is not invertible is invalid: the fallback paint is used (https://drafts.csswg.org/css-transforms/#transform-function-lists)
        if (server is SvgLinearGradientElement or SvgRadialGradientElement or SvgPatternElement
            && !GetTemplateTransform(GetTemplateChain(server)).IsInvertible)
        {
            return false;
        }

        switch (server)
        {
            case SvgLinearGradientElement or SvgRadialGradientElement:
                paint = CreateGradientPaint(server, element, getBoundingBox, context);
                return true;

            case SvgPatternElement:
                paint = CreatePatternPaint(server, element, getBoundingBox, context, out var invalidTile);

                // As in the browsers, a pattern without tile (e.g. its width is missing) is invalid: the fallback is used
                return !invalidTile;

            default:
                return false;
        }
    }

    /// <summary>Returns the element referenced by the href (or xlink:href) attribute of a paint server, in its document, or null.</summary>
    private static Element? GetTemplateElement(Element element)
        => SvgLinking.TryGetSameDocumentFragment(element, SvgLinking.GetHref(element), out var id) ? element.NodeDocument.GetElementById(id) : null;

    /// <summary>
    /// Returns the chain of the templates of a paint server (https://svgwg.org/svg2-draft/pservers.html#PaintServerTemplates): the
    /// element, then the elements referenced by href, while they are paint servers of the same kind (gradients for a gradient),
    /// without cycle.
    /// </summary>
    private static List<Element> GetTemplateChain(Element server)
    {
        var chain = new List<Element> { server };
        for (var current = GetTemplateElement(server); current is not null && chain.Count < 32 && !chain.Contains(current); current = GetTemplateElement(current))
        {
            if (server is SvgPatternElement ? current is not SvgPatternElement : current is not (SvgLinearGradientElement or SvgRadialGradientElement))
                break;

            chain.Add(current);
        }

        return chain;
    }

    /// <summary>Returns the value of an attribute of the first element of a template chain that has it (optionally restricted to the elements of a kind).</summary>
    private static string? GetTemplateAttribute(List<Element> chain, string name, Func<Element, bool>? applies = null)
    {
        foreach (var element in chain)
        {
            if ((applies is null || applies(element)) && element.GetAnimatedAttributeValue(name) is { } value)
                return value;
        }

        return null;
    }

    /// <summary>
    /// Returns the transform of a paint server (<c>gradientTransform</c>, <c>patternTransform</c>: the presentation attributes of the
    /// transform property) of the first element of the chain that has one.
    /// </summary>
    private static SvgAffine GetTemplateTransform(List<Element> chain)
    {
        foreach (var element in chain)
        {
            // "Specified" is decided on the transform property: an empty or invalid attribute leaves it at none
            if (SvgUserSpace.GetStyle(element) is { } style && style.Layout.HasTransform && LayoutTransforms.GetTransformFunctions(style) != "none")
                return SvgUserSpace.GetTransform(element);
        }

        return SvgAffine.Identity;
    }

    /// <summary>Resolves a coordinate of a paint server: a fraction (or percentage) in objectBoundingBox units, a length of the user space otherwise.</summary>
    private static double ResolvePaintServerLength(Element element, string? value, bool objectBoundingBox, double percentageBasis, double defaultValue)
    {
        if (value is null)
            return defaultValue;

        value = value.Trim();
        if (objectBoundingBox)
        {
            if (value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
                return percentage / 100;

            return SvgUserSpace.ResolveLength(element, value, 1) ?? defaultValue;
        }

        return SvgUserSpace.ResolveLength(element, value, percentageBasis) ?? defaultValue;
    }

    /// <summary>
    /// Creates the paint of a linear or radial gradient (https://svgwg.org/svg2-draft/pservers.html#Gradients): its stops (of the
    /// first gradient of the template chain that has stops), its geometry in its units, its transform and its spread method.
    /// </summary>
    private static DisplayPaint? CreateGradientPaint(Element gradient, Element element, Func<PhysicalRect?> getBoundingBox, SvgPaintContext context)
    {
        var chain = GetTemplateChain(gradient);
        var stopsElement = chain.Find(static item => item.Children.Any(static child => child is SvgStopElement)) ?? gradient;
        var stops = GetGradientStops(stopsElement, out var linearRgb);
        if (stops.Count == 0)
            return null;

        // A single stop paints its color
        if (stops.Count == 1)
            return new DisplayPaint(stops[0].Color);

        var objectBoundingBox = GetTemplateAttribute(chain, "gradientUnits") != "userSpaceOnUse";
        var transform = GetTemplateTransform(chain);
        if (objectBoundingBox)
        {
            // "When the geometry of the applicable element has no width or no height ... the gradient is not rendered"
            if (getBoundingBox() is not { Width: > 0, Height: > 0 } box)
                return null;

            transform = transform.Then(new SvgAffine(box.Width, 0, 0, box.Height, box.X, box.Y));
        }

        var spread = GetTemplateAttribute(chain, "spreadMethod") switch
        {
            "reflect" => SpreadMode.Reflect,
            "repeat" => SpreadMode.Repeat,
            _ => SpreadMode.Pad,
        };

        if (linearRgb)
        {
            stops = ExpandLinearRgbStops(stops);
        }

        var (width, height) = SvgUserSpace.GetViewportSize(element, context.Viewport);
        var gradientStops = stops.ToArray();
        GradientShader shader;
        if (gradient is SvgLinearGradientElement)
        {
            static bool IsLinear(Element e) => e is SvgLinearGradientElement;
            var x1 = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "x1", IsLinear), objectBoundingBox, width, 0);
            var y1 = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "y1", IsLinear), objectBoundingBox, height, 0);
            var x2 = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "x2", IsLinear), objectBoundingBox, width, objectBoundingBox ? 1 : width);
            var y2 = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "y2", IsLinear), objectBoundingBox, height, 0);

            // A gradient vector of zero length paints the color of the last stop
            if (x1 == x2 && y1 == y2)
                return new DisplayPaint(stops[^1].Color);

            shader = new LinearGradientShader(new PhysicalOffset(x1, y1), new PhysicalOffset(x2, y2), gradientStops, spread);
        }
        else
        {
            static bool IsRadial(Element e) => e is SvgRadialGradientElement;
            var diagonal = SvgUserSpace.GetNormalizedDiagonal((width, height));
            var cx = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "cx", IsRadial), objectBoundingBox, width, objectBoundingBox ? 0.5 : width / 2);
            var cy = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "cy", IsRadial), objectBoundingBox, height, objectBoundingBox ? 0.5 : height / 2);
            var r = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "r", IsRadial), objectBoundingBox, diagonal, objectBoundingBox ? 0.5 : diagonal / 2);
            var fx = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "fx", IsRadial), objectBoundingBox, width, cx);
            var fy = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "fy", IsRadial), objectBoundingBox, height, cy);
            var fr = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "fr", IsRadial), objectBoundingBox, diagonal, 0);

            // A negative radius is an error; a radius of zero paints the color of the last stop
            if (r < 0 || fr < 0)
                return null;

            if (r == 0)
                return new DisplayPaint(stops[^1].Color);

            shader = new RadialGradientShader(new PhysicalOffset(fx, fy), fr, new PhysicalOffset(cx, cy), r, gradientStops, spread);
        }

        return new DisplayPaint(DisplayColor.Black) { Shader = shader with { InterpolatesPremultiplied = false, LocalTransform = transform.IsIdentity ? null : DisplayMatrix.FromSvg(transform) } };
    }

    /// <summary>
    /// Returns the stops of a gradient (https://svgwg.org/svg2-draft/pservers.html#StopElement): their offsets clamped to [0, 1] and
    /// increasing, their <c>stop-color</c> with <c>stop-opacity</c>; and whether the colors are interpolated in linearRGB.
    /// </summary>
    private static List<GradientStop> GetGradientStops(Element gradient, out bool linearRgb)
    {
        var stops = new List<GradientStop>();
        var engine = gradient.NodeDocument.GetStyleEngine();
        linearRgb = gradient.IsConnected && engine.GetStyle(gradient).GetComputedValue("color-interpolation") == "linearrgb";
        var previous = 0d;
        foreach (var child in gradient.Children)
        {
            if (child is not SvgStopElement || !child.IsConnected)
                continue;

            var offset = 0d;
            if (child.GetAnimatedAttributeValue("offset") is { } value)
            {
                value = value.Trim();
                if (value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage))
                {
                    offset = percentage / 100;
                }
                else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    offset = number;
                }
            }

            offset = Math.Max(previous, Math.Clamp(offset, 0, 1));
            previous = offset;
            var style = engine.GetStyle(child);
            var color = DisplayColor.Parse(style.GetUsedValue("stop-color")) ?? DisplayColor.Black;
            stops.Add(new GradientStop(offset, color.MultiplyAlpha(GetNumber(style, "stop-opacity"))));
        }

        return stops;
    }

    /// <summary>
    /// Expands the stops of a gradient interpolated in linearRGB (https://svgwg.org/svg2-draft/painting.html#ColorInterpolationProperty)
    /// into stops interpolated in sRGB: colors interpolated in linearRGB between each pair of stops.
    /// </summary>
    private static List<GradientStop> ExpandLinearRgbStops(List<GradientStop> stops)
    {
        var result = new List<GradientStop>((stops.Count * LinearRgbInterpolationSteps) + 1) { stops[0] };
        for (var i = 1; i < stops.Count; i++)
        {
            var from = stops[i - 1];
            var to = stops[i];
            for (var step = 1; step <= LinearRgbInterpolationSteps; step++)
            {
                var t = step / (double)LinearRgbInterpolationSteps;
                var color = new DisplayColor(
                    Interpolate(from.Color.R, to.Color.R, t),
                    Interpolate(from.Color.G, to.Color.G, t),
                    Interpolate(from.Color.B, to.Color.B, t),
                    (float)(from.Color.A + ((to.Color.A - from.Color.A) * t)));
                result.Add(new GradientStop(from.Offset + ((to.Offset - from.Offset) * t), color));
            }
        }

        return result;

        static float Interpolate(float from, float to, double t) => (float)ToSrgb(ToLinear(from) + ((ToLinear(to) - ToLinear(from)) * t));

        static double ToLinear(double value) => value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

        static double ToSrgb(double value) => value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;
    }

    /// <summary>
    /// Creates the paint of a pattern (https://svgwg.org/svg2-draft/pservers.html#PatternElement): its tile (x, y, width and height
    /// in <c>patternUnits</c>) repeated in the coordinates of <c>patternTransform</c>, whose content (the children of the first
    /// pattern of the template chain that has children, in the coordinates of its viewBox or of <c>patternContentUnits</c>) is
    /// painted into a display list clipped to the tile.
    /// </summary>
    private DisplayPaint? CreatePatternPaint(Element pattern, Element element, Func<PhysicalRect?> getBoundingBox, SvgPaintContext context, out bool invalidTile)
    {
        invalidTile = false;
        if (context.Depth >= MaxSvgDepth)
            return null;

        var chain = GetTemplateChain(pattern);
        var objectBoundingBox = GetTemplateAttribute(chain, "patternUnits") != "userSpaceOnUse";
        var contentObjectBoundingBox = GetTemplateAttribute(chain, "patternContentUnits") == "objectBoundingBox";
        var box = getBoundingBox();
        if ((objectBoundingBox || contentObjectBoundingBox) && box is not { Width: > 0, Height: > 0 })
            return null;

        var (width, height) = SvgUserSpace.GetViewportSize(element, context.Viewport);
        var tileX = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "x"), objectBoundingBox, width, 0);
        var tileY = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "y"), objectBoundingBox, height, 0);
        var tileWidth = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "width"), objectBoundingBox, width, 0);
        var tileHeight = ResolvePaintServerLength(element, GetTemplateAttribute(chain, "height"), objectBoundingBox, height, 0);
        if (objectBoundingBox)
        {
            var b = box!.Value;
            tileX = b.X + (tileX * b.Width);
            tileY = b.Y + (tileY * b.Height);
            tileWidth *= b.Width;
            tileHeight *= b.Height;
        }

        // A tile of zero size disables the rendering of the paint (https://svgwg.org/svg2-draft/pservers.html#PatternElementWidthAttribute): as in the browsers, the fallback paint is used
        if (!(tileWidth > 0) || !(tileHeight > 0))
        {
            invalidTile = true;
            return null;
        }

        // The content of the tile: the viewBox (which overrides patternContentUnits) or the bounding box
        SvgAffine contentTransform;
        var viewBoxElement = chain.Find(static item => item.HasAttribute("viewBox"));
        if (viewBoxElement is not null && SvgUserSpace.TryGetViewBox(viewBoxElement, out var viewBox))
        {
            if (viewBox.Width <= 0 || viewBox.Height <= 0)
                return null;

            contentTransform = SvgUserSpace.GetViewBoxTransform(viewBoxElement, tileWidth, tileHeight);
        }
        else
        {
            contentTransform = contentObjectBoundingBox ? SvgAffine.Scale(box!.Value.Width, box.Value.Height) : SvgAffine.Identity;
        }

        var contentElement = chain.Find(static item => item.Children.Any(static child => child is SvgElement)) ?? pattern;
        var tileSize = new PhysicalSize(tileWidth, tileHeight);
        var tile = PaintSvgResourceContent(contentElement, contentTransform, tileSize, context with
        {
            IsResourceContent = true,
            UseInstanceRoot = null,
            ParentStyle = null,
            UseSize = default,
            Source = null,
            Depth = context.Depth + 1,
        });

        var transform = SvgAffine.Translation(tileX, tileY).Then(GetTemplateTransform(chain));
        var shader = new TiledShader(new DisplayListShader(tile, tileSize), tileSize, default, SpreadMode.Repeat, SpreadMode.Repeat) { LocalTransform = DisplayMatrix.FromSvg(transform) };
        return new DisplayPaint(DisplayColor.Black) { Shader = shader };
    }

    /// <summary>
    /// Paints an element referenced by a feImage filter primitive (https://drafts.fxtf.org/filter-effects/#feImageElement) into a
    /// separate display list: the element (with its transform) in the coordinates given by the transform of the user space of the
    /// filtered element, as by a use element.
    /// </summary>
    private DisplayList? RenderSvgElementForFilter(Element element, SvgAffine userToPainting)
    {
        // An element whose filter references itself through feImage is painted a limited number of times
        if (_filterImageDepth >= MaxFilterImageDepth)
            return null;

        var viewport = SvgUserSpace.GetOutermostSvgElement(element) is { } svg && _engine.GetFragments(svg) is [var fragment, ..] ? SvgUserSpace.GetOutermostViewport(fragment) : new SvgOutermostViewport(300, 150, 1);
        var previousBuilder = _builder;
        var previousTransform = _svgBuilderTransform;
        _builder = new DisplayListBuilder();
        _svgBuilderTransform = SvgAffine.Identity;
        _filterImageDepth++;
        try
        {
            PaintSvgElement(element, userToPainting, new SvgPaintContext(viewport) { IsResourceContent = true, Depth = 1 });
            return _builder.ToDisplayList(default);
        }
        finally
        {
            _filterImageDepth--;
            _builder = previousBuilder;
            _svgBuilderTransform = previousTransform;
        }
    }

    /// <summary>
    /// Paints the children of a resource element (a pattern) into a separate display list of a size, in the coordinates given by
    /// <paramref name="contentTransform"/>.
    /// </summary>
    private DisplayList PaintSvgResourceContent(Element resource, SvgAffine contentTransform, PhysicalSize size, SvgPaintContext context)
    {
        var previousBuilder = _builder;
        var previousTransform = _svgBuilderTransform;
        _builder = new DisplayListBuilder();
        _svgBuilderTransform = SvgAffine.Identity;
        try
        {
            var style = resource.NodeDocument.GetStyleEngine().GetStyle(resource);
            PaintSvgChildren(resource, style, contentTransform, context);
            return _builder.ToDisplayList(size);
        }
        finally
        {
            _builder = previousBuilder;
            _svgBuilderTransform = previousTransform;
        }
    }
}
