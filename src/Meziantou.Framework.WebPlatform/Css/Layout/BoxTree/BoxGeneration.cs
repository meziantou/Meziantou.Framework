using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>How an element (or a pseudo-element) takes part in the box tree (https://drafts.csswg.org/css-display/#box-generation).</summary>
internal enum BoxGenerationKind : byte
{
    /// <summary>The element has no computed style: it is not connected, not in the flat tree, its document has no browsing context, or its navigable container is not being rendered.</summary>
    NoStyle,

    /// <summary>The element or one of its flat tree ancestors has <c>display: none</c>.</summary>
    DisplayNone,

    /// <summary>
    /// The element is in the contents of a replaced element or of a form control rendered as an atomic box (e.g. the
    /// fallback content of a <c>video</c>, the options of a <c>select</c>), or it is a <c>::before</c>/<c>::after</c>/<c>::marker</c>
    /// of such an element: it does not generate boxes (see <see cref="BoxGenerationInfo.ReplacedAncestor"/>).
    /// </summary>
    ReplacedContents,

    /// <summary>
    /// The element is an SVG element that is never rendered (<c>defs</c>, <c>symbol</c>, the paint servers...) or is in one,
    /// an SVG element outside of an <c>svg</c> element, or a non-SVG element in an SVG element other than <c>foreignObject</c>
    /// (https://svgwg.org/svg2-draft/render.html#Rendering).
    /// </summary>
    NotRenderedSvg,

    /// <summary>
    /// The pseudo-element is not generated: the <c>content</c> of a <c>::before</c>/<c>::after</c> is <c>none</c>, the originating
    /// element of a <c>::marker</c> is not a list item. An <c>embed</c> element that represents nothing has no box either
    /// (<see cref="BoxGeneration.RepresentsNothing"/>).
    /// </summary>
    NotGenerated,

    /// <summary><c>display: contents</c>: the element does not generate a box, its children and pseudo-elements do.</summary>
    Contents,

    /// <summary>The element generates boxes: it is being rendered (https://html.spec.whatwg.org/multipage/rendering.html#being-rendered).</summary>
    Box,
}

/// <summary>The result of <see cref="BoxGeneration.Get(Element)"/>.</summary>
/// <param name="Kind">How the element takes part in the box tree.</param>
/// <param name="ReplacedAncestor">For <see cref="BoxGenerationKind.ReplacedContents"/>, the nearest replaced element whose contents contain the element.</param>
/// <param name="InSkippedContents">
/// Whether the element is in the skipped contents of an ancestor (https://drafts.csswg.org/css-contain-2/#skips-its-contents):
/// <c>content-visibility: hidden</c>, or the contents of a closed <c>details</c> element (its second slot). The boxes are
/// generated, but they are not rendered (not painted, hit tested or focusable).
/// </param>
internal readonly record struct BoxGenerationInfo(BoxGenerationKind Kind, Element? ReplacedAncestor = null, bool InSkippedContents = false)
{
    /// <summary>Whether the element is being rendered (https://html.spec.whatwg.org/multipage/rendering.html#being-rendered): it generates boxes.</summary>
    public bool IsBeingRendered => Kind == BoxGenerationKind.Box;

    /// <summary>Whether the element has no boxes because of <c>display: none</c> (or has no style).</summary>
    public bool IsDisplayNone => Kind is BoxGenerationKind.DisplayNone or BoxGenerationKind.NoStyle;
}

/// <summary>
/// The box generation of the elements (https://drafts.csswg.org/css-display/#box-generation) decided from the computed
/// styles only, without building the box tree: the single "generates a box / is being rendered" query used by the box tree
/// builder (<see cref="BoxTreeBuilder"/>) and by the features that do not need the boxes (CSSOM View, the focus, innerText,
/// the container queries, the SVG bounding boxes...).
/// </summary>
internal static class BoxGeneration
{
    /// <summary>
    /// Returns how the element takes part in the box tree: <see cref="BoxGenerationKind.NoStyle"/> when it is not rendered
    /// (see <see cref="ComputedStyleDeclarations.IsRendered"/>), otherwise the result of <see cref="Get(ComputedStyle)"/>.
    /// </summary>
    public static BoxGenerationInfo Get(Element element)
    {
        if (!ComputedStyleDeclarations.IsRendered(element))
            return new BoxGenerationInfo(BoxGenerationKind.NoStyle);

        return Get(element.NodeDocument.GetStyleEngine().GetStyle(element));
    }

    /// <summary>
    /// Returns how the element or the pseudo-element of a computed style takes part in the box tree, from its computed
    /// style and the ones of its ancestors in the flat tree (the styles are the inheritance chain). The style must belong
    /// to a rendered element (the browsing context and the flat tree are not checked).
    /// </summary>
    public static BoxGenerationInfo Get(ComputedStyle style)
    {
        var display = style.Layout.Display;
        var kind = display.IsNone ? BoxGenerationKind.DisplayNone : display.IsContents ? BoxGenerationKind.Contents : BoxGenerationKind.Box;
        Element? replacedAncestor = null;
        var skipped = false;

        if (style.PseudoElement is { } pseudoElement)
        {
            if (ViewTransitionPseudoTree.IsViewTransitionPseudoElement(pseudoElement))
            {
                // The pseudo-elements of a view transition exist in the tree of the active view transition
                if (kind != BoxGenerationKind.DisplayNone && style.Element.NodeDocument.GetViewTransitionPseudoTree(style.Element)?.Contains(pseudoElement) != true)
                {
                    kind = BoxGenerationKind.NotGenerated;
                }
            }
            else if (kind != BoxGenerationKind.DisplayNone && style.Parent is { } originating && originating.PseudoElement is null)
            {
                var element = originating.Element;
                if (IsSvgElement(element))
                {
                    // The SVG elements have no CSS pseudo-element boxes (except the outermost svg element, a CSS box)
                    if (!IsSvgRoot(element))
                    {
                        kind = BoxGenerationKind.NotRenderedSvg;
                    }
                }
                else if (pseudoElement is "before" or "after" or "marker" && !GeneratesPseudoElements(element, originating))
                {
                    kind = BoxGenerationKind.ReplacedContents;
                    replacedAncestor = element;
                }
                else if (pseudoElement is "before" or "after" or StyleEngine.PickerIcon or StyleEngine.Checkmark && style.GetComputedValue("content") is "none" or "normal")
                {
                    kind = BoxGenerationKind.NotGenerated;
                }
                else if (pseudoElement == StyleEngine.SelectPicker && !originating.Engine.IsSelectPickerRendered(originating))
                {
                    // The select popover is only rendered with base appearance (a native picker is used otherwise)
                    kind = BoxGenerationKind.NotGenerated;
                }
                else if (pseudoElement == "marker" && !originating.Layout.Display.IsListItem)
                {
                    kind = BoxGenerationKind.NotGenerated;
                }

                // The ::before and ::after pseudo-elements are part of the contents of their originating element
                if (originating.Layout.ContentVisibility == ContentVisibility.Hidden && UsedContainment.CanApplySizeContainment(element, originating))
                {
                    skipped = true;
                }
            }
        }
        else if (kind != BoxGenerationKind.DisplayNone && IsSvgElement(style.Element) && !IsRenderedSvgElement(style.Element))
        {
            kind = BoxGenerationKind.NotRenderedSvg;
        }
        else if (kind != BoxGenerationKind.DisplayNone && RepresentsNothing(style.Element))
        {
            kind = BoxGenerationKind.NotGenerated;
        }

        // The ancestors in the flat tree (the originating element of a pseudo-element)
        var child = style;
        for (var parent = style.Parent; parent is not null; child = parent, parent = parent.Parent)
        {
            if (parent.Layout.Display.IsNone)
                return new BoxGenerationInfo(BoxGenerationKind.DisplayNone);

            // The children of a details element other than its summary are in its second slot, the ::details-content
            // pseudo-element (https://html.spec.whatwg.org/multipage/rendering.html#the-details-and-summary-elements), which
            // skips its contents while the details element is closed (content-visibility: hidden)
            if (parent.PseudoElement == StyleEngine.DetailsContent && kind != BoxGenerationKind.DisplayNone && !skipped
                && parent.Layout.ContentVisibility == ContentVisibility.Hidden && CanApplySizeContainmentToDetailsContent(parent))
            {
                skipped = true;
            }

            // The children of a select element rendered as a drop-down box other than its button are in its select popover,
            // the ::picker(select) pseudo-element (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2),
            // which is only rendered with base appearance: the select element renders its options otherwise
            if (parent.PseudoElement == StyleEngine.SelectPicker && child.PseudoElement is null && kind != BoxGenerationKind.DisplayNone
                && parent.Parent is { } selectStyle && !selectStyle.Engine.IsSelectPickerRendered(selectStyle))
            {
                if (!IsReplaced(selectStyle.Element, selectStyle))
                    return new BoxGenerationInfo(BoxGenerationKind.DisplayNone);

                if (replacedAncestor is null && kind != BoxGenerationKind.NotRenderedSvg)
                {
                    replacedAncestor = selectStyle.Element;
                    kind = BoxGenerationKind.ReplacedContents;
                }
            }

            // The pseudo-elements were handled above
            if (child.PseudoElement is not null || parent.PseudoElement is not null || kind == BoxGenerationKind.DisplayNone)
                continue;

            var element = parent.Element;
            if (kind != BoxGenerationKind.NotRenderedSvg && IsSvgElement(element))
            {
                // A non-SVG element is only rendered in a foreignObject element; an SVG element is not rendered in a never-rendered element
                if (IsSvgElement(child.Element) ? IsNeverRenderedSvgElement(element) : element.LocalName != "foreignObject")
                {
                    kind = BoxGenerationKind.NotRenderedSvg;
                }
            }
            else if (replacedAncestor is null && kind != BoxGenerationKind.NotRenderedSvg && (HidesChildren(element, parent) || (element.IsHtml(TagId.Frameset) && !FramesetFormattingContext.IsFramesetChild(child.Element))))
            {
                // (only the frame and frameset children of a frameset are rendered, see FramesetFormattingContext)
                replacedAncestor = element;
                kind = BoxGenerationKind.ReplacedContents;
            }

            if (!skipped && parent.Layout.ContentVisibility == ContentVisibility.Hidden && UsedContainment.CanApplySizeContainment(element, parent))
            {
                skipped = true;
            }
        }

        return kind == BoxGenerationKind.DisplayNone ? new BoxGenerationInfo(kind) : new BoxGenerationInfo(kind, replacedAncestor, skipped);
    }

    /// <summary>
    /// Whether the element is a replaced element (https://html.spec.whatwg.org/multipage/rendering.html#replaced-elements) or
    /// a form control or widget rendered as an atomic box: its children do not generate boxes. These are the embedded
    /// content elements (<c>img</c>, <c>iframe</c>, <c>embed</c>, <c>video</c>, <c>audio</c>, a <c>canvas</c> when scripting
    /// is enabled, an <c>object</c> that does not represent its fallback content), <c>frame</c>, the form
    /// controls (<c>input</c>, <c>textarea</c>, a <c>select</c> that is not a customizable select, <c>meter</c>,
    /// <c>progress</c>) and the outermost <c>svg</c> element (its contents are SVG boxes).
    /// </summary>
    public static bool IsReplaced(Element element, ComputedStyle style)
    {
        if (!element.IsHtmlNamespace)
            return IsSvgRoot(element);

        return element.TagId switch
        {
            TagId.Img or TagId.Iframe or TagId.Frame or TagId.Embed or TagId.Video or TagId.Audio or TagId.Textarea => true,

            // (a meter and a progress bar with base appearance hold the boxes of their parts, see FormControlContents)
            TagId.Meter or TagId.Progress => !FormControlContents.HasBaseAppearanceBoxes(element, style),

            // As in the browsers, a checkbox or a radio button whose appearance is none is not a widget: it is a regular box
            // (an inline-block), with its ::before and ::after pseudo-elements
            // A file upload control is an inline-block box that holds the box of its button and its text, and some controls
            // with base appearance hold the boxes of their parts (see FormControlContents)
            TagId.Input => !(style.GetComputedValue("appearance") == "none" && Infra.InfraAscii.ToAsciiLowercase(element.GetAttributeValue("type") ?? "") is "checkbox" or "radio")
                && !FormControlContents.IsFileUploadControl(element) && !FormControlContents.HasBaseAppearanceBoxes(element, style),

            // https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element: the canvas represents embedded content when
            // scripting is enabled, its fallback content otherwise
            TagId.Canvas => style.Environment.ScriptingEnabled,
            TagId.Object => element is HtmlObjectElement { IsShowingFallbackContent: false },

            // https://html.spec.whatwg.org/multipage/form-elements.html#customizable-select: the children of a customizable select are rendered
            TagId.Select => !StyleEngine.HasBaseAppearance(style),
            _ => false,
        };
    }

    /// <summary>
    /// Whether the children of the element do not generate boxes: the element is replaced (see <see cref="IsReplaced"/>), but
    /// a list box with native appearance, whose rows are the boxes of its children (see <see cref="FormControlContents"/>);
    /// or it is an option of such a list box, whose row shows its label.
    /// </summary>
    private static bool HidesChildren(Element element, ComputedStyle style)
    {
        if (element.TagId == TagId.Option && element.IsHtmlNamespace)
            return style.SelectStyle is { } selectStyle && FormControlContents.IsNativeListBox(selectStyle.Element, selectStyle);

        return IsReplaced(element, style) && !FormControlContents.IsNativeListBox(element, style);
    }

    /// <summary>
    /// Whether the <c>::before</c>, <c>::after</c> and <c>::marker</c> pseudo-elements of the element are generated: not for
    /// the replaced elements (https://drafts.csswg.org/css-pseudo-4/#generated-content); the checkboxes and the radio buttons
    /// whose <c>appearance</c> is <c>none</c> or <c>base</c> are not replaced (see <see cref="IsReplaced"/>); nor for the
    /// file upload controls, as in the browsers, and the other controls whose contents are generated; nor for a <c>frameset</c>,
    /// whose only children boxes are the ones of its <c>frame</c> and <c>frameset</c> children (see <see cref="FramesetFormattingContext"/>).
    /// </summary>
    public static bool GeneratesPseudoElements(Element element, ComputedStyle style) => !IsReplaced(element, style) && !FormControlContents.HasGeneratedContents(element, style) && !element.IsHtml(TagId.Frameset);

    /// <summary>
    /// Whether the element is an embed element that represents nothing (https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-embed-element:
    /// it has neither a src nor a type attribute): as in the browsers, it generates no box.
    /// </summary>
    public static bool RepresentsNothing(Element element)
        => element.IsHtml(TagId.Embed) && !element.HasAttributeWithLocalName("src") && !element.HasAttributeWithLocalName("type");

    /// <summary>Whether the element is an SVG element.</summary>
    public static bool IsSvgElement(Element element) => ReferenceEquals(element.NamespaceUri, Namespaces.Svg);

    /// <summary>
    /// Whether the element is an outermost svg element (https://svgwg.org/svg2-draft/struct.html#Glossary): an svg element
    /// whose parent in the flat tree is not an SVG element, or is a foreignObject element. It generates a CSS box (a replaced box)
    /// whose contents are SVG boxes.
    /// </summary>
    public static bool IsSvgRoot(Element element) => IsSvgElement(element) && element.LocalName == "svg" && !IsInSvgContent(element);

    /// <summary>Whether the parent of an element in the flat tree is an SVG element other than foreignObject (the element is in the SVG content of an svg element).</summary>
    private static bool IsInSvgContent(Element element) => HtmlElement.GetFlatTreeParent(element) is Element parent && IsSvgElement(parent) && parent.LocalName != "foreignObject";

    /// <summary>
    /// Whether the SVG element is never rendered (https://svgwg.org/svg2-draft/render.html#TermNeverRenderedElement): the
    /// definitions, the paint servers, the resources and the metadata. A <c>symbol</c> is only rendered through a <c>use</c>
    /// element (in its shadow tree).
    /// </summary>
    public static bool IsNeverRenderedSvgElement(Element element) => IsSvgElement(element)
        && element.LocalName is "clipPath" or "defs" or "desc" or "linearGradient" or "marker" or "mask" or "metadata" or "pattern" or "radialGradient" or "script" or "style" or "symbol" or "title" or "filter";

    /// <summary>Whether the SVG element itself can be rendered: it is not never rendered, and it is in an svg element (or it is an outermost svg element).</summary>
    private static bool IsRenderedSvgElement(Element element)
    {
        if (IsNeverRenderedSvgElement(element))
            return false;

        return element.LocalName == "svg" || IsInSvgContent(element);
    }

    /// <summary>
    /// Whether size containment (so <c>content-visibility</c>) can apply to the box of the <c>::details-content</c>
    /// pseudo-element of a style (see <see cref="UsedContainment.CanApplySizeContainment(ContainerBox)"/>): it generates a
    /// box that is not an inline box, an internal table or ruby box, a ruby container or a table.
    /// </summary>
    internal static bool CanApplySizeContainmentToDetailsContent(ComputedStyle style)
    {
        var display = style.Layout.Display;
        return !display.IsNone && !display.IsContents && !display.IsInlineBox && display.Internal is DisplayInternal.None or DisplayInternal.TableCaption && display.Inside is not (DisplayInside.Table or DisplayInside.Ruby);
    }
}
