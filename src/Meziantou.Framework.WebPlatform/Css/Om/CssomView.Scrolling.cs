using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;

// The parameters of the window operations are defined by the IDL, but the window does not move
#pragma warning disable IDE0060 // Remove unused parameter

namespace Meziantou.Framework.WebPlatform.Dom;

// The members of CSSOM View (https://drafts.csswg.org/cssom-view/) that are defined from the computed styles, the viewport,
// the boxes of the elements and the scrolling boxes. The scrolling members (scroll*, client*) and the offset* members read the
// layout (see LayoutEngine.Scrolling.cs and LayoutEngine.Geometry.cs; getClientRects() and the hit testing are in CssomView.cs).
// The members that only depend on the computed styles (offsetParent, scrollingElement, checkVisibility(), currentCSSZoom...)
// follow the specification.

/// <summary>https://drafts.csswg.org/cssom-view/#enumdef-scrollbehavior</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#enumdef-scrollbehavior"/>
[IdlInterface("ScrollBehavior", "https://drafts.csswg.org/cssom-view/#enumdef-scrollbehavior")]
public enum ScrollBehavior
{
    /// <summary>The scroll behavior is determined by the <c>scroll-behavior</c> CSS property.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollbehavior-auto"/>
    [IdlMember("auto", "https://drafts.csswg.org/cssom-view/#dom-scrollbehavior-auto")]
    Auto,

    /// <summary>The scroll happens in a single jump.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollbehavior-instant"/>
    [IdlMember("instant", "https://drafts.csswg.org/cssom-view/#dom-scrollbehavior-instant")]
    Instant,

    /// <summary>The scroll is animated smoothly.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollbehavior-smooth"/>
    [IdlMember("smooth", "https://drafts.csswg.org/cssom-view/#dom-scrollbehavior-smooth")]
    Smooth,
}

/// <summary>https://drafts.csswg.org/cssom-view/#enumdef-scrolllogicalposition</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#enumdef-scrolllogicalposition"/>
[IdlInterface("ScrollLogicalPosition", "https://drafts.csswg.org/cssom-view/#enumdef-scrolllogicalposition")]
public enum ScrollLogicalPosition
{
    /// <summary>Aligns the start edge of the element with the start edge of the scrolling box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-start"/>
    [IdlMember("start", "https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-start")]
    Start,

    /// <summary>Aligns the center of the element with the center of the scrolling box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-center"/>
    [IdlMember("center", "https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-center")]
    Center,

    /// <summary>Aligns the end edge of the element with the end edge of the scrolling box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-end"/>
    [IdlMember("end", "https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-end")]
    End,

    /// <summary>
    /// Scrolls the minimum amount needed to make the element visible, aligning it with the nearest edge.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-nearest"/>
    [IdlMember("nearest", "https://drafts.csswg.org/cssom-view/#dom-scrolllogicalposition-nearest")]
    Nearest,
}

/// <summary>https://drafts.csswg.org/cssom-view/#enumdef-scrollintoviewcontainer</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#enumdef-scrollintoviewcontainer"/>
[IdlInterface("ScrollIntoViewContainer", "https://drafts.csswg.org/cssom-view/#enumdef-scrollintoviewcontainer")]
public enum ScrollIntoViewContainer
{
    /// <summary>Scrolls all the scrolling boxes that are ancestors of the element.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollintoviewcontainer-all"/>
    [IdlMember("all", "https://drafts.csswg.org/cssom-view/#dom-scrollintoviewcontainer-all")]
    All,

    /// <summary>Scrolls only the nearest scrolling box that is an ancestor of the element.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollintoviewcontainer-nearest"/>
    [IdlMember("nearest", "https://drafts.csswg.org/cssom-view/#dom-scrollintoviewcontainer-nearest")]
    Nearest,
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-scrolloptions</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#dictdef-scrolloptions"/>
[IdlInterface("ScrollOptions", "https://drafts.csswg.org/cssom-view/#dictdef-scrolloptions")]
public class ScrollOptions
{
    /// <summary>Gets the scroll behavior of the scroll operation.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolloptions-behavior"/>
    [IdlMember("behavior", "https://drafts.csswg.org/cssom-view/#dom-scrolloptions-behavior")]
    public ScrollBehavior Behavior { get; init; }
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-scrolltooptions</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#dictdef-scrolltooptions"/>
[IdlInterface("ScrollToOptions", "https://drafts.csswg.org/cssom-view/#dictdef-scrolltooptions")]
public sealed class ScrollToOptions : ScrollOptions
{
    /// <summary>Gets the horizontal scroll position to scroll to, or null to keep the current position.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolltooptions-left"/>
    [IdlMember("left", "https://drafts.csswg.org/cssom-view/#dom-scrolltooptions-left")]
    public double? Left { get; init; }

    /// <summary>Gets the vertical scroll position to scroll to, or null to keep the current position.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrolltooptions-top"/>
    [IdlMember("top", "https://drafts.csswg.org/cssom-view/#dom-scrolltooptions-top")]
    public double? Top { get; init; }
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-scrollintoviewoptions</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#dictdef-scrollintoviewoptions"/>
[IdlInterface("ScrollIntoViewOptions", "https://drafts.csswg.org/cssom-view/#dictdef-scrollintoviewoptions")]
public sealed class ScrollIntoViewOptions : ScrollOptions
{
    /// <summary>Gets the alignment of the element in the block axis of the scrolling box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollintoviewoptions-block"/>
    [IdlMember("block", "https://drafts.csswg.org/cssom-view/#dom-scrollintoviewoptions-block")]
    public ScrollLogicalPosition Block { get; init; } = ScrollLogicalPosition.Start;

    /// <summary>Gets the alignment of the element in the inline axis of the scrolling box.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollintoviewoptions-inline"/>
    [IdlMember("inline", "https://drafts.csswg.org/cssom-view/#dom-scrollintoviewoptions-inline")]
    public ScrollLogicalPosition Inline { get; init; } = ScrollLogicalPosition.Nearest;

    /// <summary>Gets which scrolling boxes are scrolled to reveal the element.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-scrollintoviewoptions-container"/>
    [IdlMember("container", "https://drafts.csswg.org/cssom-view/#dom-scrollintoviewoptions-container")]
    public ScrollIntoViewContainer Container { get; init; } = ScrollIntoViewContainer.All;
}

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-checkvisibilityoptions</summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#dictdef-checkvisibilityoptions"/>
[IdlInterface("CheckVisibilityOptions", "https://drafts.csswg.org/cssom-view/#dictdef-checkvisibilityoptions")]
public sealed class CheckVisibilityOptions
{
    /// <summary>Gets a value indicating whether an element with an opacity of 0 is considered not visible.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-checkopacity"/>
    [IdlMember("checkOpacity", "https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-checkopacity")]
    public bool CheckOpacity { get; init; }

    /// <summary>
    /// Gets a value indicating whether an element with a <c>visibility</c> of <c>hidden</c> or <c>collapse</c> is
    /// considered not visible.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-checkvisibilitycss"/>
    [IdlMember("checkVisibilityCSS", "https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-checkvisibilitycss")]
    public bool CheckVisibilityCss { get; init; }

    /// <summary>
    /// Gets a value indicating whether an element skipped by <c>content-visibility: auto</c> is considered not visible.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-contentvisibilityauto"/>
    [IdlMember("contentVisibilityAuto", "https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-contentvisibilityauto")]
    public bool ContentVisibilityAuto { get; init; }

    /// <summary>
    /// Gets a value indicating whether an element with an opacity of 0 is considered not visible (alias of
    /// <c>checkOpacity</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-opacityproperty"/>
    [IdlMember("opacityProperty", "https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-opacityproperty")]
    public bool OpacityProperty { get; init; }

    /// <summary>
    /// Gets a value indicating whether an element with a <c>visibility</c> of <c>hidden</c> or <c>collapse</c> is
    /// considered not visible (alias of <c>checkVisibilityCSS</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-visibilityproperty"/>
    [IdlMember("visibilityProperty", "https://drafts.csswg.org/cssom-view/#dom-checkvisibilityoptions-visibilityproperty")]
    public bool VisibilityProperty { get; init; }
}

/// <summary>Helpers of the CSSOM View members that use the computed styles.</summary>
internal static class CssomViewHelpers
{
    /// <summary>Returns the computed style of an element.</summary>
    public static ComputedStyle GetStyle(Element element) => element.NodeDocument.GetStyleEngine().GetStyle(element);

    /// <summary>The parent element of an element in the flat tree, or null.</summary>
    public static Element? GetFlatTreeParentElement(Element element) => HtmlElement.GetFlatTreeParent(element) as Element;

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#the-body-element-2: the first body child of the html document element.</summary>
    public static HtmlBodyElement? GetTheBodyElement(Document document)
    {
        if (document.DocumentElement is not HtmlHtmlElement root)
            return null;

        foreach (var child in root.ChildNodes)
        {
            if (child is HtmlBodyElement body)
                return body;
        }

        return null;
    }

    /// <summary>https://drafts.csswg.org/cssom-view/#potentially-scrollable (in either axis when <paramref name="axis"/> is null).</summary>
    public static bool IsPotentiallyScrollable(Element body, char? axis, bool parentClipIsHidden = false)
    {
        if (!ComputedStyleDeclarations.IsBeingRendered(body) || body.ParentElement is not { } parent)
            return false;

        var parentStyle = GetStyle(parent);
        var style = GetStyle(body);
        return axis switch
        {
            'x' => IsScrollable(parentStyle.GetComputedValue("overflow-x"), parentClipIsHidden) && IsScrollable(style.GetComputedValue("overflow-x"), clipIsHidden: false),
            'y' => IsScrollable(parentStyle.GetComputedValue("overflow-y"), parentClipIsHidden) && IsScrollable(style.GetComputedValue("overflow-y"), clipIsHidden: false),
            _ => IsPotentiallyScrollable(body, 'x', parentClipIsHidden) || IsPotentiallyScrollable(body, 'y', parentClipIsHidden),
        };

        static bool IsScrollable(string overflow, bool clipIsHidden) => overflow != "visible" && (clipIsHidden || overflow != "clip");
    }

    /// <summary>The element is the root element and its document is not in quirks mode, or it is the body element of a document in quirks mode: its client area is the viewport.</summary>
    public static bool IsViewportElement(Element element)
    {
        var document = element.NodeDocument;
        var quirks = document.Mode == DocumentMode.Quirks;
        return quirks ? ReferenceEquals(element, GetTheBodyElement(document)) : ReferenceEquals(element, document.DocumentElement);
    }

    /// <summary>
    /// Returns the size of the content box of a replaced element that is being rendered (an img, iframe, embed, video or
    /// canvas element), or null. There is no layout: the size is the computed width and height when they are lengths, else
    /// the size of the default sizing algorithm (https://drafts.csswg.org/css-images/#default-sizing): the natural size of
    /// the image or of the canvas (the default object size, 300x150, for the other elements), keeping its aspect ratio
    /// when only one of them is a length.
    /// </summary>
    public static (double Width, double Height)? GetReplacedContentSize(Element element)
    {
        ImageNaturalSize natural;
        switch (element)
        {
            case HtmlImageElement image:
                // A broken image has no natural size (its alternative text is rendered instead)
                if (image.GetDensityCorrectedNaturalSize() is not { } imageSize)
                    return null;

                natural = new ImageNaturalSize(imageSize.Width, imageSize.Height, imageSize.Height > 0 ? imageSize.Width / imageSize.Height : null);
                break;

            case HtmlCanvasElement canvas:
                natural = ImageNaturalSize.FromPixels(canvas.Width, canvas.Height);
                break;

            case HtmlIFrameElement or HtmlEmbedElement or HtmlVideoElement:
                natural = default;
                break;

            default:
                return null;
        }

        if (!ComputedStyleDeclarations.IsBeingRendered(element))
            return null;

        var style = GetStyle(element);
        var concrete = natural.GetConcreteSize();
        return (GetContentLength(style, horizontal: true), GetContentLength(style, horizontal: false)) switch
        {
            ({ } width, { } height) => (width, height),
            ({ } width, null) => (width, natural.AspectRatio is { } ratio ? width / ratio : concrete.Height),
            (null, { } height) => (natural.AspectRatio is { } ratio ? height * ratio : concrete.Width, height),
            _ => concrete,
        };

        // The width or height of the content box when it is a length (box-sizing: border-box includes the padding and the border)
        static double? GetContentLength(ComputedStyle style, bool horizontal)
        {
            if (CssValueComputer.ParsePixels(style.GetComputedValue(horizontal ? "width" : "height")) is not { } length)
                return null;

            if (style.GetComputedValue("box-sizing") == "border-box")
            {
                length -= horizontal
                    ? GetPixels(style, "padding-left") + GetPixels(style, "padding-right") + style.GetUsedBorderWidth("border-left-width") + style.GetUsedBorderWidth("border-right-width")
                    : GetPixels(style, "padding-top") + GetPixels(style, "padding-bottom") + style.GetUsedBorderWidth("border-top-width") + style.GetUsedBorderWidth("border-bottom-width");
            }

            return Math.Max(length, 0);
        }
    }

    /// <summary>Returns the computed value of a property in pixels, or 0 when it is not a length (e.g. a percentage).</summary>
    public static double GetPixels(ComputedStyle style, string property) => CssValueComputer.ParsePixels(style.GetComputedValue(property)) ?? 0;

    /// <summary>The element is being rendered and its box is not an inline box (display: inline).</summary>
    public static bool HasNonInlineBox(Element element) => ComputedStyleDeclarations.IsBeingRendered(element) && GetStyle(element).GetComputedValue("display") != "inline";

    /// <summary>https://drafts.csswg.org/css-viewport/#effective-zoom: the product of the zoom of the element and of its flat tree ancestors.</summary>
    public static double GetEffectiveZoom(Element element) => GetStyle(element).Layout.EffectiveZoom;

    /// <summary>
    /// Whether the computed style of an element makes it a containing block for its fixed-positioned descendants
    /// (https://drafts.csswg.org/css-position/#fixed-cb): transforms, perspective, preserve-3d, filters, layout or paint
    /// containment, and the will-change values that do the same.
    /// </summary>
    public static bool EstablishesFixedContainingBlock(ComputedStyle style)
        => style.Layout.EstablishesFixedPositionContainingBlock || (style.PseudoElement is null && style.Element is SvgForeignObjectElement && BoxGeneration.Get(style).IsBeingRendered);

    /// <summary>Whether an ancestor of the element in the flat tree establishes a containing block for fixed-positioned descendants.</summary>
    public static bool HasFixedContainingBlockAncestor(Element element)
    {
        for (var ancestor = GetFlatTreeParentElement(element); ancestor is not null; ancestor = GetFlatTreeParentElement(ancestor))
        {
            if (EstablishesFixedContainingBlock(GetStyle(ancestor)))
                return true;
        }

        return false;
    }

    /// <summary>Whether a node is in a shadow tree that is not a shadow-including inclusive ancestor of another node (open or closed).</summary>
    public static bool IsShadowHiddenFrom(Node a, Node b) => a.GetRootNode() is ShadowRoot root && !root.IsShadowIncludingInclusiveAncestorOf(b);

    /// <summary>https://dom.spec.whatwg.org/#concept-closed-shadow-hidden</summary>
    public static bool IsClosedShadowHiddenFrom(Node a, Node b)
    {
        if (a.GetRootNode() is not ShadowRoot root)
            return false;

        for (Node? current = b; current is not null; current = current is ShadowRoot shadowRoot ? shadowRoot.Host : current.ParentNode)
        {
            if (ReferenceEquals(current, root))
                return false;
        }

        return root.Mode == ShadowRootMode.Closed || IsClosedShadowHiddenFrom(root.Host, b);
    }
}

public partial class Element
{
    /// <summary>
    /// Returns whether the element is visible (https://drafts.csswg.org/cssom-view/#dom-element-checkvisibility): it is being
    /// rendered, no flat tree ancestor has <c>content-visibility: hidden</c> and, depending on the options, it is not
    /// transparent (<c>opacity: 0</c>) or invisible (<c>visibility</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-checkvisibility"/>
    [IdlMember("checkVisibility", "https://drafts.csswg.org/cssom-view/#dom-element-checkvisibility")]
    public bool CheckVisibility(CheckVisibilityOptions? options = null)
    {
        if (!ComputedStyleDeclarations.IsBeingRendered(this))
            return false;

        for (var ancestor = CssomViewHelpers.GetFlatTreeParentElement(this); ancestor is not null; ancestor = CssomViewHelpers.GetFlatTreeParentElement(ancestor))
        {
            var contentVisibility = CssomViewHelpers.GetStyle(ancestor).GetComputedValue("content-visibility");
            if (contentVisibility == "hidden")
                return false;

            // "If an ancestor of this in the flat tree has content-visibility: auto and is skipping its contents": the element
            // with content-visibility: auto is not relevant to the user (https://drafts.csswg.org/css-contain-2/#relevant-to-the-user)
            if (contentVisibility == "auto" && options is { ContentVisibilityAuto: true } && ContentRelevancy.SkipsContents(ancestor, CssomViewHelpers.GetStyle(ancestor)))
                return false;
        }

        if (options is { OpacityProperty: true } or { CheckOpacity: true })
        {
            for (Element? current = this; current is not null; current = CssomViewHelpers.GetFlatTreeParentElement(current))
            {
                if (double.TryParse(CssomViewHelpers.GetStyle(current).GetComputedValue("opacity"), NumberStyles.Float, CultureInfo.InvariantCulture, out var opacity) && opacity == 0)
                    return false;
            }
        }

        if (options is { VisibilityProperty: true } or { CheckVisibilityCss: true } && CssomViewHelpers.GetStyle(this).GetComputedValue("visibility") != "visible")
            return false;

        return true;
    }

    /// <summary>Scrolls the element into view (https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview), aligning its top with the top of its scrolling boxes.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview"/>
    [IdlMember("scrollIntoView", "https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview")]
    public Task ScrollIntoView() => ScrollIntoView(alignToTop: true);

    /// <summary>
    /// Scrolls the element into view (https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview): its block-start edge is
    /// aligned with the block-start edge of its scrolling boxes when <paramref name="alignToTop"/> is true, its block-end edge
    /// with their block-end edge otherwise.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview"/>
    [IdlMember("scrollIntoView", "https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview")]
    public Task ScrollIntoView(bool alignToTop) => ScrollIntoViewCore(ScrollBehavior.Auto, alignToTop ? ScrollLogicalPosition.Start : ScrollLogicalPosition.End, ScrollLogicalPosition.Nearest, container: null);

    /// <summary>
    /// Scrolls the element into view (https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview): its scrolling boxes, from
    /// the innermost to the outermost (or only the nearest one with <c>container: "nearest"</c>), are scrolled to align it as
    /// given by the options, with its <c>scroll-margin</c> and their <c>scroll-padding</c>. The task completes when the scrolls
    /// are completed.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview"/>
    [IdlMember("scrollIntoView", "https://drafts.csswg.org/cssom-view/#dom-element-scrollintoview")]
    public Task ScrollIntoView(ScrollIntoViewOptions? options)
    {
        options ??= new ScrollIntoViewOptions();
        return ScrollIntoViewCore(options.Behavior, options.Block, options.Inline, options.Container == ScrollIntoViewContainer.Nearest ? this : null);
    }

    private Task ScrollIntoViewCore(ScrollBehavior behavior, ScrollLogicalPosition block, ScrollLogicalPosition inline, Element? container)
    {
        // "If the element does not have any associated box, or is not available to user-agent features, then return a resolved Promise"
        var document = NodeDocument;
        if (document.BrowsingContext is null || !ComputedStyleDeclarations.IsBeingRendered(this))
            return Task.CompletedTask;

        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is null)
            return Task.CompletedTask;

        return Css.Layout.ScrollIntoView.Run(engine, this, behavior, block, inline, container, snapsToTarget: true);
    }

    /// <summary>
    /// Scrolls the element (https://drafts.csswg.org/cssom-view/#dom-element-scroll) to the scroll position of
    /// <see cref="ScrollToOptions.Left"/> and <see cref="ScrollToOptions.Top"/> (the current ones when they are absent): the
    /// viewport for the root element (and the body element in quirks mode), else its scrolling box. The task completes when the
    /// scroll is completed.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scroll"/>
    [IdlMember("scroll", "https://drafts.csswg.org/cssom-view/#dom-element-scroll")]
    public Task Scroll(ScrollToOptions? options = null)
    {
        options ??= new ScrollToOptions();
        double? x = options.Left is { } left ? CssomViewScrolling.NormalizeNonFinite(left) : null;
        double? y = options.Top is { } top ? CssomViewScrolling.NormalizeNonFinite(top) : null;
        return ScrollCore(x, y, options.Behavior);
    }

    /// <summary>Scrolls the element to a scroll position (https://drafts.csswg.org/cssom-view/#dom-element-scroll): see <see cref="Scroll(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scroll"/>
    [IdlMember("scroll", "https://drafts.csswg.org/cssom-view/#dom-element-scroll")]
    public Task Scroll(double x, double y) => ScrollCore(CssomViewScrolling.NormalizeNonFinite(x), CssomViewScrolling.NormalizeNonFinite(y), ScrollBehavior.Auto);

    /// <summary>Scrolls the element to a position (null keeps the current position of the axis), or by an offset when <paramref name="relative"/> is true (a relative scroll, for scroll snapping).</summary>
    private Task ScrollCore(double? x, double? y, ScrollBehavior behavior, bool relative = false)
    {
        var document = NodeDocument;
        if (CssomViewScrolling.GetActiveWindow(document) is not { } window)
            return Task.CompletedTask;

        if (ReferenceEquals(this, document.DocumentElement))
            return document.Mode == DocumentMode.Quirks ? Task.CompletedTask : window.ScrollCore(x, y, behavior, relative);

        if (CssomViewScrolling.ScrollsTheViewport(this))
            return window.ScrollCore(x, y, behavior, relative);

        // "If the element does not have any associated box, the element has no associated scrolling box, or the element has no overflow, return a resolved Promise"
        if (CssomViewScrolling.GetScrollState(this) is not { HasOverflow: true } state)
            return Task.CompletedTask;

        var zoom = CssomViewHelpers.GetEffectiveZoom(this);
        var engine = document.GetLayoutEngine();
        if (relative)
            return CssomViewScrolling.ScrollElementBy(engine, state, new PhysicalOffset((x ?? 0) * zoom, (y ?? 0) * zoom), behavior, this);

        return CssomViewScrolling.ScrollElement(engine, state, x is { } left ? left * zoom : null, y is { } top ? top * zoom : null, behavior, this);
    }

    /// <summary>Scrolls the element (https://drafts.csswg.org/cssom-view/#dom-element-scrollto): see <see cref="Scroll(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollto"/>
    [IdlMember("scrollTo", "https://drafts.csswg.org/cssom-view/#dom-element-scrollto")]
    public Task ScrollTo(ScrollToOptions? options = null) => Scroll(options);

    /// <summary>Scrolls the element (https://drafts.csswg.org/cssom-view/#dom-element-scrollto): see <see cref="Scroll(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollto"/>
    [IdlMember("scrollTo", "https://drafts.csswg.org/cssom-view/#dom-element-scrollto")]
    public Task ScrollTo(double x, double y) => Scroll(x, y);

    /// <summary>
    /// Scrolls the element by an offset (https://drafts.csswg.org/cssom-view/#dom-element-scrollby): the offsets are added to
    /// <see cref="ScrollLeft"/> and <see cref="ScrollTop"/>. A scroll snap container snaps in the direction of the scroll (a
    /// relative scroll, https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollby"/>
    [IdlMember("scrollBy", "https://drafts.csswg.org/cssom-view/#dom-element-scrollby")]
    public Task ScrollBy(ScrollToOptions? options = null)
    {
        options ??= new ScrollToOptions();
        var left = CssomViewScrolling.NormalizeNonFinite(options.Left ?? 0);
        var top = CssomViewScrolling.NormalizeNonFinite(options.Top ?? 0);
        return ScrollCore(left, top, options.Behavior, relative: true);
    }

    /// <summary>Scrolls the element by an offset (https://drafts.csswg.org/cssom-view/#dom-element-scrollby): see <see cref="ScrollBy(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollby"/>
    [IdlMember("scrollBy", "https://drafts.csswg.org/cssom-view/#dom-element-scrollby")]
    public Task ScrollBy(double x, double y) => ScrollBy(new ScrollToOptions { Left = x, Top = y });

    /// <summary>
    /// Gets or sets the vertical scroll position (https://drafts.csswg.org/cssom-view/#dom-element-scrolltop): the scroll
    /// position of the viewport for the root element (and the body element in quirks mode), the one of the scrolling box of the
    /// element, else 0. It is negative when the content overflows upwards (e.g. <c>flex-direction: column-reverse</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrolltop"/>
    [IdlMember("scrollTop", "https://drafts.csswg.org/cssom-view/#dom-element-scrolltop")]
    public double ScrollTop
    {
        get => GetScrollPosition(horizontal: false);
        set => SetScrollPosition(horizontal: false, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll position (https://drafts.csswg.org/cssom-view/#dom-element-scrollleft): see
    /// <see cref="ScrollTop"/>. It is negative when the content overflows leftwards (<c>direction: rtl</c>, <c>vertical-rl</c>).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollleft"/>
    [IdlMember("scrollLeft", "https://drafts.csswg.org/cssom-view/#dom-element-scrollleft")]
    public double ScrollLeft
    {
        get => GetScrollPosition(horizontal: true);
        set => SetScrollPosition(horizontal: true, value);
    }

    private double GetScrollPosition(bool horizontal)
    {
        var document = NodeDocument;
        if (CssomViewScrolling.GetActiveWindow(document) is not { } window)
            return 0;

        if (ReferenceEquals(this, document.DocumentElement))
            return document.Mode == DocumentMode.Quirks ? 0 : horizontal ? window.ScrollX : window.ScrollY;

        if (CssomViewScrolling.ScrollsTheViewport(this))
            return horizontal ? window.ScrollX : window.ScrollY;

        if (CssomViewScrolling.GetScrollState(this) is not { } state)
            return 0;

        return (horizontal ? state.Offset.X : state.Offset.Y) / CssomViewHelpers.GetEffectiveZoom(this);
    }

    private void SetScrollPosition(bool horizontal, double value)
    {
        value = CssomViewScrolling.NormalizeNonFinite(value);
        var document = NodeDocument;
        if (CssomViewScrolling.GetActiveWindow(document) is not { } window)
            return;

        if (ReferenceEquals(this, document.DocumentElement) && document.Mode == DocumentMode.Quirks)
            return;

        if (ReferenceEquals(this, document.DocumentElement) || CssomViewScrolling.ScrollsTheViewport(this))
        {
            _ = horizontal ? window.ScrollCore(value, null, ScrollBehavior.Auto) : window.ScrollCore(null, value, ScrollBehavior.Auto);
            return;
        }

        if (CssomViewScrolling.GetScrollState(this) is not { HasOverflow: true } state)
            return;

        value *= CssomViewHelpers.GetEffectiveZoom(this);
        _ = CssomViewScrolling.ScrollElement(document.GetLayoutEngine(), state, horizontal ? value : null, horizontal ? null : value, ScrollBehavior.Auto, this);
    }

    /// <summary>
    /// Gets the width of the scrolling area (https://drafts.csswg.org/cssom-view/#dom-element-scrollwidth): the scrolling area of
    /// the viewport for the root element (the body element in quirks mode), at least the width of the viewport without its
    /// scrollbar; else the width of the scrolling area of the box (its padding box and its scrollable overflow).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollwidth"/>
    [IdlMember("scrollWidth", "https://drafts.csswg.org/cssom-view/#dom-element-scrollwidth")]
    public int ScrollWidth => GetScrollingAreaSize(horizontal: true);

    /// <summary>Gets the height of the scrolling area (https://drafts.csswg.org/cssom-view/#dom-element-scrollheight): see <see cref="ScrollWidth"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-scrollheight"/>
    [IdlMember("scrollHeight", "https://drafts.csswg.org/cssom-view/#dom-element-scrollheight")]
    public int ScrollHeight => GetScrollingAreaSize(horizontal: false);

    /// <summary>
    /// Gets the width of the top border and of a scrollbar between the top border and the top padding edge
    /// (https://drafts.csswg.org/cssom-view/#dom-element-clienttop), or 0 for an element without box or with an inline box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-clienttop"/>
    [IdlMember("clientTop", "https://drafts.csswg.org/cssom-view/#dom-element-clienttop")]
    public int ClientTop => GetClientEdge(horizontal: false);

    /// <summary>
    /// Gets the width of the left border and of a scrollbar between the left border and the left padding edge (a vertical
    /// scrollbar of a right-to-left box, https://drafts.csswg.org/cssom-view/#dom-element-clientleft), or 0 for an element
    /// without box or with an inline box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-clientleft"/>
    [IdlMember("clientLeft", "https://drafts.csswg.org/cssom-view/#dom-element-clientleft")]
    public int ClientLeft => GetClientEdge(horizontal: true);

    /// <summary>
    /// Gets the width of the padding box without the scrollbar (https://drafts.csswg.org/cssom-view/#dom-element-clientwidth):
    /// the width of the viewport without its scrollbar for the root element (the body element in quirks mode), 0 for an element
    /// without box or with an inline box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-clientwidth"/>
    [IdlMember("clientWidth", "https://drafts.csswg.org/cssom-view/#dom-element-clientwidth")]
    public int ClientWidth => GetClientSize(horizontal: true);

    /// <summary>Gets the height of the padding box without the scrollbar (https://drafts.csswg.org/cssom-view/#dom-element-clientheight): see <see cref="ClientWidth"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-clientheight"/>
    [IdlMember("clientHeight", "https://drafts.csswg.org/cssom-view/#dom-element-clientheight")]
    public int ClientHeight => GetClientSize(horizontal: false);

    /// <summary>Gets the effective zoom of the element (https://drafts.csswg.org/cssom-view/#dom-element-currentcsszoom), or 1 when it is not being rendered.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-element-currentcsszoom"/>
    [IdlMember("currentCSSZoom", "https://drafts.csswg.org/cssom-view/#dom-element-currentcsszoom")]
    public double CurrentCssZoom => ComputedStyleDeclarations.IsBeingRendered(this) ? CssomViewHelpers.GetEffectiveZoom(this) : 1;

    private int GetClientEdge(bool horizontal)
    {
        if (CssomViewScrolling.GetBlockFragment(this) is not { } fragment)
            return 0;

        var edge = horizontal ? fragment.Border.Left + fragment.Scrollbars.Left : fragment.Border.Top + fragment.Scrollbars.Top;

        // The scrollport of a text field is its inner editor, in its content box: the padding of its inline axis is outside
        if (CssomViewScrolling.IsInlineAxisOfTextField(fragment, horizontal))
        {
            edge += horizontal ? fragment.Padding.Left : fragment.Padding.Top;
        }

        return (int)Math.Round(edge / fragment.Box.Style.Layout.EffectiveZoom, MidpointRounding.AwayFromZero);
    }

    private int GetClientSize(bool horizontal)
    {
        if (CssomViewScrolling.GetBlockFragment(this) is not { } fragment)
            return 0;

        if (CssomViewHelpers.IsViewportElement(this) && NodeDocument.GetLayoutEngine().ViewportScrollState.Fragment is { } root)
            return (int)(horizontal ? root.Size.Width : root.Size.Height);

        var padding = CssomViewScrolling.IsInlineAxisOfTextField(fragment, horizontal) ? fragment.ContentBoxRect : fragment.PaddingBoxRect;
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        return CssomViewScrolling.RoundSize((horizontal ? padding.Width : padding.Height) / zoom);
    }

    private int GetScrollingAreaSize(bool horizontal)
    {
        var document = NodeDocument;
        if (CssomViewScrolling.GetActiveWindow(document) is null)
            return 0;

        var isViewportElement = document.Mode == DocumentMode.Quirks
            ? ReferenceEquals(this, CssomViewHelpers.GetTheBodyElement(document)) && !CssomViewHelpers.IsPotentiallyScrollable(this, horizontal ? 'x' : 'y')
            : ReferenceEquals(this, document.DocumentElement);
        if (isViewportElement)
        {
            // "max(viewport scrolling area width, viewport width)"
            var engine = document.GetLayoutEngine();
            if (engine.EnsureLayout() is null)
                return 0;

            var viewport = engine.ViewportScrollState;
            var area = viewport.ScrollingArea;
            var port = viewport.Scrollport;
            return (int)Math.Round(horizontal ? Math.Max(area.Width, port.Width) : Math.Max(area.Height, port.Height), MidpointRounding.AwayFromZero);
        }

        // The scrolling area of an element that skips its contents (content-visibility) depends on its contents
        if (ComputedStyleDeclarations.IsBeingRendered(this))
        {
            document.GetLayoutEngine().EnsureContentsLaidOut(this);
        }

        if (CssomViewScrolling.GetBlockFragment(this) is not { } fragment)
            return 0;

        // The scrolling box of a fieldset is its anonymous fieldset content box (the legend is not in it)
        if (fragment.ScrollState is null && Css.Layout.BlockFormattingContext.IsFieldset(fragment.Box) && document.GetLayoutEngine().GetScrollState(this) is { Fragment: { } scrollingBox })
        {
            fragment = scrollingBox;
        }

        // The scrolling box of a text field is its inner editor, which scrolls its line in the inline axis of the content box
        // (as in the browsers, the field does not scroll in its block axis: its scrolling area is its padding box)
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        if (Css.Layout.TextControlLayout.GetInnerEditorFragment(fragment) is { IsScrollContainer: true } editor)
        {
            var inlineAxis = fragment.WritingDirection.IsHorizontal == horizontal;
            var area = inlineAxis ? editor.ScrollState?.ScrollingArea ?? editor.GetScrollingArea() : fragment.PaddingBoxRect;
            return CssomViewScrolling.RoundSize((horizontal ? area.Width : area.Height) / zoom);
        }

        var scrollingArea = fragment.ScrollState?.ScrollingArea ?? fragment.GetScrollingArea();
        return CssomViewScrolling.RoundSize((horizontal ? scrollingArea.Width : scrollingArea.Height) / zoom);
    }
}

/// <summary>Helpers of the scrolling members of CSSOM View (https://drafts.csswg.org/cssom-view/#scrolling).</summary>
internal static class CssomViewScrolling
{
    /// <summary>https://drafts.csswg.org/cssom-view/#normalize-non-finite-values: NaN and the infinities are 0.</summary>
    public static double NormalizeNonFinite(double value) => double.IsFinite(value) ? value : 0;

    /// <summary>Returns the window of the document when it is the active document of its browsing context ("If document is not the active document [...] If window is null"), else null.</summary>
    public static Window? GetActiveWindow(Document document)
        => document.BrowsingContext is { } browsingContext && ReferenceEquals(browsingContext.Document, document) ? document.DefaultView : null;

    /// <summary>"If the element is the body element, document is in quirks mode, and the element is not potentially scrollable": the element scrolls the viewport.</summary>
    public static bool ScrollsTheViewport(Element element)
    {
        var document = element.NodeDocument;
        return document.Mode == DocumentMode.Quirks && ReferenceEquals(element, CssomViewHelpers.GetTheBodyElement(document)) && !CssomViewHelpers.IsPotentiallyScrollable(element, axis: null);
    }

    /// <summary>Returns the scrolling box of the element (the layout is computed when it is out of date), or null when it is not a scroll container.</summary>
    public static ScrollState? GetScrollState(Element element)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is null || !ComputedStyleDeclarations.IsBeingRendered(element))
            return null;

        return document.GetLayoutEngine().GetScrollState(element);
    }

    /// <summary>
    /// Returns the first box fragment of the element when it is not an inline box (the table wrapper box of a table, which has no
    /// border and contains the captions, as in the browsers), or null (no box, "or if the box is inline"). The layout is computed
    /// when it is out of date.
    /// </summary>
    public static BoxFragment? GetBlockFragment(Element element)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is null || !ComputedStyleDeclarations.IsBeingRendered(element))
            return null;

        var fragments = document.GetLayoutEngine().GetFragments(element);
        if (fragments.Count == 0 || fragments[0].Box.IsInlineBox)
            return null;

        return fragments[0];
    }

    /// <summary>
    /// Whether an axis is the inline axis of the fragment of a text field: its inner editor (see <see cref="Css.Layout.TextControlLayout"/>),
    /// the scroll container of its value, is in its content box, so its <c>client*</c> and <c>scroll*</c> members leave the
    /// padding of the inline axis out (csswg-drafts#13502).
    /// </summary>
    public static bool IsInlineAxisOfTextField(BoxFragment fragment, bool horizontal)
        => fragment.WritingDirection.IsHorizontal == horizontal && Css.Layout.TextControlLayout.GetInnerEditorFragment(fragment) is { IsScrollContainer: true };

    /// <summary>
    /// Rounds a size to an integer (the browsers snap the sizes to the pixels from their positions, but the line heights of
    /// the layout are not rounded to integers as theirs, so a size rounded from its position would depend on the position).
    /// </summary>
    public static int RoundSize(double size) => (int)Math.Round(size, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Scrolls an element (https://drafts.csswg.org/cssom-view/#scroll-an-element) to a scroll position in the coordinates of the
    /// fragment tree (null keeps the current position of the axis), clamped to its scrolling area. A scroll snap container
    /// snaps to the snap position closest to it in the scrolled axes (an absolute scroll, https://drafts.csswg.org/css-scroll-snap-1/#absolute-scroll).
    /// </summary>
    public static Task ScrollElement(LayoutEngine engine, ScrollState state, double? x, double? y, ScrollBehavior behavior, Element? element)
    {
        var position = state.Clamp(new PhysicalOffset(x ?? state.Offset.X, y ?? state.Offset.Y));
        position = engine.GetSnapPosition(state, SnapSelectionStrategy.ForEndPosition(position, x is not null, y is not null));
        return PerformScroll(engine, state, position, behavior, element);
    }

    /// <summary>
    /// Scrolls an element by an offset in the coordinates of the fragment tree: a relative scroll, which snaps to the snap position
    /// ahead in its direction (https://drafts.csswg.org/css-scroll-snap-1/#relative-scroll).
    /// </summary>
    public static Task ScrollElementBy(LayoutEngine engine, ScrollState state, PhysicalOffset delta, ScrollBehavior behavior, Element? element)
    {
        var position = engine.GetSnapPosition(state, SnapSelectionStrategy.ForDisplacement(state.Offset, delta));
        engine.RecordRelativeScroll(state, position - state.Offset);
        return PerformScroll(engine, state, position, behavior, element);
    }

    private static Task PerformScroll(LayoutEngine engine, ScrollState state, PhysicalOffset position, ScrollBehavior behavior, Element? element)
    {
        // https://drafts.csswg.org/css-overflow-5/#current-scroll-target: the current scroll target "is reset to null after [...]
        // any script initiated operations that do not have a target"
        engine.SetCurrentScrollTarget(state, target: null);

        // "If position is the same as box's current scroll position, and box does not have an ongoing smooth scroll, return a resolved Promise"
        // (the snap targets may have changed: https://drafts.csswg.org/css-scroll-snap-2/#scrollsnapchange)
        if (position == state.Offset && state.SmoothScroll is null)
        {
            engine.UpdateScrollSnapChangeTargets(state);
            return Task.CompletedTask;
        }

        return engine.PerformScroll(state, position, behavior, element);
    }
}

public partial class HtmlElement
{
    /// <summary>
    /// Gets the nearest ancestor that is a scroll container (https://drafts.csswg.org/cssom-view/#dom-htmlelement-scrollparent):
    /// the containing blocks are approximated by the flat tree ancestors (the positioned ancestors for the absolutely
    /// positioned elements), as there is no layout.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlelement-scrollparent"/>
    [IdlMember("scrollParent", "https://drafts.csswg.org/cssom-view/#dom-htmlelement-scrollparent")]
    public Element? ScrollParent
    {
        get
        {
            var document = NodeDocument;
            if (!ComputedStyleDeclarations.IsBeingRendered(this) || ReferenceEquals(this, document.DocumentElement) || ReferenceEquals(this, CssomViewHelpers.GetTheBodyElement(document)))
                return null;

            var position = CssomViewHelpers.GetStyle(this).GetComputedValue("position");
            var fixedWithoutContainingBlock = position == "fixed" && !CssomViewHelpers.HasFixedContainingBlockAncestor(this);
            if (fixedWithoutContainingBlock)
                return null;

            for (var ancestor = GetContainingBlockElement(this, position); ancestor is not null; ancestor = GetContainingBlockElement(ancestor, CssomViewHelpers.GetStyle(ancestor).GetComputedValue("position")))
            {
                var style = CssomViewHelpers.GetStyle(ancestor);
                var hidden = CssomViewHelpers.IsClosedShadowHiddenFrom(ancestor, this);
                if (!hidden && IsScrollContainer(ancestor, style))
                    return ancestor;

                if (style.GetComputedValue("position") == "fixed" && !CssomViewHelpers.HasFixedContainingBlockAncestor(ancestor))
                    return null;
            }

            // The initial containing block
            var scrollingElement = document.ScrollingElement;
            return scrollingElement is not null && !CssomViewHelpers.IsClosedShadowHiddenFrom(scrollingElement, this) ? scrollingElement : null;
        }
    }

    /// <summary>
    /// Gets the element the offsets are relative to (https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetparent): the
    /// nearest positioned ancestor, the body element, or a table, td or th ancestor of a static element.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetparent"/>
    [IdlMember("offsetParent", "https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetparent")]
    public Element? OffsetParent
    {
        get
        {
            // Every body element is excluded, not only the body element (https://github.com/w3c/csswg-drafts/issues/12834)
            var document = NodeDocument;
            var body = CssomViewHelpers.GetTheBodyElement(document);
            if (!ComputedStyleDeclarations.IsBeingRendered(this) || ReferenceEquals(this, document.DocumentElement) || this is HtmlBodyElement)
                return null;

            var position = CssomViewHelpers.GetStyle(this).GetComputedValue("position");
            if (position == "fixed" && !CssomViewHelpers.HasFixedContainingBlockAncestor(this))
                return null;

            // As in the browsers, an element rendered in the top layer has no offset parent: its containing block is the initial
            // containing block or the viewport (https://drafts.csswg.org/css-position-4/#top-layer), not one of its ancestors
            if (document.IsRenderedInTopLayer(this))
                return null;

            var inFixedContainingBlock = position == "fixed";
            var zoom = CssomViewHelpers.GetEffectiveZoom(this);
            for (var ancestor = CssomViewHelpers.GetFlatTreeParentElement(this); ancestor is not null; ancestor = CssomViewHelpers.GetFlatTreeParentElement(ancestor))
            {
                // As in the browsers, the ancestors in a shadow tree the element is not in are skipped, open or closed (the
                // specification only skips the closed shadow trees)
                var style = CssomViewHelpers.GetStyle(ancestor);
                var hidden = CssomViewHelpers.IsShadowHiddenFrom(ancestor, this);
                var fixedContainingBlock = CssomViewHelpers.EstablishesFixedContainingBlock(style);
                if (hidden)
                {
                    if (style.GetComputedValue("position") == "fixed" && !CssomViewHelpers.HasFixedContainingBlockAncestor(ancestor))
                        return null;

                    // The containing block of the fixed element is in a shadow tree the element is not in (the element is
                    // slotted in it): the search continues from the containing block, as for an element that is not fixed
                    if (inFixedContainingBlock && fixedContainingBlock)
                    {
                        inFixedContainingBlock = false;
                    }

                    continue;
                }

                if (inFixedContainingBlock && fixedContainingBlock)
                    return ancestor;

                if (!inFixedContainingBlock)
                {
                    if (fixedContainingBlock || style.GetComputedValue("position") != "static" || ReferenceEquals(ancestor, body))
                        return ancestor;

                    if (position == "static" && ancestor is HtmlTableCellElement or HtmlTableElement)
                        return ancestor;
                }

                if (CssomViewHelpers.GetEffectiveZoom(ancestor) != zoom)
                    return ancestor;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the vertical offset of the border box relative to the <see cref="OffsetParent"/> (https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsettop):
    /// the distance from the top padding edge of the first box of the offset parent to the top border edge of the first box of
    /// the element, or from the top of the initial containing block when there is no offset parent, ignoring the transforms and
    /// the scroll positions, divided by the effective zoom of the element. 0 for the body element and an element without box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsettop"/>
    [IdlMember("offsetTop", "https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsettop")]
    public int OffsetTop => GetOffsetPosition(horizontal: false);

    /// <summary>Gets the horizontal offset of the border box relative to the <see cref="OffsetParent"/> (https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetleft): see <see cref="OffsetTop"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetleft"/>
    [IdlMember("offsetLeft", "https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetleft")]
    public int OffsetLeft => GetOffsetPosition(horizontal: true);

    /// <summary>
    /// Gets the width of the border box (https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetwidth): the width of the
    /// bounding box of the border boxes of the fragments of the principal box of the element (e.g. the lines of an inline box),
    /// ignoring the transforms, divided by the effective zoom of the element, or 0 when it has no box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetwidth"/>
    [IdlMember("offsetWidth", "https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetwidth")]
    public int OffsetWidth => GetOffsetSize(horizontal: true);

    /// <summary>Gets the height of the border box (https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetheight): see <see cref="OffsetWidth"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetheight"/>
    [IdlMember("offsetHeight", "https://drafts.csswg.org/cssom-view/#dom-htmlelement-offsetheight")]
    public int OffsetHeight => GetOffsetSize(horizontal: false);

    private int GetOffsetPosition(bool horizontal)
    {
        // "If the element is the body element or does not have any associated box return zero"
        if (ReferenceEquals(this, CssomViewHelpers.GetTheBodyElement(NodeDocument)) || CssomViewGeometry.GetLayoutEngine(this) is not { } engine)
            return 0;

        var fragments = engine.GetFragments(this);
        if (fragments.Count == 0)
            return 0;

        var position = CssomViewGeometry.GetPositionInInitialContainingBlock(fragments[0]);
        var value = horizontal ? position.X : position.Y;
        // The offsets relative to a body element are relative to the initial containing block, as in the browsers (the steps of
        // older versions of the specification: "If the offsetParent of the element is null or the HTML body element")
        if (OffsetParent is { } offsetParent and not HtmlBodyElement && engine.GetFragments(offsetParent) is [var parentFragment, ..])
        {
            // The top padding edge of the first box of the offset parent (the table wrapper box of a table, which contains the
            // captions, as in the browsers)
            var parentPosition = CssomViewGeometry.GetPositionInInitialContainingBlock(parentFragment);
            value -= horizontal ? parentPosition.X + parentFragment.Border.Left + parentFragment.Scrollbars.Left : parentPosition.Y + parentFragment.Border.Top + parentFragment.Scrollbars.Top;
        }

        return CssomViewGeometry.RoundPosition(value / fragments[0].Box.Style.Layout.EffectiveZoom);
    }

    private int GetOffsetSize(bool horizontal)
    {
        if (CssomViewGeometry.GetLayoutEngine(this) is not { } engine)
            return 0;

        var fragments = engine.GetFragmentsWithSplitBlocks(this);
        if (fragments.Count == 0)
            return 0;

        var bounds = new PhysicalRect(CssomViewGeometry.GetPositionInInitialContainingBlock(fragments[0]), fragments[0].Size);
        for (var i = 1; i < fragments.Count; i++)
        {
            bounds = bounds.Union(new PhysicalRect(CssomViewGeometry.GetPositionInInitialContainingBlock(fragments[i]), fragments[i].Size));
        }

        var zoom = fragments[0].Box.Style.Layout.EffectiveZoom;
        return CssomViewScrolling.RoundSize((horizontal ? bounds.Width : bounds.Height) / zoom);
    }

    /// <summary>
    /// The element whose box is the containing block of an element (https://drafts.csswg.org/css-display/#containing-block),
    /// approximated without layout: the nearest positioned (or fixed containing block) ancestor for an absolutely positioned
    /// element, the nearest fixed containing block ancestor for a fixed positioned element, else the parent in the flat tree.
    /// Returns null for the initial containing block.
    /// </summary>
    private static Element? GetContainingBlockElement(Element element, string position)
    {
        for (var ancestor = CssomViewHelpers.GetFlatTreeParentElement(element); ancestor is not null; ancestor = CssomViewHelpers.GetFlatTreeParentElement(ancestor))
        {
            var style = CssomViewHelpers.GetStyle(ancestor);
            if (style.GetComputedValue("display") == "contents")
                continue;

            switch (position)
            {
                case "absolute" when style.GetComputedValue("position") == "static" && !CssomViewHelpers.EstablishesFixedContainingBlock(style):
                case "fixed" when !CssomViewHelpers.EstablishesFixedContainingBlock(style):
                    continue;
            }

            return ancestor;
        }

        return null;
    }

    /// <summary>https://drafts.csswg.org/css-overflow/#scroll-container: the overflow is neither visible nor clip.</summary>
    private static bool IsScrollContainer(Element element, ComputedStyle style)
    {
        // The overflow of the root element and of the body element propagates to the viewport
        if (ReferenceEquals(element, element.NodeDocument.DocumentElement))
            return false;

        return style.GetComputedValue("overflow-x") is not ("visible" or "clip") || style.GetComputedValue("overflow-y") is not ("visible" or "clip");
    }
}

public sealed partial class HtmlImageElement
{
    /// <summary>
    /// Gets the horizontal position of the image (https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-x): the x-coordinate
    /// of the left border edge of its first box relative to the initial containing block origin, ignoring the transforms, or 0
    /// when it does not generate a box.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-x"/>
    [IdlMember("x", "https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-x")]
    public int X => GetLayoutPosition().X;

    /// <summary>Gets the vertical position of the image (https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-y): the y-coordinate of the top border edge of its first box (see <see cref="X"/>).</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-y"/>
    [IdlMember("y", "https://drafts.csswg.org/cssom-view/#dom-htmlimageelement-y")]
    public int Y => GetLayoutPosition().Y;
}

public partial class Document
{
    private VisualViewport? _visualViewport;

    /// <summary>
    /// Gets the element that scrolls the viewport (https://drafts.csswg.org/cssom-view/#dom-document-scrollingelement): the
    /// root element, or in quirks mode the body element when it is not potentially scrollable.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-document-scrollingelement"/>
    [IdlMember("scrollingElement", "https://drafts.csswg.org/cssom-view/#dom-document-scrollingelement")]
    public Element? ScrollingElement
    {
        get
        {
            if (Mode == DocumentMode.Quirks)
            {
                var body = CssomViewHelpers.GetTheBodyElement(this);
                return body is not null && !CssomViewHelpers.IsPotentiallyScrollable(body, axis: null, parentClipIsHidden: true) ? body : null;
            }

            return DocumentElement;
        }
    }

    internal VisualViewport VisualViewport => _visualViewport ??= new VisualViewport(this);

    /// <summary>The visual viewport of the document, or null when it was never requested (it has no event listener).</summary>
    internal VisualViewport? VisualViewportOrNull => _visualViewport;
}

public partial class Window
{
    /// <summary>Gets the visual viewport of the document (https://drafts.csswg.org/cssom-view/#dom-window-visualviewport), or null when the document is not fully active.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-visualviewport"/>
    [IdlMember("visualViewport", "https://drafts.csswg.org/cssom-view/#dom-window-visualviewport")]
    public VisualViewport? VisualViewport => Document.IsFullyActive ? Document.VisualViewport : null;

    /// <summary>Moves the window (https://drafts.csswg.org/cssom-view/#dom-window-moveto): there is no client window, nothing happens.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-moveto"/>
    [IdlMember("moveTo", "https://drafts.csswg.org/cssom-view/#dom-window-moveto")]
    public void MoveTo(int x, int y)
    {
    }

    /// <summary>Moves the window (https://drafts.csswg.org/cssom-view/#dom-window-moveby): there is no client window, nothing happens.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-moveby"/>
    [IdlMember("moveBy", "https://drafts.csswg.org/cssom-view/#dom-window-moveby")]
    public void MoveBy(int x, int y)
    {
    }

    /// <summary>Resizes the window (https://drafts.csswg.org/cssom-view/#dom-window-resizeto): there is no client window, nothing happens.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-resizeto"/>
    [IdlMember("resizeTo", "https://drafts.csswg.org/cssom-view/#dom-window-resizeto")]
    public void ResizeTo(int width, int height)
    {
    }

    /// <summary>Resizes the window (https://drafts.csswg.org/cssom-view/#dom-window-resizeby): there is no client window, nothing happens.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-resizeby"/>
    [IdlMember("resizeBy", "https://drafts.csswg.org/cssom-view/#dom-window-resizeby")]
    public void ResizeBy(int x, int y)
    {
    }

    /// <summary>
    /// Gets the horizontal scroll position of the viewport (https://drafts.csswg.org/cssom-view/#dom-window-scrollx): the x
    /// coordinate of the left of the viewport relative to the initial containing block, or 0 without viewport.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scrollx"/>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-pagexoffset"/>
    [IdlMember("scrollX", "https://drafts.csswg.org/cssom-view/#dom-window-scrollx")]
    [IdlMember("pageXOffset", "https://drafts.csswg.org/cssom-view/#dom-window-pagexoffset")]
    public double ScrollX => GetViewportScrollState()?.Offset.X ?? 0;

    /// <summary>Gets the vertical scroll position of the viewport (https://drafts.csswg.org/cssom-view/#dom-window-scrolly): see <see cref="ScrollX"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scrolly"/>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-pageyoffset"/>
    [IdlMember("scrollY", "https://drafts.csswg.org/cssom-view/#dom-window-scrolly")]
    [IdlMember("pageYOffset", "https://drafts.csswg.org/cssom-view/#dom-window-pageyoffset")]
    public double ScrollY => GetViewportScrollState()?.Offset.Y ?? 0;

    /// <summary>
    /// Scrolls the viewport (https://drafts.csswg.org/cssom-view/#dom-window-scroll) to the scroll position of
    /// <see cref="ScrollToOptions.Left"/> and <see cref="ScrollToOptions.Top"/> (the current ones when they are absent), clamped
    /// to its scrolling area. The task completes when the scroll is completed.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scroll"/>
    [IdlMember("scroll", "https://drafts.csswg.org/cssom-view/#dom-window-scroll")]
    public Task Scroll(ScrollToOptions? options = null)
    {
        options ??= new ScrollToOptions();
        double? x = options.Left is { } left ? CssomViewScrolling.NormalizeNonFinite(left) : null;
        double? y = options.Top is { } top ? CssomViewScrolling.NormalizeNonFinite(top) : null;
        return ScrollCore(x, y, options.Behavior);
    }

    /// <summary>Scrolls the viewport (https://drafts.csswg.org/cssom-view/#dom-window-scroll): see <see cref="Scroll(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scroll"/>
    [IdlMember("scroll", "https://drafts.csswg.org/cssom-view/#dom-window-scroll")]
    public Task Scroll(double x, double y) => ScrollCore(CssomViewScrolling.NormalizeNonFinite(x), CssomViewScrolling.NormalizeNonFinite(y), ScrollBehavior.Auto);

    /// <summary>Scrolls the viewport (https://drafts.csswg.org/cssom-view/#dom-window-scrollto): see <see cref="Scroll(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scrollto"/>
    [IdlMember("scrollTo", "https://drafts.csswg.org/cssom-view/#dom-window-scrollto")]
    public Task ScrollTo(ScrollToOptions? options = null) => Scroll(options);

    /// <summary>Scrolls the viewport (https://drafts.csswg.org/cssom-view/#dom-window-scrollto): see <see cref="Scroll(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scrollto"/>
    [IdlMember("scrollTo", "https://drafts.csswg.org/cssom-view/#dom-window-scrollto")]
    public Task ScrollTo(double x, double y) => Scroll(x, y);

    /// <summary>Scrolls the viewport by an offset (https://drafts.csswg.org/cssom-view/#dom-window-scrollby): the offsets are added to <see cref="ScrollX"/> and <see cref="ScrollY"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scrollby"/>
    [IdlMember("scrollBy", "https://drafts.csswg.org/cssom-view/#dom-window-scrollby")]
    public Task ScrollBy(ScrollToOptions? options = null)
    {
        options ??= new ScrollToOptions();
        var left = CssomViewScrolling.NormalizeNonFinite(options.Left ?? 0);
        var top = CssomViewScrolling.NormalizeNonFinite(options.Top ?? 0);
        return ScrollCore(left, top, options.Behavior, relative: true);
    }

    /// <summary>Scrolls the viewport by an offset (https://drafts.csswg.org/cssom-view/#dom-window-scrollby): see <see cref="ScrollBy(ScrollToOptions?)"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-scrollby"/>
    [IdlMember("scrollBy", "https://drafts.csswg.org/cssom-view/#dom-window-scrollby")]
    public Task ScrollBy(double x, double y) => ScrollBy(new ScrollToOptions { Left = x, Top = y });

    /// <summary>The scrolling box of the viewport after the layout, or null when there is no viewport (the document is not rendered).</summary>
    private ScrollState? GetViewportScrollState()
    {
        var document = Document;
        if (BrowsingContext is null || document.LayoutEngineOrNull is not { } engine)
            return null;

        return engine.EnsureLayout() is null ? null : engine.ViewportScrollState;
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom-view/#dom-window-scroll: scrolls the viewport to a position (null keeps the current position
    /// of the axis), or by an offset when <paramref name="relative"/> is true (snapping in the direction of the scroll).
    /// </summary>
    internal Task ScrollCore(double? x, double? y, ScrollBehavior behavior, bool relative = false)
    {
        var document = Document;
        if (BrowsingContext is null || !document.IsFullyActive)
            return Task.CompletedTask;

        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is null)
            return Task.CompletedTask;

        var state = engine.ViewportScrollState;
        if (relative)
            return CssomViewScrolling.ScrollElementBy(engine, state, new PhysicalOffset(x ?? 0, y ?? 0), behavior, document.DocumentElement);

        return CssomViewScrolling.ScrollElement(engine, state, x, y, behavior, document.DocumentElement);
    }

    /// <summary>
    /// Gets the horizontal position of the client window on the screen (https://drafts.csswg.org/cssom-view/#dom-window-screenx):
    /// 0, or the position requested by the <c>left</c> feature of the <c>window.open()</c> that created the top-level browsing context.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-screenx"/>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-screenleft"/>
    [IdlMember("screenX", "https://drafts.csswg.org/cssom-view/#dom-window-screenx")]
    [IdlMember("screenLeft", "https://drafts.csswg.org/cssom-view/#dom-window-screenleft")]
    public int ScreenX => BrowsingContext?.Top.ClientWindowLeft ?? 0;

    /// <summary>Gets the vertical position of the client window on the screen (https://drafts.csswg.org/cssom-view/#dom-window-screeny): see <see cref="ScreenX"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-screeny"/>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-screentop"/>
    [IdlMember("screenY", "https://drafts.csswg.org/cssom-view/#dom-window-screeny")]
    [IdlMember("screenTop", "https://drafts.csswg.org/cssom-view/#dom-window-screentop")]
    public int ScreenY => BrowsingContext?.Top.ClientWindowTop ?? 0;

    /// <summary>
    /// Gets the width of the client window (https://drafts.csswg.org/cssom-view/#dom-window-outerwidth): the width of the
    /// viewport of the top-level browsing context (the client window has no decoration), or 0 without browsing context.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-outerwidth"/>
    [IdlMember("outerWidth", "https://drafts.csswg.org/cssom-view/#dom-window-outerwidth")]
    public int OuterWidth => BrowsingContext is { } browsingContext ? (int)StyleEngine.CreateEnvironment(browsingContext.Top.Document).Width : 0;

    /// <summary>Gets the height of the client window (https://drafts.csswg.org/cssom-view/#dom-window-outerheight): see <see cref="OuterWidth"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-window-outerheight"/>
    [IdlMember("outerHeight", "https://drafts.csswg.org/cssom-view/#dom-window-outerheight")]
    public int OuterHeight => BrowsingContext is { } browsingContext ? (int)StyleEngine.CreateEnvironment(browsingContext.Top.Document).Height : 0;
}

/// <summary>
/// The visual viewport of a document (https://drafts.csswg.org/cssom-view/#the-visualviewport-interface): there is no pinch
/// zoom, so it is the layout viewport without its scrollbars (the initial containing block computed by the layout: the
/// <c>clientWidth</c> and <c>clientHeight</c> of the root element), a scale of 1, no offset from the layout viewport, and the
/// scroll position of the layout viewport as page offsets. It is never scrolled relative to the layout viewport, so it never
/// fires <c>scroll</c> nor <c>scrollend</c> events; it fires <c>resize</c> events when its size changes (the size of the
/// viewport, or its classic scrollbars).
/// </summary>
/// <seealso href="https://drafts.csswg.org/cssom-view/#visualviewport"/>
[IdlInterface("VisualViewport", "https://drafts.csswg.org/cssom-view/#visualviewport")]
public sealed class VisualViewport : EventTarget, IRealmBoundPlatformObject
{
    private readonly Document _document;

    /// <summary>
    /// The size reported by the last resize steps or by the layout passes since then, tracked once the visual viewport has
    /// a resize event listener (see <see cref="RunResizeSteps"/>).
    /// </summary>
    private PhysicalSize? _lastSize;
    private bool _resizePending;

    internal VisualViewport(Document document)
    {
        _document = document;
    }

    internal override Window? RelevantGlobal => _document.RelevantWindow;

    Window? IRealmBoundPlatformObject.RealmWindow => _document.RelevantWindow;

    /// <summary>
    /// Gets the offset of the left edge of the visual viewport from the left edge of the layout viewport: 0, there is no pinch zoom.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-offsetleft"/>
    [IdlMember("offsetLeft", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-offsetleft")]
    public double OffsetLeft => 0;

    /// <summary>
    /// Gets the offset of the top edge of the visual viewport from the top edge of the layout viewport: 0, there is no pinch zoom.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-offsettop"/>
    [IdlMember("offsetTop", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-offsettop")]
    public double OffsetTop => 0;

    /// <summary>
    /// Gets the x coordinate of the left edge of the visual viewport relative to the initial containing block: the scroll
    /// position of the layout viewport plus <see cref="OffsetLeft"/> (<see cref="Window.ScrollX"/>), or 0 when the document is not fully active.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-pageleft"/>
    [IdlMember("pageLeft", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-pageleft")]
    public double PageLeft => _document.IsFullyActive && _document.DefaultView is { } window ? window.ScrollX + OffsetLeft : 0;

    /// <summary>
    /// Gets the y coordinate of the top edge of the visual viewport relative to the initial containing block: see <see cref="PageLeft"/>.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-pagetop"/>
    [IdlMember("pageTop", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-pagetop")]
    public double PageTop => _document.IsFullyActive && _document.DefaultView is { } window ? window.ScrollY + OffsetTop : 0;

    /// <summary>
    /// Gets the width of the visual viewport (https://drafts.csswg.org/cssom-view/#dom-visualviewport-width): the width of the
    /// viewport without the rendered vertical classic scrollbar, divided by <see cref="Scale"/> (the layout is computed when it
    /// is out of date), or 0 when the document is not fully active.
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-width"/>
    [IdlMember("width", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-width")]
    public double Width => _document.IsFullyActive ? GetSize().Width / Scale : 0;

    /// <summary>Gets the height of the visual viewport (https://drafts.csswg.org/cssom-view/#dom-visualviewport-height): see <see cref="Width"/>.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-height"/>
    [IdlMember("height", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-height")]
    public double Height => _document.IsFullyActive ? GetSize().Height / Scale : 0;

    /// <summary>Gets the pinch-zoom scale of the visual viewport: 1 (there is no pinch zoom), or 0 when the document is not fully active.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-scale"/>
    [IdlMember("scale", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-scale")]
    public double Scale => _document.IsFullyActive ? 1 : 0;

    /// <summary>The size of the layout viewport without its scrollbars, from the layout (computed when it is out of date), or the size of the viewport when the document is not rendered.</summary>
    private PhysicalSize GetSize()
    {
        if (_document.BrowsingContext is not null && _document.GetLayoutEngine() is var engine && engine.EnsureLayout() is not null)
            return engine.InitialContainingBlockSize;

        var environment = StyleEngine.CreateEnvironment(_document);
        return new PhysicalSize(environment.Width, environment.Height);
    }

    private protected override void OnEventListenerAdded(EventListenerEntry listener)
    {
        base.OnEventListenerAdded(listener);
        if (listener.Type == "resize" && _lastSize is null && _document.IsFullyActive && _document.DefaultView is { } window)
        {
            _lastSize = GetSize();
            window.RegisterForResizeSteps();
        }
    }

    /// <summary>
    /// Called by each layout pass of the document with the size of the initial containing block: the resize event of the next
    /// resize steps is queued when the size changed, even when it changes again before them (as in the browsers, e.g. classic
    /// scrollbars that appear then disappear).
    /// </summary>
    internal void OnLayout(PhysicalSize size)
    {
        if (_lastSize is { } last && last != size)
        {
            _lastSize = size;
            _resizePending = true;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/cssom-view/#run-the-resize-steps: "If the VisualViewport associated with doc has had its scale,
    /// width, or height properties changed since the last time these steps were run, fire an event named resize at the
    /// VisualViewport". The layout is updated first (the size of the viewport may have changed, e.g. a nested viewport).
    /// </summary>
    internal void RunResizeSteps()
    {
        if (_lastSize is not { } last || !_document.IsFullyActive)
            return;

        var size = GetSize();
        if (size != last)
        {
            _lastSize = size;
            _resizePending = true;
        }

        if (_resizePending)
        {
            _resizePending = false;
            FireEvent(new Event("resize"));
        }
    }

    /// <summary>Gets or sets the event handler of the <c>resize</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-onresize"/>
    [IdlMember("onresize", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-onresize")]
    public Action<Event>? OnResize
    {
        get => GetEventHandler("resize");
        set => SetEventHandler("resize", value);
    }

    /// <summary>Gets or sets the event handler of the <c>scroll</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-onscroll"/>
    [IdlMember("onscroll", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-onscroll")]
    public Action<Event>? OnScroll
    {
        get => GetEventHandler("scroll");
        set => SetEventHandler("scroll", value);
    }

    /// <summary>Gets or sets the event handler of the <c>scrollend</c> event.</summary>
    /// <seealso href="https://drafts.csswg.org/cssom-view/#dom-visualviewport-onscrollend"/>
    [IdlMember("onscrollend", "https://drafts.csswg.org/cssom-view/#dom-visualviewport-onscrollend")]
    public Action<Event>? OnScrollEnd
    {
        get => GetEventHandler("scrollend");
        set => SetEventHandler("scrollend", value);
    }
}
