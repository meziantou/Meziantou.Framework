using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Builds the filter of an SVG filter element referenced by <c>filter: url()</c> (https://drafts.fxtf.org/filter-effects/#FilterElement):
/// the filter region, then each filter primitive with its inputs, its subregion, its color space and its attributes, in the
/// coordinates of the painting (<see cref="SvgResourceSpace"/>).
/// </summary>
internal static class SvgFilterBuilder
{
    /// <summary>
    /// Returns the filter of a filter element applied to an element in a resource space. <paramref name="taintedSource"/> is set when
    /// the source graphic is tainted (https://drafts.fxtf.org/filter-effects/#tainted-filter-primitives: a previous filter used
    /// <c>currentcolor</c>, which may be a :visited color).
    /// </summary>
    /// <param name="filter">The filter element.</param>
    /// <param name="space">The resource space of the filtered element.</param>
    /// <param name="taintedSource">Whether the source graphic is tainted.</param>
    /// <param name="renderElement">Paints an element referenced by a feImage primitive (in the coordinates given by a transform of its user space), or null: the element references are transparent.</param>
    public static FilterGraph Build(Element filter, SvgResourceSpace space, bool taintedSource = false, Func<Element, SvgAffine, DisplayList?>? renderElement = null)
    {
        // https://drafts.fxtf.org/filter-effects/#element-attrdef-filter-filterunits
        var filterUnitsBox = SvgResourceSpace.IsObjectBoundingBox(filter, "filterUnits", defaultIsObjectBoundingBox: true);
        var primitiveUnitsBox = SvgResourceSpace.IsObjectBoundingBox(filter, "primitiveUnits", defaultIsObjectBoundingBox: false);
        var region = space.ResolveRegion(filter, filterUnitsBox, "-10%", "-10%", "120%", "120%");

        // "A negative or zero value disables the effect of the given filter element": nothing is rendered
        if (region.Width <= 0 || region.Height <= 0 || (filterUnitsBox && (space.BoundingBox.Width <= 0 || space.BoundingBox.Height <= 0)))
            return new FilterGraph([], space.ToPainting(region));

        var context = new BuildContext(space, region, primitiveUnitsBox) { TaintedSource = taintedSource, RenderElement = renderElement };
        foreach (var child in GetPrimitivesElement(filter).Children)
        {
            if (child is not SvgElement)
                continue;

            AddPrimitive(context, child);
        }

        return new FilterGraph([.. context.Primitives], space.ToPainting(region));
    }

    /// <summary>
    /// Returns the element whose children are the primitives of a filter element: the filter element, or, when it has no child
    /// element, the filter element referenced by its <c>href</c> (https://drafts.fxtf.org/filter-effects/#element-attrdef-filter-href).
    /// </summary>
    private static Element GetPrimitivesElement(Element filter)
    {
        var current = filter;
        for (var depth = 0; depth < 16 && current.FirstElementChild is null; depth++)
        {
            if (!SvgLinking.TryGetSameDocumentFragment(current, SvgLinking.GetHref(current), out var id) || current.NodeDocument.GetElementById(id) is not SvgFilterElement referenced || ReferenceEquals(referenced, filter))
                break;

            current = referenced;
        }

        return current;
    }

    private sealed class BuildContext(SvgResourceSpace space, PhysicalRect region, bool primitiveUnitsBox)
    {
        public SvgResourceSpace Space { get; } = space;

        /// <summary>The filter region in user units.</summary>
        public PhysicalRect Region { get; } = region;

        public bool PrimitiveUnitsBox { get; } = primitiveUnitsBox;

        public List<FilterPrimitive> Primitives { get; } = [];

        /// <summary>The subregions of the primitives in user units.</summary>
        public List<PhysicalRect> Subregions { get; } = [];

        public Dictionary<string, int> Results { get; } = new(StringComparer.Ordinal);

        public bool TaintedSource { get; init; }

        /// <summary>Paints an element referenced by feImage, or null.</summary>
        public Func<Element, SvgAffine, DisplayList?>? RenderElement { get; init; }

        /// <summary>Whether the results of the primitives are tainted.</summary>
        public List<bool> Tainted { get; } = [];

        public bool IsTainted(int input) => input switch
        {
            FilterInput.SourceGraphic or FilterInput.SourceAlpha => TaintedSource,
            >= 0 => Tainted[input],
            _ => false,
        };

        /// <summary>Converts a number of the primitive units in the x or y axis to the coordinates of the painting.</summary>
        public double ToPaintingLength(double value, bool horizontal)
        {
            if (PrimitiveUnitsBox)
            {
                value *= horizontal ? Space.BoundingBox.Width : Space.BoundingBox.Height;
            }

            return value * Space.Scale;
        }
    }

    private static void AddPrimitive(BuildContext context, Element element)
    {
        var name = element.LocalName;
        int[] inputs;
        switch (name)
        {
            case "feFlood" or "feTurbulence" or "feImage":
                inputs = [];
                break;

            case "feComposite" or "feBlend" or "feDisplacementMap":
                inputs = [ResolveInput(context, element.GetAnimatedAttributeValue("in")), ResolveInput(context, element.GetAnimatedAttributeValue("in2"))];
                break;

            case "feMerge":
            {
                var list = new List<int>();
                foreach (var node in element.Children)
                {
                    if (node.LocalName == "feMergeNode")
                    {
                        list.Add(ResolveInput(context, node.GetAnimatedAttributeValue("in")));
                    }
                }

                inputs = [.. list];
                break;
            }

            case "feGaussianBlur" or "feOffset" or "feDropShadow" or "feColorMatrix" or "feComponentTransfer" or "feMorphology" or "feTile"
                or "feConvolveMatrix" or "feDiffuseLighting" or "feSpecularLighting":
                inputs = [ResolveInput(context, element.GetAnimatedAttributeValue("in"))];
                break;

            default:
                // An unknown element is not a filter primitive
                return;
        }

        var subregion = GetSubregion(context, element, name, inputs);
        var linear = SvgUserSpace.GetComputedValue(element, "color-interpolation-filters") is not ("srgb" or "sRGB");
        var primitive = (name == "feImage" ? CreateImage(context, element, subregion) : CreatePrimitive(context, element, name, inputs)) ?? new FloodPrimitive(DisplayColor.Transparent) { Inputs = [] };
        primitive = primitive with
        {
            Inputs = inputs,
            Subregion = context.Space.ToPainting(subregion),
            LinearRgb = linear,
        };

        // https://drafts.fxtf.org/filter-effects/#tainted-filter-primitives: feDisplacementMap with a tainted displacement map
        // does not displace its input
        if (primitive is DisplacementMapPrimitive && inputs.Length > 1 && context.IsTainted(inputs[1]))
        {
            primitive = new OffsetPrimitive(0, 0) { Inputs = [inputs[0]], Subregion = primitive.Subregion, LinearRgb = linear };
        }

        // A color that uses currentcolor taints the primitive
        var tainted = name switch
        {
            "feFlood" or "feDropShadow" => IsCurrentColor(element, "flood-color"),
            "feDiffuseLighting" or "feSpecularLighting" => IsCurrentColor(element, "lighting-color"),
            _ => false,
        };

        foreach (var input in primitive.Inputs)
        {
            tainted |= context.IsTainted(input);
        }

        context.Primitives.Add(primitive);
        context.Subregions.Add(subregion);
        context.Tainted.Add(tainted);
        if (element.GetAnimatedAttributeValue("result") is { Length: > 0 } result)
        {
            context.Results[result] = context.Primitives.Count - 1;
        }
    }

    /// <summary>
    /// Resolves an input (https://drafts.fxtf.org/filter-effects/#element-attrdef-filter-primitive-in): a standard input, the
    /// result of a previous primitive, or the result of the previous primitive (the source graphic for the first one) when it is
    /// missing or references no result.
    /// </summary>
    private static int ResolveInput(BuildContext context, string? value)
    {
        switch (value?.Trim())
        {
            case "SourceGraphic":
                return FilterInput.SourceGraphic;

            case "SourceAlpha":
                return FilterInput.SourceAlpha;

            case "BackgroundImage" or "BackgroundAlpha" or "FillPaint" or "StrokePaint":
                return FilterInput.Transparent;

            case { Length: > 0 } reference when context.Results.TryGetValue(reference, out var index):
                return index;

            default:
                return context.Primitives.Count > 0 ? context.Primitives.Count - 1 : FilterInput.SourceGraphic;
        }
    }

    /// <summary>
    /// Returns the filter primitive subregion (https://drafts.fxtf.org/filter-effects/#FilterPrimitiveSubRegion) in user units: its
    /// <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c>, the missing ones being those of the union of the subregions of its inputs
    /// (the filter region for a standard input, no input and feTile), clipped to the filter region.
    /// </summary>
    private static PhysicalRect GetSubregion(BuildContext context, Element element, string name, int[] inputs)
    {
        var defaults = context.Region;
        if (name != "feTile" && inputs.Length > 0)
        {
            PhysicalRect? union = null;
            foreach (var input in inputs)
            {
                if (input < 0)
                {
                    union = null;
                    break;
                }

                var rect = context.Subregions[input];
                union = union is { } u ? Union(u, rect) : rect;
            }

            if (union is { } inputsUnion)
            {
                defaults = inputsUnion;
            }
        }

        var space = context.Space;
        var box = context.PrimitiveUnitsBox;
        var x = element.GetAnimatedAttributeValue("x") is { } xValue ? space.ResolveCoordinate(element, xValue, box, horizontal: true, position: true) : defaults.X;
        var y = element.GetAnimatedAttributeValue("y") is { } yValue ? space.ResolveCoordinate(element, yValue, box, horizontal: false, position: true) : defaults.Y;
        var width = element.GetAnimatedAttributeValue("width") is { } widthValue ? space.ResolveCoordinate(element, widthValue, box, horizontal: true, position: false) : defaults.Width;
        var height = element.GetAnimatedAttributeValue("height") is { } heightValue ? space.ResolveCoordinate(element, heightValue, box, horizontal: false, position: false) : defaults.Height;
        var subregion = new PhysicalRect(x, y, Math.Max(0, width), Math.Max(0, height));
        return Intersect(subregion, context.Region);
    }

    /// <summary>
    /// Creates a feImage primitive (https://drafts.fxtf.org/filter-effects/#feImageElement): an element of the document referenced by
    /// href is painted in the user space of the filtered element, at the origin of the subregion; an image (a data: URL, decoded
    /// synchronously, or the image fetched by the element, see <see cref="SvgFEImageElement.Image"/>) is drawn into the subregion
    /// with its preserveAspectRatio. The result is transparent when nothing can be painted (e.g. the image is not loaded yet).
    /// </summary>
    private static ImagePrimitive? CreateImage(BuildContext context, Element element, PhysicalRect userSubregion)
    {
        var href = element.GetAnimatedAttributeValue("href") ?? element.GetAttributeNS(Namespaces.XLink, "href");
        if (string.IsNullOrWhiteSpace(href))
            return null;

        var document = element.NodeDocument;
        if (href.StartsWith('#', StringComparison.Ordinal))
        {
            if (context.RenderElement is not { } render || document.GetElementById(href[1..]) is not SvgElement referenced || referenced.IsInclusiveAncestorOf(element))
                return null;

            // https://drafts.fxtf.org/filter-effects/#feImageElement: "the referenced resource is rendered according to the
            // behavior of the use element": as in the browsers, at the origin of the filter primitive subregion (the x and y of
            // the use element)
            var toPainting = SvgAffine.Translation(userSubregion.X, userSubregion.Y).Then(context.Space.UserToPainting);
            return render(referenced, toPainting) is { } content ? new ImagePrimitive(content) : null;
        }

        // An image resource: a data: URL, or the image fetched by the element (transparent until it is loaded)
        PaintImage image;
        if (Urls.UrlParser.Parse(href, document.DocumentBaseUrl) is { Scheme: "data" } url)
        {
            if (Urls.DataUrl.Process(url) is not var (mimeType, body) || !ImageHeaders.TryGetImageInfo(body, mimeType, out var naturalSize))
                return null;

            image = new PaintImage(url.Serialize(), naturalSize.Width ?? 0, naturalSize.Height ?? 0, body, mimeType);
        }
        else if (element is SvgFEImageElement { Image: { } fetched })
        {
            image = fetched;
        }
        else
        {
            return null;
        }

        var subregion = context.Space.ToPainting(userSubregion);
        if (subregion.IsEmpty)
            return null;

        var builder = new DisplayListBuilder();
        if (image.IsSvg)
        {
            if (SvgImage.Get(image, document) is not { } svgImage)
                return null;

            new PaintedImage.SvgPaintedImage(svgImage, 1, colorScheme: null, element.GetAnimatedAttributeValue("preserveAspectRatio")).Draw(builder, subregion, element);
        }
        else
        {
            if (image.Width <= 0 || image.Height <= 0)
                return null;

            var fit = Painter.GetPreserveAspectRatioTransform(element.GetAnimatedAttributeValue("preserveAspectRatio"), image.Width, image.Height, subregion.Width, subregion.Height);
            builder.Save();
            builder.Add(new ClipRectItem(subregion));
            builder.Add(new DrawImageItem(image, new PhysicalRect(0, 0, image.Width, image.Height), new PhysicalRect(subregion.X + fit.E, subregion.Y + fit.F, image.Width * fit.A, image.Height * fit.D), ImageSampling.Smooth) { Source = element });
            builder.Restore();
        }

        return new ImagePrimitive(builder.ToDisplayList(subregion.Size));
    }

    private static PhysicalRect Union(PhysicalRect a, PhysicalRect b)
    {
        var left = Math.Min(a.Left, b.Left);
        var top = Math.Min(a.Top, b.Top);
        return new PhysicalRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    private static PhysicalRect Intersect(PhysicalRect a, PhysicalRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        return new PhysicalRect(left, top, Math.Max(0, Math.Min(a.Right, b.Right) - left), Math.Max(0, Math.Min(a.Bottom, b.Bottom) - top));
    }

    private static FilterPrimitive? CreatePrimitive(BuildContext context, Element element, string name, int[] inputs)
    {
        switch (name)
        {
            case "feFlood":
                return new FloodPrimitive(GetColor(element, "flood-color", "flood-opacity"));

            case "feGaussianBlur":
            {
                // https://drafts.fxtf.org/filter-effects/#element-attrdef-fegaussianblur-stddeviation: the negative values are treated
                // as zero, zero disables the blur in its axis
                var (x, y) = GetNumberPair(element, "stdDeviation", 0);
                x = Math.Max(0, x);
                y = Math.Max(0, y);

                var edgeMode = element.GetAnimatedAttributeValue("edgeMode") switch
                {
                    "duplicate" => FilterEdgeMode.Duplicate,
                    "wrap" => FilterEdgeMode.Wrap,
                    _ => FilterEdgeMode.None,
                };
                return new GaussianBlurPrimitive(context.ToPaintingLength(x, horizontal: true), context.ToPaintingLength(y, horizontal: false), edgeMode);
            }

            case "feOffset":
                return new OffsetPrimitive(context.ToPaintingLength(GetNumber(element, "dx", 0), horizontal: true), context.ToPaintingLength(GetNumber(element, "dy", 0), horizontal: false));

            case "feDropShadow":
            {
                var (x, y) = GetNumberPair(element, "stdDeviation", 2);
                x = Math.Max(0, x);
                y = Math.Max(0, y);

                return new DropShadowPrimitive(
                    context.ToPaintingLength(GetNumber(element, "dx", 2), horizontal: true),
                    context.ToPaintingLength(GetNumber(element, "dy", 2), horizontal: false),
                    context.ToPaintingLength(x, horizontal: true),
                    context.ToPaintingLength(y, horizontal: false),
                    GetColor(element, "flood-color", "flood-opacity"));
            }

            case "feColorMatrix":
                return new ColorMatrixPrimitive(GetColorMatrix(element));

            case "feComponentTransfer":
                return CreateComponentTransfer(element);

            case "feComposite":
            {
                var op = element.GetAnimatedAttributeValue("operator") switch
                {
                    "in" => CompositeOperator.In,
                    "out" => CompositeOperator.Out,
                    "atop" => CompositeOperator.Atop,
                    "xor" => CompositeOperator.Xor,
                    "lighter" => CompositeOperator.Lighter,
                    "arithmetic" => CompositeOperator.Arithmetic,
                    _ => CompositeOperator.Over,
                };
                return new CompositePrimitive(op, GetNumber(element, "k1", 0), GetNumber(element, "k2", 0), GetNumber(element, "k3", 0), GetNumber(element, "k4", 0));
            }

            case "feBlend":
                return new BlendPrimitive(Painter.ParseBlendMode(element.GetAnimatedAttributeValue("mode") ?? "normal"));

            case "feMerge":
                return new MergePrimitive();

            case "feMorphology":
            {
                // https://drafts.fxtf.org/filter-effects/#element-attrdef-femorphology-radius: a negative or zero value disables the
                // effect (the result is the input)
                var (x, y) = GetNumberPair(element, "radius", 0);
                if (x <= 0 || y <= 0)
                    return new OffsetPrimitive(0, 0);

                return new MorphologyPrimitive(element.GetAnimatedAttributeValue("operator") == "dilate", context.ToPaintingLength(x, horizontal: true), context.ToPaintingLength(y, horizontal: false));
            }

            case "feTurbulence":
            {
                var (frequencyX, frequencyY) = GetNumberPair(element, "baseFrequency", 0);

                // https://drafts.fxtf.org/filter-effects/#element-attrdef-feturbulence-basefrequency: "Negative values are
                // unsupported": the attribute is ignored (its initial value is 0)
                if (frequencyX < 0 || frequencyY < 0)
                {
                    (frequencyX, frequencyY) = (0, 0);
                }

                var octaves = (int)GetNumber(element, "numOctaves", 1);
                var seed = GetNumber(element, "seed", 0);
                var stitch = element.GetAnimatedAttributeValue("stitchTiles") == "stitch";
                var fractal = element.GetAnimatedAttributeValue("type") == "fractalNoise";

                // The noise is a function of the user space (of the bounding box units for primitiveUnits="objectBoundingBox")
                var space = context.Space;
                var transform = DisplayMatrix.FromSvg(space.UserToPainting);
                if (context.PrimitiveUnitsBox)
                {
                    transform = transform.PreConcat(new DisplayMatrix(space.BoundingBox.Width, 0, 0, space.BoundingBox.Height, space.BoundingBox.X, space.BoundingBox.Y));
                }

                return new TurbulencePrimitive(frequencyX, frequencyY, octaves, seed, stitch, fractal, transform);
            }

            case "feTile":
            {
                var input = inputs.Length > 0 ? inputs[0] : FilterInput.SourceGraphic;
                var inputSubregion = input >= 0 ? context.Subregions[input] : context.Region;
                return new TilePrimitive(context.Space.ToPainting(inputSubregion));
            }

            case "feConvolveMatrix":
                return CreateConvolveMatrix(element);

            case "feDisplacementMap":
            {
                var scale = GetNumber(element, "scale", 0);
                return new DisplacementMapPrimitive(context.PrimitiveUnitsBox ? scale * context.Space.BoundingBox.Width * context.Space.Scale : scale * context.Space.Scale, ParseChannel(element.GetAnimatedAttributeValue("xChannelSelector")), ParseChannel(element.GetAnimatedAttributeValue("yChannelSelector")));
            }

            case "feDiffuseLighting" or "feSpecularLighting":
                return CreateLighting(context, element, name == "feSpecularLighting");

            default:
                return null;
        }

        static int ParseChannel(string? value) => value switch
        {
            "R" => 0,
            "G" => 1,
            "B" => 2,
            _ => 3,
        };
    }

    /// <summary>Whether a color property uses currentcolor (also in a color function, e.g. <c>color-mix()</c>).</summary>
    private static bool IsCurrentColor(Element element, string property)
        => SvgUserSpace.GetStyle(element)?.GetComputedValue(property) is { } value && value.Contains("currentcolor", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns the color of <c>flood-color</c> or <c>lighting-color</c> with its opacity.</summary>
    private static DisplayColor GetColor(Element element, string colorProperty, string? opacityProperty)
    {
        var style = SvgUserSpace.GetStyle(element);
        var color = style is null ? DisplayColor.Black : DisplayColor.Parse(style.GetUsedValue(colorProperty)) ?? DisplayColor.Black;
        if (opacityProperty is not null && style is not null)
        {
            var value = style.GetComputedValue(opacityProperty);
            var opacity = value.EndsWith('%', StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage)
                ? percentage / 100
                : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 1;
            color = color.MultiplyAlpha(Math.Clamp(opacity, 0, 1));
        }

        return color;
    }

    private static double GetNumber(Element element, string attribute, double defaultValue)
        => SvgParsing.TryParseSingleNumber(element.GetAnimatedAttributeValue(attribute), out var value) ? value : defaultValue;

    /// <summary>Returns a &lt;number-optional-number&gt; attribute (the second number is the first one when it is missing).</summary>
    private static (double X, double Y) GetNumberPair(Element element, string attribute, double defaultValue)
    {
        if (!SvgParsing.TryParseNumberList(element.GetAnimatedAttributeValue(attribute), out var values) || values.Count is 0 or > 2)
            return (defaultValue, defaultValue);

        return (values[0], values.Count > 1 ? values[1] : values[0]);
    }

    /// <summary>The matrix of feColorMatrix (https://drafts.fxtf.org/filter-effects/#feColorMatrixElement).</summary>
    private static float[] GetColorMatrix(Element element)
    {
        var hasValues = SvgParsing.TryParseNumberList(element.GetAnimatedAttributeValue("values"), out var values) && element.GetAnimatedAttributeValue("values") is not null;
        switch (element.GetAnimatedAttributeValue("type"))
        {
            case "saturate":
            {
                var s = hasValues && values.Count == 1 ? values[0] : 1;
                return
                [
                    0.213f + (0.787f * s), 0.715f - (0.715f * s), 0.072f - (0.072f * s), 0, 0,
                    0.213f - (0.213f * s), 0.715f + (0.285f * s), 0.072f - (0.072f * s), 0, 0,
                    0.213f - (0.213f * s), 0.715f - (0.715f * s), 0.072f + (0.928f * s), 0, 0,
                    0, 0, 0, 1, 0,
                ];
            }

            case "hueRotate":
            {
                var radians = (hasValues && values.Count == 1 ? values[0] : 0) * Math.PI / 180;
                var cos = (float)Math.Cos(radians);
                var sin = (float)Math.Sin(radians);
                return
                [
                    0.213f + (cos * 0.787f) - (sin * 0.213f), 0.715f - (cos * 0.715f) - (sin * 0.715f), 0.072f - (cos * 0.072f) + (sin * 0.928f), 0, 0,
                    0.213f - (cos * 0.213f) + (sin * 0.143f), 0.715f + (cos * 0.285f) + (sin * 0.140f), 0.072f - (cos * 0.072f) - (sin * 0.283f), 0, 0,
                    0.213f - (cos * 0.213f) - (sin * 0.787f), 0.715f - (cos * 0.715f) + (sin * 0.715f), 0.072f + (cos * 0.928f) + (sin * 0.072f), 0, 0,
                    0, 0, 0, 1, 0,
                ];
            }

            case "luminanceToAlpha":
                return [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.2125f, 0.7154f, 0.0721f, 0, 0];

            default:
                // matrix: 20 values (the identity when the list is missing or invalid)
                return hasValues && values.Count == 20 ? [.. values] : [1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0];
        }
    }

    /// <summary>feComponentTransfer (https://drafts.fxtf.org/filter-effects/#feComponentTransferElement): a table per channel.</summary>
    private static ComponentTransferPrimitive CreateComponentTransfer(Element element)
    {
        byte[]? red = null;
        byte[]? green = null;
        byte[]? blue = null;
        byte[]? alpha = null;
        foreach (var child in element.Children)
        {
            switch (child.LocalName)
            {
                case "feFuncR":
                    red = CreateTable(child);
                    break;
                case "feFuncG":
                    green = CreateTable(child);
                    break;
                case "feFuncB":
                    blue = CreateTable(child);
                    break;
                case "feFuncA":
                    alpha = CreateTable(child);
                    break;
            }
        }

        return new ComponentTransferPrimitive(red, green, blue, alpha);

        static byte[]? CreateTable(Element function)
        {
            var type = function.GetAnimatedAttributeValue("type");
            if (!SvgParsing.TryParseNumberList(function.GetAnimatedAttributeValue("tableValues"), out var tableValues))
            {
                tableValues = [];
            }

            var slope = GetNumber(function, "slope", 1);
            var intercept = GetNumber(function, "intercept", 0);
            var amplitude = GetNumber(function, "amplitude", 1);
            var exponent = GetNumber(function, "exponent", 1);
            var offset = GetNumber(function, "offset", 0);
            if (type is not ("table" or "discrete" or "linear" or "gamma") || (type is "table" or "discrete" && tableValues.Count == 0))
                return null;

            var table = new byte[256];
            for (var i = 0; i < table.Length; i++)
            {
                var c = i / 255d;
                double result;
                switch (type)
                {
                    case "table":
                    {
                        var n = tableValues.Count - 1;
                        if (n == 0)
                        {
                            result = tableValues[0];
                            break;
                        }

                        var k = Math.Min((int)Math.Floor(c * n), n - 1);
                        result = tableValues[k] + ((c - ((double)k / n)) * n * (tableValues[k + 1] - tableValues[k]));
                        break;
                    }

                    case "discrete":
                    {
                        var n = tableValues.Count;
                        var k = Math.Min((int)Math.Floor(c * n), n - 1);
                        result = tableValues[k];
                        break;
                    }

                    case "linear":
                        result = (slope * c) + intercept;
                        break;

                    default:
                        result = (amplitude * Math.Pow(c, exponent)) + offset;
                        break;
                }

                table[i] = (byte)Math.Round(Math.Clamp(result, 0, 1) * 255, MidpointRounding.AwayFromZero);
            }

            return table;
        }
    }

    /// <summary>feConvolveMatrix (https://drafts.fxtf.org/filter-effects/#feConvolveMatrixElement).</summary>
    private static ConvolveMatrixPrimitive? CreateConvolveMatrix(Element element)
    {
        var (orderX, orderY) = GetNumberPair(element, "order", 3);
        if (orderX < 1 || orderY < 1 || orderX != Math.Floor(orderX) || orderY != Math.Floor(orderY))
            return null;

        var x = (int)orderX;
        var y = (int)orderY;
        if (!SvgParsing.TryParseNumberList(element.GetAnimatedAttributeValue("kernelMatrix"), out var kernel) || kernel.Count != x * y)
            return null;

        var sum = 0d;
        foreach (var value in kernel)
        {
            sum += value;
        }

        var divisor = GetNumber(element, "divisor", sum == 0 ? 1 : sum);
        if (divisor == 0)
        {
            divisor = 1;
        }

        var targetX = (int)GetNumber(element, "targetX", Math.Floor(x / 2d));
        var targetY = (int)GetNumber(element, "targetY", Math.Floor(y / 2d));
        if (targetX < 0 || targetX >= x || targetY < 0 || targetY >= y)
            return null;

        var edgeMode = element.GetAnimatedAttributeValue("edgeMode") switch
        {
            "none" => FilterEdgeMode.None,
            "wrap" => FilterEdgeMode.Wrap,
            _ => FilterEdgeMode.Duplicate,
        };
        return new ConvolveMatrixPrimitive(x, y, [.. kernel], divisor, GetNumber(element, "bias", 0), targetX, targetY, edgeMode, element.GetAnimatedAttributeValue("preserveAlpha") == "true");
    }

    /// <summary>feDiffuseLighting and feSpecularLighting (https://drafts.fxtf.org/filter-effects/#feDiffuseLightingElement).</summary>
    private static LightingPrimitive? CreateLighting(BuildContext context, Element element, bool specular)
    {
        FilterLight? light = null;
        foreach (var child in element.Children)
        {
            switch (child.LocalName)
            {
                case "feDistantLight":
                    light = new DistantLight(GetNumber(child, "azimuth", 0), GetNumber(child, "elevation", 0));
                    break;

                case "fePointLight":
                {
                    var point = ToPainting(context, child, "x", "y", "z");
                    light = new PointLight(point.X, point.Y, point.Z);
                    break;
                }

                case "feSpotLight":
                {
                    var point = ToPainting(context, child, "x", "y", "z");
                    var target = ToPainting(context, child, "pointsAtX", "pointsAtY", "pointsAtZ");
                    double? cone = child.GetAnimatedAttributeValue("limitingConeAngle") is not null ? GetNumber(child, "limitingConeAngle", 90) : null;
                    light = new SpotLight(point.X, point.Y, point.Z, target.X, target.Y, target.Z, GetNumber(child, "specularExponent", 1), cone);
                    break;
                }

                default:
                    continue;
            }

            break;
        }

        if (light is null)
            return null;

        var constant = GetNumber(element, specular ? "specularConstant" : "diffuseConstant", 1);
        var exponent = GetNumber(element, "specularExponent", 1);
        return new LightingPrimitive(specular, light, GetColor(element, "lighting-color", opacityProperty: null), GetNumber(element, "surfaceScale", 1), constant, exponent);

        static (double X, double Y, double Z) ToPainting(BuildContext context, Element light, string xAttribute, string yAttribute, string zAttribute)
        {
            var x = GetNumber(light, xAttribute, 0);
            var y = GetNumber(light, yAttribute, 0);
            var z = GetNumber(light, zAttribute, 0);
            var space = context.Space;
            if (context.PrimitiveUnitsBox)
            {
                x = space.BoundingBox.X + (x * space.BoundingBox.Width);
                y = space.BoundingBox.Y + (y * space.BoundingBox.Height);
                z *= Math.Sqrt(((space.BoundingBox.Width * space.BoundingBox.Width) + (space.BoundingBox.Height * space.BoundingBox.Height)) / 2);
            }

            var point = space.ToPainting(x, y);
            return (point.X, point.Y, z * space.Scale);
        }
    }
}
