using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Svg;

/// <summary>
/// The viewport of an outermost svg element in its user units: the content box of its CSS box divided by its effective zoom
/// (https://svgwg.org/svg2-draft/coords.html#ViewportSpace), and the zoom that maps the user units to CSS px.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct SvgOutermostViewport(double Width, double Height, double Zoom)
{
    /// <summary>
    /// The svg or symbol element referenced by a use element whose size is overridden by the width and height of the use element
    /// (https://svgwg.org/svg2-draft/struct.html#UseLayout), when its element instances are painted, or null.
    /// </summary>
    public Element? OverriddenViewportElement { get; init; }

    /// <summary>The size of the viewport of <see cref="OverriddenViewportElement"/>.</summary>
    public (double Width, double Height) OverriddenViewportSize { get; init; }
}

/// <summary>
/// The coordinate systems of SVG (https://svgwg.org/svg2-draft/coords.html): the lengths of the geometry attributes and
/// properties in user units, the SVG viewports (the viewBox and the size of the svg elements, the content box of the CSS box of
/// the outermost svg element), the transforms of the elements (the transform property, whose presentation attribute is the
/// transform attribute, around transform-origin in the reference box of transform-box) and the current transformation
/// matrices (<c>getCTM()</c>, <c>getScreenCTM()</c>, the mapping to the CSS box of the outermost svg element).
/// </summary>
internal static class SvgUserSpace
{
    /// <summary>Returns the computed style of an element of a connected document, or null.</summary>
    public static ComputedStyle? GetStyle(Element element) => element.IsConnected ? element.NodeDocument.GetStyleEngine().GetStyle(element) : null;

    /// <summary>Returns the computed value of a property (or the value of the attribute of a disconnected element).</summary>
    public static string? GetComputedValue(Element element, string property) => GetStyle(element)?.GetComputedValue(property) ?? element.GetAnimatedAttributeValue(property);

    /// <summary>
    /// Returns the outermost svg element of an element of an SVG document fragment (https://svgwg.org/svg2-draft/struct.html#TermOutermostSVGElement):
    /// the element itself for an outermost svg element, or null when the element is not in an svg element (an svg element in
    /// a foreignObject element is an outermost svg element).
    /// </summary>
    public static SvgSvgElement? GetOutermostSvgElement(Element element)
    {
        var outermost = element as SvgSvgElement;
        if (element.ParentNode is not SvgElement)
            return outermost;

        for (var ancestor = element.ParentNode; ancestor is SvgElement svgAncestor; ancestor = ancestor.ParentNode)
        {
            if (svgAncestor is SvgSvgElement svg)
            {
                outermost = svg;
            }

            if (svgAncestor is SvgForeignObjectElement)
                break;
        }

        return outermost;
    }

    /// <summary>Whether an svg element is an outermost svg element (its parent is not an SVG element, or is a foreignObject element).</summary>
    public static bool IsOutermost(SvgSvgElement svg) => svg.ParentNode is not SvgElement || svg.ParentNode is SvgForeignObjectElement;

    /// <summary>
    /// Returns the viewport of an outermost svg element from the layout (its content box), computing the layout when it is out
    /// of date; null when the element has no CSS box (the document has no browsing context, it is not rendered...).
    /// </summary>
    public static SvgOutermostViewport? GetOutermostViewport(SvgSvgElement outermost)
    {
        if (CssomViewGeometry.GetLayoutEngine(outermost) is not { IsInLayout: false } engine || engine.GetFragments(outermost) is not [var fragment, ..])
            return null;

        return GetOutermostViewport(fragment);
    }

    /// <summary>Returns the viewport of an outermost svg element from its fragment.</summary>
    public static SvgOutermostViewport GetOutermostViewport(BoxFragment fragment)
    {
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        var content = fragment.ContentBoxRect;
        return new SvgOutermostViewport(content.Width / zoom, content.Height / zoom, zoom);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Lengths (https://svgwg.org/svg2-draft/coords.html#Units)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a length (a computed value, or the value of a geometry attribute: user units, absolute and relative lengths,
    /// percentages and math functions) to user units, or returns null (auto, invalid value).
    /// </summary>
    public static double? ResolveLength(Element element, string? value, double percentageBasis)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "auto")
            return null;

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number;

        if (BoxSizes.ResolveLength(value, percentageBasis) is { } pixels)
            return pixels;

        if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is [CssPreservedToken { Type: CssTokenType.Dimension } dimension])
        {
            var unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
            if (unit is "em" or "rem")
            {
                var fontSize = unit == "rem" ? (element.NodeDocument.DocumentElement is { } root ? GetStyle(root)?.FontSize : null) : GetStyle(element)?.FontSize;
                return dimension.Token.NumericValue * (fontSize ?? CssValueComputer.MediumFontSize);
            }

            if (unit == "ex")
                return dimension.Token.NumericValue * (GetStyle(element)?.GetFontRelativeLength(FontRelativeUnit.Ex) ?? (CssValueComputer.MediumFontSize * Css.Fonts.BundledFonts.DefaultRatios.XHeight));

            if (CssCalc.TryConvertToCanonicalUnit(dimension.Token.NumericValue, unit, out var converted, out var canonicalUnit) && canonicalUnit == "px")
                return converted;

            // The other relative units (cap, ch, ic, lh, the units of the root element, the viewport and container units)
            if (GetUnitSize(element, attribute: "", unit) is { } unitSize)
                return dimension.Token.NumericValue * unitSize;
        }

        return null;
    }

    /// <summary>
    /// Returns the size in user units of one unit of a length reflected by an attribute of an element (an <c>SVGLength</c>,
    /// https://svgwg.org/svg2-draft/types.html#InterfaceSVGLength), or null when it cannot be resolved (the element is not
    /// connected): a percentage of the width, the height or the normalized diagonal of the SVG viewport of the element, depending
    /// on the attribute; the font-relative units of the element (and of the root element); the viewport units; the absolute units.
    /// </summary>
    public static double? GetUnitSize(Element element, string attribute, string unit)
    {
        if (GetStyle(element) is not { } style)
            return null;

        unit = InfraAscii.ToAsciiLowercase(unit);
        if (unit == "%")
        {
            var viewport = GetViewportSize(element);
            return attribute switch
            {
                "x" or "cx" or "dx" or "x1" or "x2" or "width" or "rx" or "fx" or "refX" or "markerWidth" or "textLength" => viewport.Width,
                "y" or "cy" or "dy" or "y1" or "y2" or "height" or "ry" or "fy" or "refY" or "markerHeight" => viewport.Height,
                _ => GetNormalizedDiagonal(viewport),
            } / 100;
        }

        var root = element.NodeDocument.DocumentElement is { } documentElement ? GetStyle(documentElement) : null;
        var (viewportWidth, viewportHeight) = element.NodeDocument.DefaultView is not null ? style.Engine.GetViewportUnitSize(style.Environment) : (0d, 0d);
        return unit switch
        {
            "em" => style.FontSize,
            "ex" => style.GetFontRelativeLength(FontRelativeUnit.Ex),
            "ch" => style.GetFontRelativeLength(FontRelativeUnit.Ch),
            "ic" => style.GetFontRelativeLength(FontRelativeUnit.Ic),
            "cap" => style.GetFontRelativeLength(FontRelativeUnit.Cap),
            "lh" => style.LineHeight,
            "rem" => root?.FontSize ?? CssValueComputer.MediumFontSize,
            "rex" => root?.GetFontRelativeLength(FontRelativeUnit.Ex),
            "rch" => root?.GetFontRelativeLength(FontRelativeUnit.Ch),
            "ric" => root?.GetFontRelativeLength(FontRelativeUnit.Ic),
            "rcap" => root?.GetFontRelativeLength(FontRelativeUnit.Cap),
            "rlh" => root?.LineHeight,
            "vw" or "svw" or "lvw" or "dvw" or "vi" or "svi" or "lvi" or "dvi" => viewportWidth / 100,
            "vh" or "svh" or "lvh" or "dvh" or "vb" or "svb" or "lvb" or "dvb" => viewportHeight / 100,
            "vmin" or "svmin" or "lvmin" or "dvmin" => Math.Min(viewportWidth, viewportHeight) / 100,
            "vmax" or "svmax" or "lvmax" or "dvmax" => Math.Max(viewportWidth, viewportHeight) / 100,

            // https://drafts.csswg.org/css-conditional-5/#container-lengths: the size of the query container, else of the small viewport
            "cqw" => ContainerWidth(),
            "cqh" => ContainerHeight(),
            "cqi" => style.Layout.IsHorizontalWritingMode ? ContainerWidth() : ContainerHeight(),
            "cqb" => style.Layout.IsHorizontalWritingMode ? ContainerHeight() : ContainerWidth(),
            "cqmin" => Math.Min(ContainerWidth(), ContainerHeight()),
            "cqmax" => Math.Max(ContainerWidth(), ContainerHeight()),
            _ => CssCalc.TryConvertToCanonicalUnit(1, unit, out var converted, out var canonicalUnit) && canonicalUnit == "px" ? converted : null,
        };

        double ContainerWidth() => (ContainerQueries.GetContainerSize(style, horizontal: true) ?? viewportWidth) / 100;

        double ContainerHeight() => (ContainerQueries.GetContainerSize(style, horizontal: false) ?? viewportHeight) / 100;
    }

    /// <summary>The percentage basis of the lengths that are not along an axis (e.g. r): the normalized diagonal of the viewport (https://svgwg.org/svg2-draft/coords.html#Units).</summary>
    public static double GetNormalizedDiagonal((double Width, double Height) viewport) => Math.Sqrt(((viewport.Width * viewport.Width) + (viewport.Height * viewport.Height)) / 2);

    // ----------------------------------------------------------------------------------------------------------------
    // Viewports (https://svgwg.org/svg2-draft/coords.html#EstablishingANewSVGViewport)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the value of the viewBox of an element: its attribute, or for the root element of an SVG document the viewBox of
    /// the view given by the fragment of the URL of the document (<see cref="SvgViewSpec"/>).
    /// </summary>
    public static string? GetViewBoxValue(Element svg)
        => svg.ParentNode is Document document && SvgViewSpec.Get(document)?.ViewBox is { } view ? view : svg.GetAnimatedAttributeValue("viewBox");

    /// <summary>Parses the viewBox attribute of an element: false when it is missing or invalid (a negative size), true with an empty size that disables the rendering.</summary>
    public static bool TryGetViewBox(Element svg, out (double X, double Y, double Width, double Height) viewBox)
    {
        viewBox = default;
        if (!SvgParsing.TryParseNumberList(GetViewBoxValue(svg), out var numbers) || numbers.Count != 4 || numbers[2] < 0 || numbers[3] < 0)
            return false;

        viewBox = (numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    /// <summary>
    /// The size of the SVG viewport of an element (https://svgwg.org/svg2-draft/coords.html#Units), which its percentages refer
    /// to: the viewBox of the nearest svg ancestor, or its size; for an outermost svg element, its content box from the layout
    /// (<paramref name="outermost"/>, else computed from the layout), or its computed size in the viewport of the browsing
    /// context when it has no CSS box.
    /// </summary>
    public static (double Width, double Height) GetViewportSize(Element element, SvgOutermostViewport? outermost = null)
    {
        for (var ancestor = element.ParentNode; ancestor is SvgElement svgAncestor and not SvgForeignObjectElement; ancestor = ancestor.ParentNode)
        {
            // The svg or symbol element referenced by a use element whose width and height override its size
            if (outermost is { OverriddenViewportElement: { } overridden } && ReferenceEquals(svgAncestor, overridden))
                return TryGetViewBox(svgAncestor, out var viewBox) ? (viewBox.Width, viewBox.Height) : outermost.Value.OverriddenViewportSize;

            if (svgAncestor is SvgSvgElement svg)
                return GetViewportSizeEstablishedBy(svg, outermost);
        }

        return GetInitialViewportSize(element);
    }

    /// <summary>The size of the viewport established by an svg element for its children: its viewBox, else its size.</summary>
    public static (double Width, double Height) GetViewportSizeEstablishedBy(SvgSvgElement svg, SvgOutermostViewport? outermost = null)
    {
        if (TryGetViewBox(svg, out var viewBox))
            return (viewBox.Width, viewBox.Height);

        return GetSvgSize(svg, outermost);
    }

    /// <summary>The size of the viewport of an svg element (its width and height): the content box of an outermost svg element.</summary>
    public static (double Width, double Height) GetSvgSize(SvgSvgElement svg, SvgOutermostViewport? outermost = null)
    {
        if (IsOutermost(svg))
        {
            if ((outermost ?? GetOutermostViewport(svg)) is { } viewport)
                return (viewport.Width, viewport.Height);

            var (initialWidth, initialHeight) = GetInitialViewportSize(svg);
            return (ResolveLength(svg, GetComputedValue(svg, "width"), initialWidth) ?? (svg.ParentNode is Document ? initialWidth : 300), ResolveLength(svg, GetComputedValue(svg, "height"), initialHeight) ?? (svg.ParentNode is Document ? initialHeight : 150));
        }

        // The width and height attributes of a nested svg element are not presentation attributes, and the properties do not
        // apply to it (https://github.com/w3c/svgwg/issues/1057)
        var (width, height) = GetViewportSize(svg, outermost);
        return (ResolveLength(svg, svg.GetAnimatedAttributeValue("width"), width) ?? width, ResolveLength(svg, svg.GetAnimatedAttributeValue("height"), height) ?? height);
    }

    private static (double Width, double Height) GetInitialViewportSize(Element element)
    {
        if (element.NodeDocument.DefaultView is { } window)
            return (window.InnerWidth, window.InnerHeight);

        return (300, 150);
    }

    /// <summary>
    /// The transform of the viewBox of an element for a viewport size (https://svgwg.org/svg2-draft/coords.html#ComputingAViewportsTransform),
    /// with its preserveAspectRatio attribute. The identity when it has no valid viewBox.
    /// </summary>
    public static SvgAffine GetViewBoxTransform(Element svg, double viewportWidth, double viewportHeight)
    {
        if (!TryGetViewBox(svg, out var viewBox) || viewBox.Width <= 0 || viewBox.Height <= 0)
            return SvgAffine.Identity;

        var scaleX = viewportWidth / viewBox.Width;
        var scaleY = viewportHeight / viewBox.Height;
        // The preserveAspectRatio of the view of the document (the document of an SVG image sets the one that applies on its root)
        var viewPreserveAspectRatio = svg.ParentNode is Document { SvgImage: null } document ? SvgViewSpec.Get(document)?.PreserveAspectRatio : null;
        var preserveAspectRatio = (viewPreserveAspectRatio ?? svg.GetAnimatedAttributeValue("preserveAspectRatio") ?? "xMidYMid meet").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var align = preserveAspectRatio.Length > 0 ? preserveAspectRatio[0] : "xMidYMid";
        var translateX = -viewBox.X * scaleX;
        var translateY = -viewBox.Y * scaleY;
        if (align != "none")
        {
            var scale = preserveAspectRatio.Length > 1 && preserveAspectRatio[1] == "slice" ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);
            scaleX = scaleY = scale;
            translateX = -viewBox.X * scale;
            translateY = -viewBox.Y * scale;
            if (align.Contains("xMid", StringComparison.Ordinal))
            {
                translateX += (viewportWidth - (viewBox.Width * scale)) / 2;
            }
            else if (align.Contains("xMax", StringComparison.Ordinal))
            {
                translateX += viewportWidth - (viewBox.Width * scale);
            }

            if (align.Contains("YMid", StringComparison.Ordinal))
            {
                translateY += (viewportHeight - (viewBox.Height * scale)) / 2;
            }
            else if (align.Contains("YMax", StringComparison.Ordinal))
            {
                translateY += viewportHeight - (viewBox.Height * scale);
            }
        }

        return new SvgAffine(scaleX, 0, 0, scaleY, translateX, translateY);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Transforms (https://drafts.csswg.org/css-transforms-1/#transform-rendering)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Returns the transform of an element in the user space of its parent (https://drafts.csswg.org/css-transforms-1/#transform-rendering):
    /// the <c>translate</c>, <c>rotate</c>, <c>scale</c>, offset path (<see cref="MotionPath"/>) and <c>transform</c> properties
    /// (the transform attribute is the presentation attribute of <c>transform</c>) around <c>transform-origin</c>, in the
    /// reference box of <c>transform-box</c>.
    /// </summary>
    public static SvgAffine GetTransform(Element element, SvgOutermostViewport? outermost = null)
    {
        // https://svgwg.org/specs/animations/#AnimateMotionElement: the supplemental transform of the animateMotion elements is
        // applied after the transform of the element (in the user space of its parent)
        var transform = GetTransformWithoutMotion(element, outermost);
        return element.NodeDocument.SmilOrNull?.GetMotionTransform(element) is { } motion ? transform.Then(motion) : transform;
    }

    private static SvgAffine GetTransformWithoutMotion(Element element, SvgOutermostViewport? outermost)
    {
        if (GetStyle(element) is not { } style)
        {
            // A disconnected element has no style: its transform attribute
            var result = SvgAffine.Identity;
            if (SvgTransform.TryParseList(element.GetAnimatedAttributeValue("transform"), out var transforms))
            {
                foreach (var transform in transforms)
                {
                    var m = transform.Matrix;
                    result = new SvgAffine(m.A, m.B, m.C, m.D, m.E, m.F).Then(result);
                }
            }

            return result;
        }

        if (!style.Layout.HasTransform)
            return SvgAffine.Identity;

        var hasOffsetPath = MotionPath.HasOffsetPath(style);
        var functions = LayoutTransforms.GetTransformFunctions(style);
        if (functions == "none" && !hasOffsetPath)
            return SvgAffine.Identity;

        var reference = GetTransformReferenceBox(element, style.GetComputedValue("transform-box"), outermost);
        var (originX, originY, _) = LayoutTransforms.GetOrigin(style, (reference.Width, reference.Height));
        originX += reference.X;
        originY += reference.Y;

        // https://drafts.csswg.org/css-transforms-2/#ctm: the offset transform of the motion path is applied after translate,
        // rotate and scale, and before transform
        double[]? matrix;
        if (hasOffsetPath)
        {
            var (viewportWidth, viewportHeight) = GetViewportSize(element, outermost);
            var offset = MotionPath.GetMatrix(style, element, new PhysicalSize(viewportWidth, viewportHeight), new PhysicalOffset(originX, originY), new PhysicalRect(reference.X, reference.Y, reference.Width, reference.Height));
            var before = ToMatrix(LayoutTransforms.GetTransformFunctions(style, individual: true, transform: false));
            var after = ToMatrix(LayoutTransforms.GetTransformFunctions(style, individual: false, transform: true));
            if (before is null || after is null)
                return SvgAffine.Identity;

            matrix = offset is null ? CssTransforms.Multiply(before, after) : CssTransforms.Multiply(CssTransforms.Multiply(before, offset), after);
        }
        else
        {
            matrix = ToMatrix(functions);
            if (matrix is null)
                return SvgAffine.Identity;
        }

        double[]? ToMatrix(string value)
            => value == "none" ? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1] : CssTransforms.ToMatrix(CssParser.ParseListOfComponentValues(value), CssCalcContext.Default, (reference.Width, reference.Height), out _);

        return SvgAffine.Translation(-originX, -originY).Then(SvgAffine.FromMatrix(matrix)).Then(SvgAffine.Translation(originX, originY));
    }

    /// <summary>
    /// Returns the reference box of the transforms of an SVG element without CSS box (https://drafts.csswg.org/css-transforms-1/#transform-box),
    /// in its user space: its object bounding box for <c>fill-box</c> (and <c>content-box</c>), its stroke bounding box for
    /// <c>stroke-box</c> (and <c>border-box</c>), the nearest SVG viewport for <c>view-box</c> (at the origin of the coordinate
    /// system of its viewBox, with the size of the viewBox). The used value of <c>stroke-box</c> is <c>fill-box</c> when the
    /// stroke bounding box depends on the transform: the element or one of its descendants has a non-scaling stroke
    /// (https://github.com/w3c/csswg-drafts/issues/9640).
    /// </summary>
    public static (double X, double Y, double Width, double Height) GetTransformReferenceBox(Element element, string transformBox, SvgOutermostViewport? outermost = null)
    {
        switch (transformBox)
        {
            case "fill-box" or "content-box" or "stroke-box" or "border-box":
                var stroke = transformBox is "stroke-box" or "border-box" && !HasNonScalingStroke(element, depth: 0);
                var box = element is SvgElement svgElement ? SvgBoundingBox.Compute(svgElement, fill: true, stroke, outermost) : new DomRect();
                return (box.X, box.Y, box.Width, box.Height);
            default:
                var (width, height) = GetViewportSize(element, outermost);
                return (0, 0, width, height);
        }
    }

    /// <summary>Whether an element or one of its descendants has <c>vector-effect: non-scaling-stroke</c> (https://svgwg.org/svg2-draft/coords.html#VectorEffects).</summary>
    private static bool HasNonScalingStroke(Element element, int depth)
    {
        if (GetComputedValue(element, "vector-effect") == "non-scaling-stroke")
            return true;

        if (depth > MaxNonScalingStrokeDepth)
            return false;

        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child is SvgElement && HasNonScalingStroke(child, depth + 1))
                return true;
        }

        return false;
    }

    private const int MaxNonScalingStrokeDepth = 64;

    /// <summary>
    /// The transform from the user space of an element to the user space of its parent: its transform, and the viewport of a
    /// nested svg element (its x and y, and the transform of its viewBox); the translation of a use element by x and y is
    /// applied by <see cref="GetUseTranslation"/>.
    /// </summary>
    public static SvgAffine GetTransformToParent(Element element, SvgOutermostViewport? outermost = null)
    {
        var transform = GetTransform(element, outermost);
        if (element is SvgSvgElement svg && !IsOutermost(svg))
        {
            transform = GetNestedViewportTransform(svg, outermost).Then(transform);
        }

        return transform;
    }

    /// <summary>The transform from the user space of the children of a nested svg element to its own user space: its viewBox, then the translation by x and y.</summary>
    public static SvgAffine GetNestedViewportTransform(SvgSvgElement svg, SvgOutermostViewport? outermost = null)
    {
        var (width, height) = GetViewportSize(svg, outermost);
        var x = ResolveLength(svg, GetComputedValue(svg, "x"), width) ?? 0;
        var y = ResolveLength(svg, GetComputedValue(svg, "y"), height) ?? 0;
        var (viewportWidth, viewportHeight) = GetSvgSize(svg, outermost);
        return GetViewBoxTransform(svg, viewportWidth, viewportHeight).Then(SvgAffine.Translation(x, y));
    }

    /// <summary>The translation of the referenced content of a use element by its x and y (https://svgwg.org/svg2-draft/struct.html#UseLayout).</summary>
    public static SvgAffine GetUseTranslation(Element use, SvgOutermostViewport? outermost = null)
    {
        var (width, height) = GetViewportSize(use, outermost);
        var x = ResolveLength(use, GetComputedValue(use, "x"), width) ?? 0;
        var y = ResolveLength(use, GetComputedValue(use, "y"), height) ?? 0;
        return SvgAffine.Translation(x, y);
    }

    /// <summary>
    /// The transform from the user space of the children of an outermost svg element to the coordinates of its content box in
    /// CSS px: the transform of its viewBox in its viewport, then its zoom, then <c>currentScale</c> and <c>currentTranslate</c>
    /// of the root element of an SVG document (https://svgwg.org/svg2-draft/struct.html#__svg__SVGSVGElement__currentScale).
    /// </summary>
    public static SvgAffine GetOutermostContentTransform(SvgSvgElement outermost, SvgOutermostViewport? viewport = null)
    {
        var (width, height) = GetSvgSize(outermost, viewport);
        var zoom = (viewport ?? GetOutermostViewport(outermost))?.Zoom ?? 1;
        var transform = GetViewBoxTransform(outermost, width, height).Then(SvgAffine.Scale(zoom, zoom));

        // The transform of the view of the document (https://svgwg.org/svg2-draft/linking.html#SVGFragmentIdentifiers), in the
        // user space of the content
        if (outermost.ParentNode is Document document && SvgViewSpec.Get(document)?.Transform is { } viewTransform)
        {
            transform = viewTransform.Then(transform);
        }
        // The magnification and the panning of the document only apply to the root element of an SVG document (as in Gecko and
        // WebKit: they have no effect on an svg element embedded in an HTML document)
        var scale = outermost.CurrentScale;
        var translate = outermost.CurrentTranslate;
        if (outermost.ParentNode is Document && (scale != 1 || translate.X != 0 || translate.Y != 0))
        {
            transform = transform.Then(new SvgAffine(scale, 0, 0, scale, translate.X, translate.Y));
        }

        return transform;
    }

    /// <summary>
    /// Returns the transform from the user space of an element (after its own transform) to the coordinates of the content box
    /// of its outermost svg element in CSS px (the transforms of the element and of its ancestors, the viewports of the nested
    /// svg elements, the viewBox and the zoom of the outermost svg element), or null when the element is not in an svg element.
    /// </summary>
    public static SvgAffine? GetTransformToOutermostContentBox(Element element, out SvgSvgElement? outermost, SvgOutermostViewport? viewport = null)
    {
        outermost = GetOutermostSvgElement(element);
        if (outermost is null || ReferenceEquals(element, outermost))
            return null;

        var transform = GetTransformToParent(element, viewport);
        for (var ancestor = element.ParentNode as Element; ancestor is not null && !ReferenceEquals(ancestor, outermost); ancestor = ancestor.ParentNode as Element)
        {
            transform = transform.Then(GetTransformToParent(ancestor, viewport));
        }

        return transform.Then(GetOutermostContentTransform(outermost, viewport));
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGraphicsElement__getCTM: the transform from the user space of an element
    /// to the coordinate system of its nearest viewport element, as in the browsers: the transforms of the element and of its
    /// ancestors up to its nearest svg ancestor, whose viewBox (and x and y for a nested svg element) is included. For an
    /// outermost svg element, the transform of its viewBox (and of its zoom, <c>currentScale</c> and <c>currentTranslate</c>).
    /// </summary>
    public static SvgAffine GetCtm(SvgElement element)
    {
        if (element is SvgSvgElement svg && IsOutermost(svg))
            return GetOutermostContentTransform(svg);

        var transform = SvgAffine.Identity;
        for (Element? current = element; current is SvgElement; current = current.ParentNode as Element)
        {
            if (current is SvgSvgElement currentSvg && IsOutermost(currentSvg))
                return transform.Then(GetOutermostContentTransform(currentSvg));

            transform = transform.Then(GetTransformToParent(current));
            if (!ReferenceEquals(current, element) && current is SvgSvgElement)
                break;
        }

        return transform;
    }

    /// <summary>
    /// https://svgwg.org/svg2-draft/types.html#__svg__SVGGraphicsElement__getScreenCTM: the transform from the user space of an
    /// element to the coordinates of the viewport of the browsing context (the CSS layout of the outermost svg element: its
    /// content box, its CSS transforms and the ones of its ancestors, the scroll positions), or null when the element is not
    /// rendered.
    /// </summary>
    public static SvgAffine? GetScreenCtm(SvgElement element)
    {
        var outermost = GetOutermostSvgElement(element);
        if (outermost is null || CssomViewGeometry.GetLayoutEngine(outermost) is not { } engine || engine.GetFragments(outermost) is not [var fragment, ..])
            return null;

        var viewport = GetOutermostViewport(fragment);
        var transform = ReferenceEquals(element, outermost) ? GetOutermostContentTransform(outermost, viewport) : GetTransformToOutermostContentBox(element, out _, viewport) ?? SvgAffine.Identity;
        return transform.Then(GetContentBoxToViewportTransform(engine, fragment));
    }

    /// <summary>The transform from the coordinates of the content box of a fragment to the coordinates of the viewport (its CSS transforms and the ones of its ancestors, the scroll positions).</summary>
    public static SvgAffine GetContentBoxToViewportTransform(LayoutEngine engine, BoxFragment fragment)
    {
        var content = fragment.ContentBoxRect.Offset;
        var origin = engine.ToViewport(LayoutEngine.MapToRoot(fragment, content));
        var unitX = engine.ToViewport(LayoutEngine.MapToRoot(fragment, new PhysicalOffset(content.X + 1, content.Y)));
        var unitY = engine.ToViewport(LayoutEngine.MapToRoot(fragment, new PhysicalOffset(content.X, content.Y + 1)));
        return new SvgAffine(unitX.X - origin.X, unitX.Y - origin.Y, unitY.X - origin.X, unitY.Y - origin.Y, origin.X, origin.Y);
    }
}
