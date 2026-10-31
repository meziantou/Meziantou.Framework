using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The trigger of a focus change (https://html.spec.whatwg.org/multipage/interaction.html#focusing-steps).</summary>
internal enum FocusTrigger
{
    Other,
    Click,
}

/// <summary>The kind of position of a <see cref="FocusNavigationStartingPoint"/>.</summary>
internal enum FocusNavigationStartingPointKind
{
    /// <summary>The node (e.g. the clicked node, or the target of a fragment navigation).</summary>
    Node,

    /// <summary>The position after the node and its descendants in the flat tree (the node preceded a removed node).</summary>
    AfterNode,

    /// <summary>The position before the children of the node in the flat tree (the first child of the node was removed).</summary>
    BeforeChildren,

    /// <summary>
    /// The position before the node in the flat tree: the node itself is the next area when it is focusable (the scroll target of
    /// an activated scroll marker, https://drafts.csswg.org/css-overflow-5/#scroll-marker-next-focus: "the next tabindex-ordered
    /// focus navigation will focus the scroll target if it is focusable, otherwise, it will find the next focusable element from
    /// the scroll target").
    /// </summary>
    BeforeNode,
}

/// <summary>
/// The sequential focus navigation starting point (https://html.spec.whatwg.org/multipage/interaction.html#sequential-focus-navigation-starting-point):
/// a node, or, as in browsers, the position in the flat tree of a removed node that contained the starting point or the
/// focused area, so the navigation continues from where the node was.
/// </summary>
internal readonly record struct FocusNavigationStartingPoint(Node Node, FocusNavigationStartingPointKind Kind = FocusNavigationStartingPointKind.Node);

// The focus state of a document (https://html.spec.whatwg.org/multipage/interaction.html#focus)
public partial class Document
{
    /// <summary>
    /// The focused area of the document (https://html.spec.whatwg.org/multipage/interaction.html#focused-area-of-the-document):
    /// an element, or null for the viewport of the document.
    /// </summary>
    internal Element? FocusedArea
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            var previous = field;
            field = value;

            // A focused scroll navigation control (a pseudo-element) is anchored at its scroll container
            FocusedScrollControl = PendingFocusedScrollControl is { } control && ReferenceEquals(control.Scroller, value) ? control : null;
            OnElementStateChangedForStyles(previous, value, StyleInvalidationStates.Focus);

            // The caret is painted in the focused text control or editing host
            _selection?.OnFocusedAreaChanged(value);
            OnCaretChanged();
        }
    }

    /// <summary>Incremented by each focus change of the top-level document, to detect the focus changes made by the event handlers of the focus events.</summary>
    internal int FocusChangeVersion { get; set; }

    /// <summary>Whether the focused element matches <c>:focus-visible</c> (the user agent indicates the focus).</summary>
    internal bool FocusVisible { get; set; }

    /// <summary>
    /// Whether the focused element was focused with <c>focusVisible: false</c>
    /// (https://html.spec.whatwg.org/multipage/interaction.html#dom-focusoptions-focusvisible): its focus is not indicated
    /// even when it supports the keyboard input.
    /// </summary>
    internal bool FocusVisibleSuppressed { get; set; }

    /// <summary>
    /// Whether the last focus change made by the user was a click that focused an element, with no keyboard interaction since
    /// (the heuristic of the browsers for <c>:focus-visible</c>: an element focused by script then does not indicate its
    /// focus). Set on the active document of the top-level browsing context.
    /// </summary>
    internal bool LastFocusWasPointer
    {
        get;
        set
        {
            field = value;
            LastPointerFocusDocument = null;
        }
    }

    /// <summary>
    /// The document of the element that the click of <see cref="LastFocusWasPointer"/> focused, or null for any document: as
    /// in Chrome, the heuristic is per document (an element focused by script in another document, e.g. a nested one that
    /// the user did not click, indicates its focus).
    /// </summary>
    internal Document? LastPointerFocusDocument { get; set; }

    /// <summary>The slider (<c>input type=range</c>) the user is dragging, and the pointer that drags it (see <see cref="HtmlInputElement.RunSliderDefaultAction"/>).</summary>
    internal (HtmlInputElement Slider, int PointerId)? DraggedSlider { get; set; }

    /// <summary>
    /// The element whose space keydown event was not canceled: it is activated when the key is released. Until then, it is
    /// in a formal activation state (it matches <c>:active</c>).
    /// </summary>
    internal Element? SpaceKeyActivationTarget
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            field?.NodeDocument.IncrementElementStateVersion();
            field = value;
            value?.NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#sequential-focus-navigation-starting-point</summary>
    internal FocusNavigationStartingPoint? SequentialFocusNavigationStartingPoint { get; set; }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#autofocus-candidates</summary>
    internal List<Element> AutofocusCandidates => _autofocusCandidates ??= [];

    private List<Element>? _autofocusCandidates;

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#autofocus-processed-flag</summary>
    internal bool AutofocusProcessed { get; set; }

    /// <summary>
    /// The order of the last focus change that put the document in the focus chain of its top-level traversable, or 0 when it
    /// was never focused: the service workers list the most recently focused windows first (https://w3c.github.io/ServiceWorker/#clients-matchall).
    /// </summary>
    internal long LastFocusOrder { get; set; }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-document-hasfocus: whether the key events are routed
    /// through or to the document, and its top-level browsing context has the system focus (see <see cref="BrowsingContext.HasSystemFocus"/>).
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-document-hasfocus"/>
    [IdlMember("hasFocus", "https://html.spec.whatwg.org/multipage/interaction.html#dom-document-hasfocus")]
    public bool HasFocus() => FocusModel.HasFocus(this);

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement: the focused element,
    /// retargeted against the document, or the body element (or the document element) when the viewport is focused.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement"/>
    [IdlMember("activeElement", "https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement")]
    public Element? ActiveElement => FocusModel.GetActiveElement(this);

    /// <summary>Whether the element is the trigger of a showing popover (https://html.spec.whatwg.org/multipage/popover.html#popover-trigger).</summary>
    internal bool IsTriggerOfShowingPopover(Element element)
    {
        if (_topLayer is null)
            return false;

        foreach (var topLayerElement in _topLayer)
        {
            if (topLayerElement is HtmlElement { IsPopoverShowing: true } popover && ReferenceEquals(popover.PopoverTrigger, element))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The close request steps (https://html.spec.whatwg.org/multipage/interaction.html#close-request-steps) that follow the
    /// relevant event of a close request of the user targeted at the document (e.g. the <c>keydown</c> event of the Escape
    /// key, when it is not canceled): the close watchers of the last group of the window (the ones of the
    /// <see cref="CloseWatcher"/> objects, of the open dialogs and of the showing auto and hint popovers) are requested to
    /// close. Returns false when nothing watched the close request.
    /// </summary>
    internal bool ProcessCloseRequest()
    {
        if (!IsFullyActive || RelevantWindow is not { } window)
            return false;

        // "If document's fullscreen element is non-null, then fully exit fullscreen given document's node navigable's
        // top-level traversable's active document" (https://fullscreen.spec.whatwg.org/#ui: it takes precedence over the close
        // watchers). The top-level document is fullscreen whenever one of the documents of its navigables is.
        if (BrowsingContext is { } navigable && navigable.Top.Document is { FullscreenElementInternal: not null } topLevelDocument)
        {
            FullscreenModel.FullyExit(topLevelDocument);
            return true;
        }

        return window.ProcessCloseWatchers();
    }

    /// <summary>The topmost modal dialog of the top layer, which blocks the document (https://html.spec.whatwg.org/multipage/interaction.html#blocked-by-a-modal-dialog).</summary>
    internal HtmlDialogElement? GetBlockingModalDialog()
    {
        if (_topLayer is null)
            return null;

        for (var i = _topLayer.Count - 1; i >= 0; i--)
        {
            if (_topLayer[i] is HtmlDialogElement { IsModal: true } dialog)
                return dialog;
        }

        return null;
    }
}

public sealed partial class ShadowRoot
{
    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement: the focused element, retargeted against the shadow root.</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement"/>
    [IdlMember("activeElement", "https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement")]
    public Element? ActiveElement => FocusModel.GetActiveElement(this);
}

/// <summary>
/// The focus (https://html.spec.whatwg.org/multipage/interaction.html#focus). Nothing is rendered: an element is
/// considered rendered (see <see cref="IsBeingRendered"/>) when its document has a browsing context and the computed
/// styles of the element and of its ancestors in the flat tree generate a box. Each top-level browsing context has the
/// system focus.
/// </summary>
internal static class FocusModel
{
    // The number of focus changes of the process (see Document.LastFocusOrder)
    private static long s_focusOrder;

    // ----------------------------------------------------------------------------------------------------------------
    // Focusable areas
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Whether the element is a focusable area (https://html.spec.whatwg.org/multipage/interaction.html#focusable-area):
    /// it has a tabindex value or is focusable by default, it does not delegate its focus, it is not actually disabled, it is
    /// not inert, and it is being rendered (or delegates its rendering to its children).
    /// </summary>
    public static bool IsFocusableArea(Element element)
    {
        // https://drafts.csswg.org/css-overflow-5/#scroll-focus: the scroll container of a focused (or being focused) scroll
        // navigation control is the DOM anchor of the control, a focusable area
        if ((element.NodeDocument.PendingFocusedScrollControl ?? element.NodeDocument.FocusedScrollControl) is { } control && ReferenceEquals(control.Scroller, element))
            return ScrollControlActions.IsFocusable(control);

        return IsFocusableElement(element);
    }

    /// <summary>Whether the element itself is a focusable area (see <see cref="IsFocusableArea(Element)"/>), not as the DOM anchor of a focused scroll navigation control.</summary>
    internal static bool IsFocusableElement(Element element)
    {
        if (GetTabIndexValue(element) is null && !IsFocusableByDefault(element))
            return false;

        if (element.AttachedShadowRoot is { DelegatesFocus: true })
            return false;

        if (HtmlElementStates.IsActuallyDisabled(element) || IsInert(element))
            return false;

        // An area element is not rendered: its shapes are rendered by the images that use its image map
        if (element.IsHtml(TagId.Area))
            return HasRenderedImage(element);

        return IsBeingRendered(element);
    }

    /// <summary>
    /// Whether an image that uses the image map of the area element is being rendered and is not inert: the shapes of the
    /// area are then focusable areas (https://html.spec.whatwg.org/multipage/interaction.html#focusable-area). As in
    /// browsers, the area element is the focused element (the specification makes the image the DOM anchor of the shapes).
    /// </summary>
    private static bool HasRenderedImage(Element area)
    {
        var map = area.ParentElement;
        while (map is not null && !map.IsHtml(TagId.Map))
        {
            map = map.ParentElement;
        }

        if (map is null || !map.IsConnected)
            return false;

        var root = map.GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element image && image.IsHtml(TagId.Img) && ReferenceEquals(GetImageMap(image), map) && IsBeingRendered(image) && !IsInert(image))
                return true;
        }

        return false;
    }

    /// <summary>The image map of an image: its usemap attribute parsed as a hash-name reference to a map element (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-a-hash-name-reference).</summary>
    private static Element? GetImageMap(Element image)
    {
        if (image.GetAttributeValue("usemap") is not { } usemap)
            return null;

        var index = usemap.IndexOf('#', StringComparison.Ordinal);
        if (index < 0 || index == usemap.Length - 1)
            return null;

        var name = usemap[(index + 1)..];
        var root = image.GetRoot();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element map && map.IsHtml(TagId.Map) && (map.GetAttributeValue("id") == name || map.GetAttributeValue("name") == name))
                return map;
        }

        return null;
    }

    /// <summary>Whether the node is a focusable area: an element (see <see cref="IsFocusableArea(Element)"/>), or the viewport of a document that has a browsing context.</summary>
    public static bool IsFocusableArea(Node node) => node switch
    {
        Element element => IsFocusableArea(element),
        Document document => document.BrowsingContext is not null && document.IsFullyActive && !IsInert(document),
        _ => false,
    };

    /// <summary>
    /// The elements that are focusable areas without a tabindex attribute (https://html.spec.whatwg.org/multipage/interaction.html#tabindex-value):
    /// <c>a</c> and <c>area</c> elements with an <c>href</c> attribute (HTML, SVG and MathML), <c>button</c>, <c>input</c> elements that are not
    /// hidden, <c>select</c>, <c>textarea</c>, the <c>summary</c> of a <c>details</c> element, the editing hosts, the navigable
    /// containers, and the media elements with controls.
    /// </summary>
    private static bool IsFocusableByDefault(Element element)
    {
        if (element.IsHtmlNamespace)
        {
            switch (element.TagId)
            {
                case TagId.A or TagId.Area:
                    return element.HasAttributeWithLocalName("href");
                case TagId.Button:
                    // The button of a customizable select is disabled with the select
                    return element.ParentElement is not HtmlSelectElement select || !HtmlElementStates.IsActuallyDisabled(select);
                case TagId.Select:
                    // A customizable list box is not focusable (its options are)
                    return !(element.HasAttributeWithLocalName("multiple") || element.GetAttributeValue("size") is { } size && Microsyntaxes.TryParseNonNegativeInteger(size, out var rows) && rows > 1)
                        || !ComputedStyleDeclarations.IsRendered(element)
                        || !StyleEngine.HasBaseAppearance(element.NodeDocument.GetStyleEngine().GetStyle(element));
                case TagId.Option:
                    // The options of a select element whose contents are rendered with base appearance are boxes that the
                    // user focuses (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element: "focus another option")
                    return element is HtmlOptionElement { CachedNearestAncestorSelect: { } optionSelect } && optionSelect.AreContentsRenderedWithBaseAppearance();
                case TagId.Textarea or TagId.Iframe or TagId.Frame:
                    return true;
                case TagId.Dialog:
                    // As in browsers, a dialog can be focused (by its focusing steps or by script), but not sequentially or by a click
                    return true;
                case TagId.Input:
                    return HtmlElementStates.GetInputType(element) != "hidden";
                case TagId.Summary:
                    return Reflection.IsSummaryForItsParentDetails(element);
                case TagId.Audio or TagId.Video:
                    return element.HasAttributeWithLocalName("controls");
            }

            return IsEditingHost(element);
        }

        if (ReferenceEquals(element.NamespaceUri, Namespaces.Svg) && element.LocalName == "a")
            return element.HasAttributeWithLocalName("href") || element.HasAttributeNS(Namespaces.XLink, "href");

        // https://w3c.github.io/mathml-core/#the-a-element: the href attribute has the same semantics as for the HTML a element
        if (element is MathMLAnchorElement)
            return element.HasAttributeWithLocalName("href");

        return false;
    }

    /// <summary>Whether the element is an editing host (https://html.spec.whatwg.org/multipage/interaction.html#editing-host): its contenteditable state is true and its parent is not editable.</summary>
    private static bool IsEditingHost(Element element)
    {
        if (element.NodeDocument.IsDesignModeEnabled)
            return ReferenceEquals(element, element.NodeDocument.DocumentElement);

        var value = element.GetAttributeValue("contenteditable");
        if (value is null)
            return false;

        value = InfraAscii.ToAsciiLowercase(value);
        if (value is not ("" or "true" or "plaintext-only"))
            return false;

        return element.ParentElement is not { } parent || !HtmlElementStates.IsEditingHostOrEditable(parent);
    }

    /// <summary>
    /// The tabindex value of the element (https://html.spec.whatwg.org/multipage/interaction.html#tabindex-value): its
    /// tabindex attribute parsed as an integer, or null. The attribute applies to the HTML, SVG and MathML elements.
    /// </summary>
    public static long? GetTabIndexValue(Element element)
    {
        if (!element.IsHtmlNamespace && !ReferenceEquals(element.NamespaceUri, Namespaces.Svg) && !ReferenceEquals(element.NamespaceUri, Namespaces.MathML))
            return null;

        return element.GetAttributeValue("tabindex") is { } value && Microsyntaxes.TryParseInteger(value, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Whether the focusable area is sequentially focusable (https://html.spec.whatwg.org/multipage/interaction.html#sequentially-focusable):
    /// its tabindex value is not negative. As in the browsers, a slot that generates no box (<c>display: contents</c>, its
    /// default style) is not sequentially focusable, although it delegates its rendering to its children: it is only the owner
    /// of the focus navigation scope of its assigned nodes.
    /// </summary>
    public static bool IsSequentiallyFocusable(Element element)
    {
        if (!IsFocusableElement(element))
            return false;

        var sequentiallyFocusable = GetTabIndexValue(element) switch
        {
            null => element is HtmlOptionElement option ? IsOptionTabStop(option) : element is not HtmlDialogElement || IsEditingHost(element),
            var tabIndex => tabIndex >= 0,
        };

        if (sequentiallyFocusable && element is HtmlInputElement { InputType: InputType.Radio } radio)
        {
            sequentiallyFocusable = IsRadioTabStop(radio);
        }

        return sequentiallyFocusable && (element is not HtmlSlotElement || BoxGeneration.Get(element).Kind != BoxGenerationKind.Contents);
    }

    /// <summary>
    /// Whether a radio button is reached by the sequential focus navigation. As in the browsers (unspecified), the radio buttons
    /// of a radio button group (https://html.spec.whatwg.org/multipage/input.html#radio-button-group) are a single stop: its
    /// checked radio button, unless it cannot be focused (e.g. it is disabled), else its first radio button that can be focused
    /// in tree order, in both directions; the arrow keys move the focus within the group (see
    /// <see cref="HtmlInputElement.RunRadioKeyDefaultAction"/>). A radio button without group is a stop.
    /// </summary>
    private static bool IsRadioTabStop(HtmlInputElement radio)
    {
        HtmlInputElement? first = null;
        foreach (var candidate in radio.GetRadioButtonGroup(includeSelf: true))
        {
            if (GetTabIndexValue(candidate) is < 0 || !IsFocusableElement(candidate))
                continue;

            if (candidate.Checkedness)
                return ReferenceEquals(candidate, radio);

            first ??= candidate;
        }

        return first is null || ReferenceEquals(first, radio);
    }

    /// <summary>
    /// Whether the option of a select element whose contents are rendered with base appearance is reached by the sequential
    /// focus navigation, as in Chrome: the options of a list box are a single stop (its first selected option that can be
    /// focused, else its first option that can be focused; the arrow keys focus the other ones), and the Tab key closes the
    /// picker of a drop-down box.
    /// </summary>
    private static bool IsOptionTabStop(HtmlOptionElement option)
    {
        if (option.CachedNearestAncestorSelect is not { } select || StyleEngine.IsDropDownBox(select))
            return false;

        HtmlOptionElement? first = null;
        foreach (var candidate in select.GetListOfOptions())
        {
            if (candidate.GetAttributeValue("tabindex") is not null || !IsFocusableElement(candidate))
                continue;

            if (candidate.Selectedness)
                return ReferenceEquals(candidate, option);

            first ??= candidate;
        }

        return ReferenceEquals(first, option);
    }

    /// <summary>Whether the focusable area is click focusable (https://html.spec.whatwg.org/multipage/interaction.html#click-focusable): a dialog without a tabindex value is not.</summary>
    public static bool IsClickFocusable(Element element) => IsFocusableElement(element) && (element is not HtmlDialogElement || GetTabIndexValue(element) is not null || IsEditingHost(element));

    /// <summary>
    /// Whether the element is considered rendered for the focus (see <see cref="BoxGeneration"/>): its document has a
    /// browsing context, and it generates boxes (an element with <c>display: contents</c> delegates its rendering to its
    /// children; the relevant canvas fallback content is focusable, https://html.spec.whatwg.org/multipage/canvas.html#being-used-as-relevant-canvas-fallback-content),
    /// not in the skipped contents of an ancestor (<c>content-visibility: hidden</c>, a closed <c>details</c>), and, as in
    /// browsers, it is visible (<c>visibility</c>). As in the browsers, the options of a select that is not a customizable
    /// select have no box: the select renders them (WPT canvas-descendants-focusability-006.html).
    /// </summary>
    public static bool IsBeingRendered(Element element)
    {
        var info = BoxGeneration.Get(element);
        var rendered = info.Kind switch
        {
            BoxGenerationKind.Box or BoxGenerationKind.Contents => true,
            BoxGenerationKind.ReplacedContents => info.ReplacedAncestor is { } replaced && replaced.IsHtml(TagId.Canvas),
            _ => false,
        };

        if (!rendered || info.InSkippedContents)
            return false;

        return element.NodeDocument.GetStyleEngine().GetStyle(element).Layout.Visibility == Visibility.Visible;
    }

    /// <summary>
    /// Whether the node is inert (https://html.spec.whatwg.org/multipage/interaction.html#inert): it is in the subtree of an
    /// element with the inert attribute (in the flat tree), or its document is blocked by a modal dialog and it is not in the
    /// subtree of the dialog (the dialog escapes the inertness of its ancestors). As in the browsers, the nodes of the
    /// document of an inert navigable container are not inert: the hit testing and the sequential focus navigation do not
    /// enter the container, but the scripts of the document can focus its elements, and the sequential focus navigation
    /// moves within the document and leaves it.
    /// </summary>
    public static bool IsInert(Node node)
    {
        if (node is Document)
            return false;

        var modalDialog = node.IsConnected ? node.NodeDocument.GetBlockingModalDialog() : null;
        for (Node? current = node; current is not null; current = HtmlElement.GetFlatTreeParent(current))
        {
            if (current is HtmlElement htmlElement && (htmlElement.HasAttributeWithLocalName("inert") || IsInertSelectButton(htmlElement)))
                return true;

            if (modalDialog is not null && ReferenceEquals(current, modalDialog))
                return false;
        }

        return modalDialog is not null;
    }

    /// <summary>
    /// Whether the element is the button of a select element rendered as a drop-down box, which is inert
    /// (https://html.spec.whatwg.org/multipage/form-elements.html#the-select-element: "Its form submission behavior is
    /// prevented because it is inert"): the user interacts with the select element.
    /// </summary>
    private static bool IsInertSelectButton(HtmlElement element)
        => element.TagId == TagId.Button && element.ParentNode is HtmlSelectElement select && StyleEngine.IsDropDownBox(select) && StyleEngine.IsSelectButton(element);

    // ----------------------------------------------------------------------------------------------------------------
    // The focused area
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the active document of the top-level browsing context of the document, or null when the document has no browsing context.</summary>
    private static Document? GetTopDocument(Document document) => document.BrowsingContext?.Top.Document;

    /// <summary>
    /// The currently focused area of a top-level traversable (https://html.spec.whatwg.org/multipage/interaction.html#currently-focused-area-of-a-top-level-traversable):
    /// an element, or the document whose viewport is focused.
    /// </summary>
    public static Node GetCurrentlyFocusedArea(Document topDocument)
    {
        var candidate = topDocument;
        while (candidate.FocusedArea is { } focused && NavigableContainers.GetContentNavigable(focused) is { } navigable)
        {
            candidate = navigable.Document;
        }

        return (Node?)candidate.FocusedArea ?? candidate;
    }

    /// <summary>The DOM anchor of the currently focused area of the top-level browsing context of the document, or null.</summary>
    public static Node? GetCurrentlyFocusedAreaOf(Document document) => GetTopDocument(document) is { } top ? GetCurrentlyFocusedArea(top) : null;

    /// <summary>The focus chain of a focusable area (https://html.spec.whatwg.org/multipage/interaction.html#focus-chain): the area, its document, the container of the document and its document, up to the top-level document.</summary>
    private static List<Node> GetFocusChain(Node subject)
    {
        var output = new List<Node>();
        var current = subject;
        while (true)
        {
            output.Add(current);
            if (current is Element element)
            {
                current = element.NodeDocument;
            }
            else if (current is Document { BrowsingContext.Container: { } container })
            {
                current = container;
            }
            else
            {
                break;
            }
        }

        return output;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#current-focus-chain-of-a-top-level-traversable</summary>
    private static List<Node> GetCurrentFocusChain(Document topDocument) => GetFocusChain(GetCurrentlyFocusedArea(topDocument));

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#has-focus-steps</summary>
    public static bool HasFocus(Document target)
    {
        // A prerendering traversable never has the system focus (https://wicg.github.io/nav-speculation/prerendering.html#interaction-with-system-focus)
        if (GetTopDocument(target) is not { } candidate || candidate.BrowsingContext is { HasSystemFocus: false })
            return false;

        while (true)
        {
            if (ReferenceEquals(candidate, target))
                return true;

            if (candidate.FocusedArea is { } focused && NavigableContainers.GetContentNavigable(focused) is { } navigable)
            {
                candidate = navigable.Document;
            }
            else
            {
                return false;
            }
        }
    }

    /// <summary>The DocumentOrShadowRoot activeElement getter steps (https://html.spec.whatwg.org/multipage/interaction.html#dom-documentorshadowroot-activeelement).</summary>
    public static Element? GetActiveElement(Node documentOrShadowRoot)
    {
        var document = documentOrShadowRoot as Document ?? documentOrShadowRoot.NodeDocument;
        Node candidate = (Node?)document.FocusedArea ?? document;
        candidate = (Node)EventDispatcher.Retarget(candidate, documentOrShadowRoot)!;
        if (!ReferenceEquals(candidate.GetRoot(), documentOrShadowRoot))
            return null;

        if (candidate is Element element)
            return element;

        return document.Body ?? document.DocumentElement;
    }

    /// <summary>
    /// Whether the element has the focus for the <c>:focus</c> pseudo-class (https://html.spec.whatwg.org/multipage/semantics-other.html#selector-focus):
    /// it is not a navigable container, and it is in the current focus chain of the top-level traversable or it is the
    /// shadow host of an element that has the focus.
    /// </summary>
    public static bool HasFocusForSelector(Element element)
    {
        if (NavigableContainers.GetContentNavigable(element) is not null || !element.IsConnected || GetCurrentlyFocusedAreaOf(element.NodeDocument) is not Element focused)
            return false;

        // https://drafts.csswg.org/css-overflow-5/#scroll-focus-pseudos: "When a ::scroll-button() or ::scroll-marker is focused,
        // none of the focus related pseudo-classes :focus, :focus-visible and :active match on the scrolling container"
        if (ReferenceEquals(focused, element) && element.NodeDocument.FocusedScrollControl is not null)
            return false;

        // The focused element and the shadow hosts of its trees, in the document of the element
        foreach (var node in GetFocusChain(focused))
        {
            if (node is not Element chainElement || !ReferenceEquals(chainElement.NodeDocument, element.NodeDocument) || NavigableContainers.GetContentNavigable(chainElement) is not null)
                continue;

            for (Element? current = chainElement; current is not null; current = (current.GetRoot() as ShadowRoot)?.Host)
            {
                if (ReferenceEquals(current, element))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the element matches <c>:focus-within</c> (https://drafts.csswg.org/selectors/#the-focus-within-pseudo): it, or one
    /// of its descendants in the flat tree, has the focus. The ancestors of an element of the top layer do not match
    /// (https://drafts.csswg.org/selectors-4/#useraction-pseudos: "up to and including the first top layer element"), neither do a
    /// select element and its ancestors for the contents of its open picker, which is rendered in the top layer (as for
    /// <c>:hover</c> and <c>:active</c>).
    /// </summary>
    public static bool HasFocusWithin(Element element)
    {
        if (!element.IsConnected || GetCurrentlyFocusedAreaOf(element.NodeDocument) is not Element focused)
            return false;

        foreach (var node in GetFocusChain(focused))
        {
            if (node is not Element chainElement || !ReferenceEquals(chainElement.NodeDocument, element.NodeDocument) || NavigableContainers.GetContentNavigable(chainElement) is not null)
                continue;

            if (!UserActionStates.IsInSelectPopover(element, chainElement) && UserActionStates.IsFlatTreeInclusiveAncestor(element, chainElement))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the element matches <c>:focus-visible</c> (https://drafts.csswg.org/selectors/#the-focus-visible-pseudo): it is
    /// the focused element (not a shadow host of its trees, nor a navigable container), and the user agent indicates the focus.
    /// </summary>
    /// <remarks>
    /// As in browsers, the focus of an element that supports the keyboard input is indicated (e.g. a button whose type changes
    /// to text), unless it was focused with <c>focusVisible: false</c>.
    /// </remarks>
    public static bool HasFocusVisible(Element element) => element.IsConnected && GetCurrentlyFocusedAreaOf(element.NodeDocument) is Element focused && ReferenceEquals(focused, element)
        && focused.NodeDocument.FocusedScrollControl is null
        && (focused.NodeDocument.FocusVisible || (!focused.NodeDocument.FocusVisibleSuppressed && SupportsKeyboardInput(focused)));

    // ----------------------------------------------------------------------------------------------------------------
    // Focusing steps
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets the focusable area for a focus target that is not a focusable area
    /// (https://html.spec.whatwg.org/multipage/interaction.html#get-the-focusable-area): the viewport of the document of a
    /// document element, the active document of a navigable container, or the focus delegate of a shadow host that
    /// delegates its focus.
    /// </summary>
    public static Node? GetFocusableArea(Node focusTarget, FocusTrigger trigger)
    {
        if (focusTarget is Document document)
            return IsFocusableArea(document) ? document : null;

        if (focusTarget is not Element element)
            return null;

        if (ReferenceEquals(element, element.NodeDocument.DocumentElement) && element.IsConnected)
            return IsFocusableArea(element.NodeDocument) ? element.NodeDocument : null;

        // The document of an inert navigable container is not inert, but the container cannot be focused
        if (NavigableContainers.GetContentNavigable(element) is { } navigable)
            return IsInert(element) ? null : navigable.Document;

        if (element.AttachedShadowRoot is { DelegatesFocus: true })
        {
            if (GetCurrentlyFocusedAreaOf(element.NodeDocument) is Element focusedElement && element.IsShadowIncludingInclusiveAncestorOf(focusedElement))
                return focusedElement;

            return GetFocusDelegate(element, trigger);
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#focus-delegate</summary>
    public static Node? GetFocusDelegate(Element focusTarget, FocusTrigger trigger)
    {
        if (focusTarget.AttachedShadowRoot is { DelegatesFocus: false })
            return null;

        Node whereToLook = focusTarget.AttachedShadowRoot is { } shadowRoot ? shadowRoot : focusTarget;
        if (GetAutofocusDelegate(whereToLook, trigger) is { } autofocusDelegate)
            return autofocusDelegate;

        var isDialog = focusTarget is HtmlDialogElement;
        foreach (var descendant in whereToLook.Descendants())
        {
            if (descendant is not Element element)
                continue;

            if (isDialog && IsSequentiallyFocusable(element))
                return element;

            if (!isDialog && IsFocusableArea(element))
                return element;

            if (GetFocusableArea(element, trigger) is { } focusableArea)
                return focusableArea;
        }

        return null;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#autofocus-delegate</summary>
    public static Node? GetAutofocusDelegate(Node focusTarget, FocusTrigger trigger)
    {
        foreach (var descendant in focusTarget.Descendants())
        {
            if (descendant is not Element element || !element.HasAttributeWithLocalName("autofocus"))
                continue;

            var focusableArea = IsFocusableArea(element) ? element : GetFocusableArea(element, trigger);
            if (focusableArea is null)
                continue;

            // Every focusable area is click focusable
            return focusableArea;
        }

        return null;
    }

    /// <summary>
    /// The focusing steps (https://html.spec.whatwg.org/multipage/interaction.html#focusing-steps) for an element, a
    /// document (its viewport, or the navigable of the document), with an optional fallback target.
    /// </summary>
    /// <returns>Whether the target (or the fallback target) is now the currently focused area.</returns>
    public static bool RunFocusingSteps(Node newFocusTarget, Node? fallbackTarget = null, FocusTrigger trigger = FocusTrigger.Other, bool? focusVisible = null)
    {
        Node? target = newFocusTarget;
        if (!IsFocusableArea(target))
        {
            target = GetFocusableArea(target, trigger);
        }

        if (target is null)
        {
            if (fallbackTarget is null)
                return false;

            target = fallbackTarget;
        }

        if (target is Element container && NavigableContainers.GetContentNavigable(container) is { } navigable)
        {
            // The document of an inert navigable container is not inert, but the container cannot be focused
            if (IsInert(container))
                return false;

            target = navigable.Document;
        }

        if (!IsFocusableArea(target) || IsInert(target))
            return false;

        var document = target as Document ?? target.NodeDocument;
        if (GetTopDocument(document) is not { } topDocument)
            return false;

        if (ReferenceEquals(target, GetCurrentlyFocusedArea(topDocument)))
        {
            // The scroll container of the focused scroll navigation control is focused: another control, or the element itself
            // when it is a focusable area
            if (target is Element { NodeDocument: var targetDocument } targetElement)
            {
                if (targetDocument.PendingFocusedScrollControl is { } pending)
                {
                    targetDocument.FocusedScrollControl = pending;
                }
                else if (targetDocument.FocusedScrollControl is not null && IsFocusableElement(targetElement))
                {
                    targetDocument.FocusedScrollControl = null;
                }
            }

            return true;
        }

        // https://html.spec.whatwg.org/multipage/interaction.html#focusing-steps: the focus changed during the ongoing navigation
        if (document.RelevantWindow?.NavigationIfCreated is { OngoingNavigateEvent: not null } navigation)
        {
            navigation.FocusChangedDuringOngoingNavigation = true;
        }

        var oldChain = GetCurrentFocusChain(topDocument);
        var newChain = GetFocusChain(target);
        RunFocusUpdateSteps(topDocument, oldChain, newChain, focusVisible, trigger);
        if (target is Element element && ReferenceEquals(GetCurrentlyFocusedArea(topDocument), element))
        {
            UpdateSelectionOnFocus(element);
        }

        return true;
    }

    /// <summary>
    /// As in browsers (unspecified), focusing a text control collapses the selection of its document before the control
    /// (the caret is in the value of the control), and focusing an editing host that does not contain the selection puts
    /// the caret at the start of its content.
    /// </summary>
    private static void UpdateSelectionOnFocus(Element element)
    {
        if (element is HtmlTextAreaElement || (element is HtmlInputElement && HtmlElementStates.GetInputType(element) is "text" or "search" or "url" or "tel" or "email" or "password" or "number"))
        {
            if (element.ParentNode is { } parent && element.NodeDocument.GetSelection() is { } selection)
            {
                selection.Collapse(parent, (uint)element.GetIndex());
            }
        }
        else if (IsEditingHost(element) && element.NodeDocument.GetSelection() is { } selection)
        {
            if (selection.RangeCount > 0 && selection.GetRangeAt(0) is { } range && element.IsShadowIncludingInclusiveAncestorOf(range.StartContainer))
                return;

            Node caret = element;
            for (var node = element.FirstChild; node is not null; node = node.GetNextInTreeOrder(element))
            {
                if (node is Text)
                {
                    caret = node;
                    break;
                }
            }

            selection.Collapse(caret, 0);
        }
    }

    /// <summary>
    /// The focusing steps for the navigable of a document (<c>window.focus()</c>, https://html.spec.whatwg.org/multipage/interaction.html#dom-window-focus).
    /// Not as in the spec, which focuses the viewport of the document, but as in the browsers: the document becomes the focused
    /// document and keeps its focused element, unless the element is a navigable container (the focus then leaves its content
    /// navigable for the viewport of the document) or is no longer a focusable area.
    /// </summary>
    public static void FocusNavigable(Document document)
    {
        if (document.FocusedArea is { IsConnected: true } focused && NavigableContainers.GetContentNavigable(focused) is null && IsFocusableArea(focused) && !IsInert(focused))
        {
            Focus(focused, focusVisible: document.FocusVisible);
            return;
        }

        RunFocusingSteps(document);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#unfocusing-steps</summary>
    /// <remarks>
    /// As in the browsers, the steps also unfocus the focused element when it is not a focusable area anymore (e.g. its tabindex
    /// attribute was removed, and the focus fixup rule has not run yet); the specification returns when "old focus target is
    /// not a focusable area".
    /// </remarks>
    public static void RunUnfocusingSteps(Element oldFocusTarget)
    {
        if (GetTopDocument(oldFocusTarget.NodeDocument) is not { } topDocument)
            return;

        Node target = oldFocusTarget;
        var focused = GetCurrentlyFocusedArea(topDocument);
        if (oldFocusTarget.AttachedShadowRoot is { DelegatesFocus: true } shadowRoot && shadowRoot.IsShadowIncludingInclusiveAncestorOf(focused))
        {
            target = focused;
        }

        if (IsInert(target))
            return;

        var oldChain = GetFocusChain(focused);
        if (!oldChain.Contains(target) || (!IsFocusableArea(target) && !ReferenceEquals(target, focused)))
            return;

        // The top-level browsing context has the system focus: the viewport of the top-level document is focused. As in
        // browsers, the sequential focus navigation continues from the element.
        RunFocusingSteps(oldChain[^1]);
        if (target is Element element)
        {
            element.NodeDocument.SequentialFocusNavigationStartingPoint = new(element);
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#focus-update-steps</summary>
    /// <remarks>
    /// <para>
    /// As in browsers, the steps stop when an event handler changes the focus (the focus change of the handler wins). Whether
    /// the new focused element indicates its focus (<c>:focus-visible</c>) is decided before the focus events are fired, by
    /// <paramref name="focusVisible"/>, or by <see cref="ShouldIndicateFocus"/> when it is null.
    /// </para>
    /// <para>
    /// Not as in the spec, but as in the browsers (and the WPT tests of the focus directory): the <c>blur</c> and <c>focus</c>
    /// events of the windows follow the focused document (the document of the focused area), not the focus chain. The window
    /// of a document gets <c>blur</c> when the focus moves to another document, even to the document of one of its nested
    /// browsing contexts, and <c>focus</c> when the focus moves to it from another document; the windows of the other
    /// documents of the chains get no event.
    /// </para>
    /// </remarks>
    private static void RunFocusUpdateSteps(Document topDocument, List<Node> oldChain, List<Node> newChain, bool? focusVisible, FocusTrigger trigger)
    {
        var version = ++topDocument.FocusChangeVersion;

        // As in the browsers, the focus moves without focus events while the top-level browsing context does not have the
        // system focus: they are fired when it gets the system focus (see OnSystemFocusChanged)
        var fireEvents = topDocument.BrowsingContext is not { HasSystemFocus: false };
        var newDocuments = newChain.OfType<Document>().ToList();
        var oldFocusedDocument = oldChain.Count > 0 ? oldChain[0] as Document ?? oldChain[0].NodeDocument : null;
        var newFocusedDocument = newChain.Count > 0 ? newChain[0] as Document ?? newChain[0].NodeDocument : null;
        var focusedDocumentChanged = !ReferenceEquals(oldFocusedDocument, newFocusedDocument);
        var newFocusIsElement = newChain.Count > 0 && newChain[0] is Element;

        // The documents of the new focus chain are the most recently focused ones (https://w3c.github.io/ServiceWorker/#clients-matchall)
        var focusOrder = Interlocked.Increment(ref s_focusOrder);
        foreach (var focusedChainDocument in newDocuments)
        {
            focusedChainDocument.LastFocusOrder = focusOrder;
        }

        while (oldChain.Count > 0 && newChain.Count > 0 && ReferenceEquals(oldChain[^1], newChain[^1]))
        {
            oldChain.RemoveAt(oldChain.Count - 1);
            newChain.RemoveAt(newChain.Count - 1);
        }

        var oldLast = oldChain.Count > 0 ? oldChain[^1] : null;
        var newLast = newChain.Count > 0 ? newChain[^1] : null;

        // The window of the old focused document gets blur after the focused element of the document: the document is not
        // in the old chain when it stays in the focus chain (the focus moves into one of its nested browsing contexts)
        if (focusedDocumentChanged && oldFocusedDocument is not null && !oldChain.Contains(oldFocusedDocument))
        {
            oldChain.Insert(oldChain.Count > 0 && ReferenceEquals(oldChain[0].NodeDocument, oldFocusedDocument) ? 1 : 0, oldFocusedDocument);
        }

        foreach (var entry in oldChain)
        {
            // The windows of the other documents get no blur event
            if (entry is Document && !(focusedDocumentChanged && ReferenceEquals(entry, oldFocusedDocument)))
                continue;

            // The changes the user made to the value of a text control while it was focused are committed
            if (entry is HtmlInputElement or HtmlTextAreaElement)
            {
                ((HtmlElement)entry).CommitUserEdits();
                if (topDocument.FocusChangeVersion != version)
                    return;
            }

            // As in browsers, the element is no longer the focused area of its document when the blur event is fired, unless
            // the focus moves to the viewport of another document (window.focus(): the document keeps its focused area, which
            // is focused again when the document gets the focus back). A navigable container is only the
            // focused area of its document while the focus is in its content navigable: the active element of the ancestor
            // documents is no longer the container when the focus leaves it.
            if (entry is Element element && ReferenceEquals(element.NodeDocument.FocusedArea, element)
                && (newDocuments.Contains(element.NodeDocument) || newFocusIsElement || NavigableContainers.GetContentNavigable(element) is not null))
            {
                element.NodeDocument.FocusedArea = null;
            }

            if (fireEvents)
            {
                var relatedTarget = ReferenceEquals(entry, oldLast) && entry is Element && newLast is Element ? newLast : null;
                FireFocusEvents(entry, "blur", "focusout", relatedTarget);
                if (topDocument.FocusChangeVersion != version)
                    return;
            }
        }

        // As in the browsers, the caret is placed in an editing host focused by script or by the keyboard when the selection is
        // not in it, before the focus events
        if (trigger != FocusTrigger.Click && newChain.Count > 0 && newChain[0] is HtmlElement { IsConnected: true } focusedHost && Editing.IsEditingHost(focusedHost))
        {
            Editing.PlaceCaretAtStart(focusedHost);
            if (topDocument.FocusChangeVersion != version)
                return;
        }

        // The window of the new focused document gets focus before the focused element of the document: the document is not
        // in the new chain when it was in the focus chain (the focus moves out of one of its nested browsing contexts)
        if (fireEvents && focusedDocumentChanged && newFocusedDocument is not null && !newChain.Contains(newFocusedDocument))
        {
            FireFocusEvents(newFocusedDocument, "focus", "focusin", relatedTarget: null);
            if (topDocument.FocusChangeVersion != version)
                return;
        }

        for (var i = newChain.Count - 1; i >= 0; i--)
        {
            var entry = newChain[i];
            if (entry is Element element)
            {
                // An event handler of the blur events removed the element (the focus fixup rule then focuses the viewport)
                if (!element.IsConnected)
                    return;

                var document = element.NodeDocument;
                if (i == 0)
                {
                    document.FocusVisible = focusVisible ?? ShouldIndicateFocus(element, trigger);
                    document.FocusVisibleSuppressed = focusVisible == false;
                }

                document.FocusedArea = element;
                document.SequentialFocusNavigationStartingPoint = null;
            }
            else if (entry is Document document)
            {
                document.FocusedArea = null;
            }

            // The windows of the other documents get no focus event
            if (fireEvents && (entry is not Document || (focusedDocumentChanged && ReferenceEquals(entry, newFocusedDocument))))
            {
                var relatedTarget = ReferenceEquals(entry, newLast) && entry is Element && oldLast is Element ? oldLast : null;
                FireFocusEvents(entry, "focus", "focusin", relatedTarget);
                if (topDocument.FocusChangeVersion != version)
                    return;
            }

            // https://w3c.github.io/clipboard-apis/#document-focus-steps: the document gained the focus
            if (fireEvents && entry is Document focusedDocument && focusedDocument.RelevantWindow is { } focusedWindow && ReferenceEquals(focusedWindow.Document, focusedDocument))
            {
                focusedWindow.NavigatorOrNull?.ClipboardOrNull?.OnDocumentFocused();
                if (topDocument.FocusChangeVersion != version)
                    return;
            }
        }
    }

    /// <summary>
    /// The top-level traversable gains or loses the system focus (https://html.spec.whatwg.org/multipage/interaction.html#tlbc-system-focus):
    /// the focus update steps with an empty chain. The focused areas of the documents do not change. As in the browsers, the
    /// focus events are only fired at the focused element and at the window of its document (not at the navigable containers
    /// of the focus chain nor at the windows of the ancestor documents).
    /// </summary>
    public static void OnSystemFocusChanged(Document topDocument, bool gained)
    {
        var version = ++topDocument.FocusChangeVersion;
        var focused = GetCurrentlyFocusedArea(topDocument);
        var focusedDocument = focused as Document ?? focused.NodeDocument;
        if (gained)
        {
            FireFocusEvents(focusedDocument, "focus", "focusin", relatedTarget: null);
            if (topDocument.FocusChangeVersion != version)
                return;

            if (focusedDocument.RelevantWindow is { } window && ReferenceEquals(window.Document, focusedDocument))
            {
                window.NavigatorOrNull?.ClipboardOrNull?.OnDocumentFocused();
                if (topDocument.FocusChangeVersion != version)
                    return;
            }

            if (focused is Element { IsConnected: true } element)
            {
                FireFocusEvents(element, "focus", "focusin", relatedTarget: null);
            }

            return;
        }

        if (focused is Element focusedElement)
        {
            // The changes the user made to the value of a text control while it was focused are committed
            if (focusedElement is HtmlInputElement or HtmlTextAreaElement)
            {
                ((HtmlElement)focusedElement).CommitUserEdits();
                if (topDocument.FocusChangeVersion != version)
                    return;
            }

            FireFocusEvents(focusedElement, "blur", "focusout", relatedTarget: null);
            if (topDocument.FocusChangeVersion != version)
                return;
        }

        FireFocusEvents(focusedDocument, "blur", "focusout", relatedTarget: null);
    }

    /// <summary>
    /// Fires a focus event (https://html.spec.whatwg.org/multipage/interaction.html#fire-a-focus-event) at an element or at
    /// the window of a document, followed by the focusin or focusout event of the UI Events specification
    /// (https://w3c.github.io/uievents/#event-type-focusin) at an element.
    /// </summary>
    private static void FireFocusEvents(Node entry, string type, string bubblingType, Node? relatedTarget)
    {
        EventTarget? target = entry switch
        {
            Element element => element,
            Document document => document.RelevantWindow is { } window && ReferenceEquals(window.Document, document) ? window : null,
            _ => null,
        };

        if (target is null)
            return;

        var view = target as Window ?? entry.NodeDocument.DefaultView;
        var focusEvent = new FocusEvent(type, new FocusEventInit { RelatedTarget = relatedTarget, View = view, Composed = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(focusEvent, target);
        if (entry is Element)
        {
            var bubblingEvent = new FocusEvent(bubblingType, new FocusEventInit { RelatedTarget = relatedTarget, View = view, Bubbles = true, Composed = true }) { IsTrusted = true };
            EventDispatcher.Dispatch(bubblingEvent, target);
        }
    }

    /// <summary>
    /// Whether the user agent indicates the focus of an element focused by script (the heuristic of the browsers for
    /// <c>:focus-visible</c>): the focus is indicated after a keyboard interaction, and for the elements that support the
    /// keyboard input (text controls, editing hosts).
    /// </summary>
    public static bool ShouldIndicateFocus(Element element, FocusTrigger trigger)
    {
        if (SupportsKeyboardInput(element))
            return true;

        if (trigger == FocusTrigger.Click)
            return false;

        return GetTopDocument(element.NodeDocument) is not { LastFocusWasPointer: true } topDocument
            || (topDocument.LastPointerFocusDocument is { } clickedDocument && !ReferenceEquals(clickedDocument, element.NodeDocument));
    }

    private static bool SupportsKeyboardInput(Element element)
    {
        if (element is HtmlTextAreaElement || IsEditingHost(element) || element is HtmlSelectElement)
            return true;

        return element is HtmlInputElement && HtmlElementStates.GetInputType(element) is "text" or "search" or "url" or "tel" or "email" or "password" or "number"
            or "date" or "month" or "week" or "time" or "datetime-local";
    }

    /// <summary>
    /// The focusing steps run by a focus API (focus(), dialogs, popovers, autofocus): the focus is indicated as decided by
    /// <paramref name="focusVisible"/>, or by <see cref="ShouldIndicateFocus"/> when it is null.
    /// </summary>
    public static void Focus(Node target, bool? focusVisible = null, FocusTrigger trigger = FocusTrigger.Other)
    {
        var document = target as Document ?? target.NodeDocument;
        var topDocument = GetTopDocument(document);
        var version = topDocument?.FocusChangeVersion;
        if (!RunFocusingSteps(target, fallbackTarget: null, trigger, focusVisible))
            return;

        // When the focus moved, the focus update steps decided whether the focus is indicated (an event handler may have
        // moved the focus again); otherwise the target was already focused
        if (topDocument is not null && topDocument.FocusChangeVersion == version && GetCurrentlyFocusedArea(topDocument) is Element focused)
        {
            focused.NodeDocument.FocusVisible = focusVisible ?? ShouldIndicateFocus(focused, trigger);
            focused.NodeDocument.FocusVisibleSuppressed = focusVisible == false;
            focused.NodeDocument.IncrementElementStateVersion();
        }
    }

    /// <summary>
    /// Focuses a scroll navigation control (https://drafts.csswg.org/css-overflow-5/#scroll-focus): the focusing steps run for
    /// its scroll container, the DOM anchor of the control ("the activeElement is the scroll container the control is associated
    /// with"), which is the focused area of its document while the control is focused; <c>:focus</c> then matches the
    /// pseudo-element, not the scroll container.
    /// </summary>
    public static void FocusScrollControl(ScrollControlReference control, bool? focusVisible, FocusTrigger trigger)
    {
        var document = control.Scroller.NodeDocument;
        if (!ScrollControlActions.IsFocusable(control))
            return;

        document.PendingFocusedScrollControl = control;
        try
        {
            Focus(control.Scroller, focusVisible, trigger);
        }
        finally
        {
            document.PendingFocusedScrollControl = null;
        }
    }

    /// <summary>The HTMLOrSVGOrMathMLElement focus(options) method steps (https://html.spec.whatwg.org/multipage/interaction.html#dom-focus).</summary>
    public static void FocusElement(Element element, FocusOptions? options)
    {
        if (!AllowFocus(element.NodeDocument))
            return;

        // Not in the spec, but as in the browsers: focus() on a label that is not a focusable area focuses its labeled control
        // (even when the label is inert)
        if (element is HtmlLabelElement { Control: { } control } label && !IsFocusableArea(label) && GetFocusableArea(label, FocusTrigger.Other) is null)
        {
            element = control;
        }

        Focus(element, options?.FocusVisible);

        // "If options["preventScroll"] is false, then scroll a target into view given this, "auto", "center", and "center""
        if (options?.PreventScroll is not true && element.NodeDocument is { BrowsingContext: not null } document && ComputedStyleDeclarations.IsBeingRendered(element))
        {
            document.GetLayoutEngine().ScrollIntoViewWhenLaidOut(element, ScrollLogicalPosition.Center, ScrollLogicalPosition.Center);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Focus fixup and autofocus (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The focus fixup rule (https://html.spec.whatwg.org/multipage/webappapis.html#focus-fixup-rule): when the focused
    /// area of the document is no longer a focusable area (e.g. it is hidden, disabled or inert), the viewport is focused.
    /// </summary>
    public static void RunFocusFixupRule(Document document)
    {
        if (document.FocusedArea is not { } focused)
            return;

        if (focused.IsConnected && ReferenceEquals(focused.NodeDocument, document) && IsFocusableArea(focused))
            return;

        // As in browsers, the sequential focus navigation continues from the element
        document.SequentialFocusNavigationStartingPoint = new(focused);

        // The focusing steps for the viewport fire the blur events when the element is in the current focus chain;
        // otherwise (e.g. the focus is in another document) the viewport becomes the focused area of the document silently
        if (ReferenceEquals(GetCurrentlyFocusedAreaOf(document), focused) && RunFocusingSteps(document))
            return;

        if (ReferenceEquals(document.FocusedArea, focused))
        {
            document.FocusedArea = null;
        }
    }

    /// <summary>
    /// The allow focus steps (https://html.spec.whatwg.org/multipage/interaction.html#allow-focus-steps): the document is
    /// allowed to use the "focus-without-user-activation" policy-controlled feature, whose default allowlist is 'self'
    /// (the documents of the cross-origin iframes are not allowed, unless the <c>allow</c> attribute of the iframe delegates
    /// the feature), or its window has transient activation.
    /// </summary>
    public static bool AllowFocus(Document target)
    {
        if (PermissionsPolicyAlgorithms.IsFeatureEnabled(PolicyControlledFeature.FocusWithoutUserActivation, target, target.Origin, report: false)
            || target.RelevantWindow is { HasTransientActivation: true })
        {
            return true;
        }

        // The violation of the policy is reported
        return PermissionsPolicyAlgorithms.IsFeatureEnabled(PolicyControlledFeature.FocusWithoutUserActivation, target, target.Origin);
    }

    /// <summary>
    /// Whether the active sandboxing flag set of the document has the sandboxed automatic features browsing context flag
    /// (https://html.spec.whatwg.org/multipage/browsers.html#sandboxed-automatic-features-browsing-context-flag): the sandbox
    /// attribute of its iframe does not have the allow-scripts keyword.
    /// </summary>
    private static bool IsSandboxedAutomaticFeatures(Document document)
    {
        if (document.BrowsingContext?.Container is not HtmlIFrameElement iframe || iframe.GetAttributeValue("sandbox") is not { } sandbox)
            return false;

        foreach (var token in sandbox.Split([' ', '\t', '\n', '\f', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("allow-scripts", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>The steps run when an element with an autofocus attribute is inserted into a document (https://html.spec.whatwg.org/multipage/interaction.html#the-autofocus-attribute).</summary>
    public static void OnAutofocusElementInserted(Element element)
    {
        var target = element.NodeDocument;
        if (!target.IsFullyActive || IsSandboxedAutomaticFeatures(target) || !AllowFocus(target) || GetTopDocument(target) is not { } topDocument)
            return;

        if (topDocument.AutofocusProcessed)
            return;

        var candidates = topDocument.AutofocusCandidates;
        candidates.Remove(element);
        candidates.Add(element);
        topDocument.EventLoop?.RequestRenderingOpportunity(topDocument);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interaction.html#flush-autofocus-candidates</summary>
    public static void FlushAutofocusCandidates(Document topDocument)
    {
        if (topDocument.AutofocusProcessed)
            return;

        var candidates = topDocument.AutofocusCandidates;
        if (candidates.Count == 0)
            return;

        if (topDocument.FocusedArea is not null || HtmlElementStates.GetTargetElement(topDocument) is not null)
        {
            candidates.Clear();
            topDocument.AutofocusProcessed = true;
            return;
        }

        while (candidates.Count > 0)
        {
            var element = candidates[0];
            var document = element.NodeDocument;
            if (!document.IsFullyActive || !ReferenceEquals(document.BrowsingContext?.Top, topDocument.BrowsingContext))
            {
                candidates.RemoveAt(0);
                continue;
            }

            // The best candidate is not ready for autofocusing: the candidates are flushed again at the next rendering update
            if (document.HasStyleSheetThatIsBlockingScripts)
                return;

            candidates.RemoveAt(0);
            var hasTargetElement = false;
            for (var navigable = document.BrowsingContext; navigable is not null; navigable = navigable.Parent)
            {
                if (HtmlElementStates.GetTargetElement(navigable.Document) is not null)
                {
                    hasTargetElement = true;
                    break;
                }
            }

            if (hasTargetElement)
                continue;

            var target = IsFocusableArea(element) ? element : GetFocusableArea(element, FocusTrigger.Other);
            if (target is not null)
            {
                candidates.Clear();
                topDocument.AutofocusProcessed = true;
                Focus(target);
            }
        }
    }

    /// <summary>The steps of the dialogs and popovers that empty the autofocus candidates of the top-level document after focusing an element (https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-focusing-steps).</summary>
    private static void SetAutofocusProcessed(Node control)
    {
        var document = control as Document ?? control.NodeDocument;
        if (GetTopDocument(document) is not { } topDocument || !Document.IsSameOrigin(document, topDocument))
            return;

        topDocument.AutofocusCandidates.Clear();
        topDocument.AutofocusProcessed = true;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/interactive-elements.html#dialog-focusing-steps</summary>
    public static void RunDialogFocusingSteps(HtmlDialogElement subject)
    {
        if (!AllowFocus(subject.NodeDocument))
            return;

        Node? control = subject.HasAttributeWithLocalName("autofocus") ? subject : null;
        control ??= GetFocusDelegate(subject, FocusTrigger.Other);
        control ??= subject;
        Focus(control);

        // As in browsers, when the focused element is now inert (blocked by a modal dialog that cannot be focused), the
        // focus fixup rule runs immediately
        if (subject.NodeDocument.FocusedArea is { } focused && IsInert(focused))
        {
            RunFocusFixupRule(subject.NodeDocument);
        }

        SetAutofocusProcessed(control);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/popover.html#popover-focusing-steps</summary>
    public static void RunPopoverFocusingSteps(HtmlElement subject)
    {
        if (subject is HtmlDialogElement dialog)
        {
            RunDialogFocusingSteps(dialog);
            return;
        }

        if (!AllowFocus(subject.NodeDocument))
            return;

        var control = subject.HasAttributeWithLocalName("autofocus") ? subject : GetAutofocusDelegate(subject, FocusTrigger.Other);
        if (control is null)
            return;

        Focus(control);
        SetAutofocusProcessed(control);
    }

    /// <summary>The focused element (the DOM anchor of the currently focused area of the top-level browsing context, when it is an element), for the previously focused element of the dialogs.</summary>
    public static Element? GetFocusedElement(Document document) => GetCurrentlyFocusedAreaOf(document) as Element;
}

/// <summary>
/// The sequential focus navigation (https://html.spec.whatwg.org/multipage/interaction.html#sequential-focus-navigation):
/// the default action of the Tab key, and the order of the focusable areas.
/// </summary>
internal static class SequentialFocusNavigation
{
    /// <summary>
    /// Moves the focus to the next (or previous) sequentially focusable area of the top-level browsing context
    /// (https://html.spec.whatwg.org/multipage/interaction.html#sequential-navigation-search-algorithm). When there is none,
    /// the focus leaves the document (the viewport of the top-level document is focused), as the focus of a browser moves
    /// to its own controls.
    /// </summary>
    public static void Navigate(Document topDocument, bool forward)
    {
        Node startingPoint = FocusModel.GetCurrentlyFocusedArea(topDocument);
        var position = GetStartingPointInside(startingPoint);
        while (true)
        {
            var document = startingPoint as Document ?? startingPoint.NodeDocument;
            Element? candidate;
            if (ScrollControlActions.GetTabStops(document) is { Count: > 0 } tabStops)
            {
                // https://drafts.csswg.org/css-overflow-5/#scroll-focus-order: the scroll navigation controls of the document
                // are in the focus navigation order
                var (element, control) = SearchWithScrollControls(startingPoint, position, forward, tabStops);
                if (control is not null)
                {
                    FocusModel.FocusScrollControl(control, focusVisible: true, FocusTrigger.Other);
                    return;
                }

                candidate = element;
            }
            else
            {
                candidate = position is { } startingPosition ? SearchFrom(startingPoint, startingPosition, forward) : Search(startingPoint, forward);
            }

            position = null;
            if (candidate is not null)
            {
                FocusModel.Focus(candidate, focusVisible: true);
                return;
            }

            document.SequentialFocusNavigationStartingPoint = null;
            if (document.BrowsingContext?.Container is not { } container)
            {
                FocusModel.RunFocusingSteps(document);
                return;
            }

            // The focus continues after the navigable container, in the parent document
            startingPoint = container;
        }
    }

    /// <summary>
    /// The sequential navigation search algorithm of a document that has scroll navigation controls
    /// (https://drafts.csswg.org/css-overflow-5/#scroll-focus-order): "focus navigation between a scroll container and the various
    /// pseudo-elements defined in this section goes in the following order": the markers of its <c>::scroll-marker-group</c> when
    /// it is before, its <c>::scroll-button()</c> pseudo-elements, the scroll container and its contents, the markers of its
    /// group when it is after. The controls are in the navigation order as elements without tabindex placed just before the
    /// scroll container, or just after its contents. Returns the next area: an element, or a control.
    /// </summary>
    private static (Element? Element, ScrollControlReference? Control) SearchWithScrollControls(Node focused, FocusNavigationStartingPoint? startingPoint, bool forward, List<ScrollControlActions.TabStop> tabStops)
    {
        var document = focused as Document ?? focused.NodeDocument;
        var scopes = new FocusNavigationScopes(document);
        var order = scopes.GetSequentialNavigationOrder();
        var positions = scopes.Positions;

        // The areas in the navigation order, with their position in the flat tree (the controls are between two nodes)
        var items = new List<(Element? Element, ScrollControlReference? Control, double Position)>(order.Count + tabStops.Count);
        foreach (var element in order)
        {
            items.Add((element, null, positions.TryGetValue(element, out var range) ? range.Start : -1));
        }

        var firstWithoutTabIndex = order.FindIndex(static element => FocusModel.GetTabIndexValue(element) is null or <= 0);
        if (firstWithoutTabIndex < 0)
        {
            firstWithoutTabIndex = order.Count;
        }

        foreach (var tabStop in tabStops)
        {
            if (!positions.TryGetValue(tabStop.Control.Scroller, out var range))
                continue;

            var position = tabStop.BeforeScroller ? range.Start - 0.5 : range.End + 0.5;
            var index = firstWithoutTabIndex;
            for (var i = items.Count - 1; i >= firstWithoutTabIndex; i--)
            {
                // After the last area that precedes the control (the controls of a scroll container keep their order)
                if (items[i].Position <= position)
                {
                    index = i + 1;
                    break;
                }
            }

            items.Insert(index, (null, tabStop.Control, position));
        }

        int next;
        var focusedControl = focused is Element focusedScroller && document.FocusedScrollControl is { } current && ReferenceEquals(current.Scroller, focusedScroller) ? current : null;
        var focusedIndex = focusedControl is not null ? items.FindIndex(item => item.Control == focusedControl) : -1;
        if (focusedControl is { IsMarker: true } marker && forward && IsInTabsMode(document, marker))
        {
            // https://drafts.csswg.org/css-overflow-5/#scroll-marker-next-focus: in tabs mode, "the next tabindex-ordered focus
            // navigation will focus the scroll target if it is focusable, otherwise, it will find the next focusable element from
            // the scroll target as though it were focused"
            var scrollTarget = marker.Element;
            var afterTarget = false;
            if (ScrollControls.IsColumnMarker(marker.PseudoElement) && document.LayoutEngineOrNull is { } layoutEngine)
            {
                // The scroll target of the scroll marker of a column: the first element of the column, else the end of the container
                var first = layoutEngine.GetFirstElementOfColumn(marker.Element, ScrollControls.GetColumnIndex(marker.PseudoElement));
                afterTarget = first is null;
                scrollTarget = first ?? scrollTarget;
            }

            next = FindNearest(positions.TryGetValue(scrollTarget, out var target) ? (afterTarget ? target.End : target.Start - 0.5) : double.MaxValue, forward: true);

            // The group of the marker follows the contents of its scroll container: the navigation continues after the marker
            if (next >= 0 && items[next].Control is { IsMarker: true } reached && ReferenceEquals(reached.Scroller, marker.Scroller))
            {
                next = focusedIndex >= 0 ? focusedIndex + 1 : -1;
            }
        }
        else if (focusedIndex >= 0)
        {
            next = focusedIndex + (forward ? 1 : -1);
        }
        else if (startingPoint is { } point && positions.TryGetValue(point.Node, out var anchor))
        {
            // The "DOM" selection mechanism from the sequential focus navigation starting point (see SearchNearestInScope)
            next = FindNearest(point.Kind switch
            {
                FocusNavigationStartingPointKind.AfterNode => anchor.End + (forward ? 0 : 1),
                FocusNavigationStartingPointKind.BeforeChildren => anchor.Start + (forward ? 0 : 1),
                FocusNavigationStartingPointKind.BeforeNode => anchor.Start - 0.25,
                _ => anchor.Start,
            }, forward);
        }
        else if (focused is Document)
        {
            next = forward ? 0 : items.Count - 1;
        }
        else if (focused is Element focusedElement && order.Contains(focusedElement))
        {
            next = items.FindIndex(item => ReferenceEquals(item.Element, focusedElement)) + (forward ? 1 : -1);
        }
        else
        {
            next = FindNearest(positions.TryGetValue(focused, out var range) ? range.Start : -1, forward);
        }

        if (next < 0 || next >= items.Count)
            return (null, null);

        var (candidate, control, _) = items[next];
        return control is not null ? (null, control) : (ResolveNavigableContainer(candidate, forward), null);

        // The area nearest after (or before) a position of the flat tree
        int FindNearest(double position, bool forward)
        {
            var best = -1;
            for (var i = 0; i < items.Count; i++)
            {
                var itemPosition = items[i].Position;
                if (forward ? itemPosition > position && (best < 0 || itemPosition < items[best].Position) : itemPosition < position && (best < 0 || itemPosition > items[best].Position))
                {
                    best = i;
                }
            }

            return best;
        }

        static bool IsInTabsMode(Document document, ScrollControlReference marker)
            => document.LayoutEngineOrNull?.BoxTree?.ScrollControls is { } boxes && boxes.Groups.TryGetValue(marker.Scroller, out var group) && group.IsTabs;
    }

    /// <summary>The sequential focus navigation starting point of the document of the focused area, when it is inside the focused area.</summary>
    private static FocusNavigationStartingPoint? GetStartingPointInside(Node focused)
    {
        var document = focused as Document ?? focused.NodeDocument;
        if (document.SequentialFocusNavigationStartingPoint is not { } startingPoint || !startingPoint.Node.IsConnected || !ReferenceEquals(startingPoint.Node.NodeDocument, document))
            return null;

        if (focused is Document)
            return startingPoint;

        // The position after the focused element, or the focused element itself, is not inside the focused element
        var isInside = startingPoint.Kind == FocusNavigationStartingPointKind.BeforeChildren
            ? focused.IsShadowIncludingInclusiveAncestorOf(startingPoint.Node)
            : !ReferenceEquals(focused, startingPoint.Node) && focused.IsShadowIncludingInclusiveAncestorOf(startingPoint.Node);
        return isInside ? startingPoint : null;
    }

    /// <summary>
    /// The sequential navigation search algorithm with the "DOM" selection mechanism from the sequential focus navigation
    /// starting point, which is inside the focused area. As in browsers, when the focused area is an element, the nearest
    /// area inside it is selected; when there is none, the search continues from the focused element (e.g. its next area in
    /// the order of the tabindex values).
    /// </summary>
    private static Element? SearchFrom(Node focused, FocusNavigationStartingPoint startingPoint, bool forward)
    {
        var document = focused as Document ?? focused.NodeDocument;
        var scopes = new FocusNavigationScopes(document);
        var candidate = SearchFromPoint(scopes, startingPoint, forward);
        if (focused is Element focusedElement && scopes.IsSequentiallyFocusable(focusedElement))
        {
            var isInside = candidate is not null
                && scopes.Positions.TryGetValue(candidate, out var position)
                && scopes.Positions.TryGetValue(focusedElement, out var range)
                && position.Start > range.Start
                && position.Start <= range.End;
            if (!isInside)
            {
                candidate = SearchFromElement(scopes, focusedElement, forward, descend: false, includeSelf: false);
            }
        }

        return ResolveNavigableContainer(candidate, forward);
    }

    /// <summary>
    /// The sequential navigation search algorithm with the "DOM" selection mechanism from a position of the flat tree: the
    /// nearest area after (or before) the position in its focus navigation scope, then in the enclosing scopes.
    /// </summary>
    private static Element? SearchFromPoint(FocusNavigationScopes scopes, FocusNavigationStartingPoint point, bool forward)
    {
        if (point is { Kind: FocusNavigationStartingPointKind.Node, Node: Element element })
            return SearchFromElement(scopes, element, forward, descend: true, includeSelf: false);

        var owner = point.Kind == FocusNavigationStartingPointKind.BeforeChildren ? scopes.GetOwnerOfChildren(point.Node) : scopes.GetOwner(point.Node);
        if (owner is null)
            return null;

        if (SearchNearestInScope(scopes, owner, point.Node, point.Kind, forward) is { } candidate)
            return candidate;

        return owner is Element ownerElement ? SearchFromElement(scopes, ownerElement, forward, descend: false, includeSelf: true) : null;
    }

    /// <summary>
    /// The sequential navigation search algorithm from an element (https://html.spec.whatwg.org/multipage/interaction.html#sequential-navigation-search-algorithm):
    /// the area that follows (or precedes) the element in the flattened tabindex-ordered focus navigation scope of the
    /// document. The search is made scope by scope, so it also starts from an element that is not in the navigation order (a
    /// negative tabindex value, an element in the shadow tree of a host with a negative tabindex value, an element that is
    /// not focusable): as in the browsers, the search then continues, in tree order, from the position of the element in its
    /// scope, and, when the scope is exhausted, from the owner of the scope in the enclosing scope.
    /// </summary>
    /// <param name="descend">Whether a forward search starts in the scope owned by the element (the search starts from the element itself, not from the end of its scope).</param>
    /// <param name="includeSelf">Whether the element is the result of a backward search when it is sequentially focusable (the search starts from the start of its scope).</param>
    private static Element? SearchFromElement(FocusNavigationScopes scopes, Element element, bool forward, bool descend, bool includeSelf)
    {
        HashSet<Node>? visited = null;
        while (true)
        {
            if (forward && descend && scopes.IsScopeOwner(element) && scopes.GetFirstArea(scopes.GetTabIndexOrderedScope(element), 0) is { } inner)
                return inner;

            if (!forward && includeSelf && scopes.IsSequentiallyFocusable(element))
                return element;

            if (scopes.GetOwner(element) is not { } owner)
                return null;

            var orderedScope = scopes.GetTabIndexOrderedScope(owner);
            var index = orderedScope.IndexOf(element);
            var candidate = index < 0
                ? SearchNearestInScope(scopes, owner, element, FocusNavigationStartingPointKind.Node, forward)
                : forward ? scopes.GetFirstArea(orderedScope, index + 1) : scopes.GetLastArea(orderedScope, index - 1);
            if (candidate is not null)
                return candidate;

            // The scope is exhausted: the search continues from its owner, in the enclosing scope (the owner precedes its scope)
            if (owner is not Element ownerElement || !(visited ??= new(ReferenceEqualityComparer.Instance)).Add(owner))
                return null;

            element = ownerElement;
            descend = false;
            includeSelf = true;
        }
    }

    /// <summary>
    /// The suitable sequentially focusable area that appears nearest after (or before) a position, among the items of the
    /// tabindex-ordered focus navigation scope of an owner and the scopes they own: the item nearest to the position in the
    /// flat tree order (the order of the sequential focus navigation, where the slotted nodes are at their slot; the reading
    /// flow order for the items of a reading flow), then the items that follow (or precede) it in the scope.
    /// </summary>
    private static Element? SearchNearestInScope(FocusNavigationScopes scopes, Node owner, Node node, FocusNavigationStartingPointKind kind, bool forward)
    {
        // The elements of a reading flow are compared in the reading flow order
        var inReadingFlow = scopes.GetReadingFlowPosition(owner, node) is not null;
        if (GetPosition(node) is not { } anchor)
            return null;

        // The candidates are after the "after" position (forward), or before the "before" position (backward)
        var (after, before) = kind switch
        {
            FocusNavigationStartingPointKind.AfterNode => (anchor.End, anchor.End + 1),
            FocusNavigationStartingPointKind.BeforeChildren => (anchor.Start, anchor.Start + 1),
            FocusNavigationStartingPointKind.BeforeNode => (anchor.Start - 1, anchor.Start),
            _ => (anchor.Start, anchor.Start),
        };

        var orderedScope = scopes.GetTabIndexOrderedScope(owner);
        var best = -1;
        var bestPosition = 0;
        for (var i = 0; i < orderedScope.Count; i++)
        {
            if (GetPosition(orderedScope[i]) is not { } itemPosition)
                continue;

            var position = itemPosition.Start;
            if (forward ? position > after && (best < 0 || position < bestPosition) : position < before && (best < 0 || position > bestPosition))
            {
                best = i;
                bestPosition = position;
            }
        }

        if (best < 0)
            return null;

        return forward ? scopes.GetFirstArea(orderedScope, best) : scopes.GetLastArea(orderedScope, best);

        (int Start, int End)? GetPosition(Node item)
        {
            if (inReadingFlow)
                return scopes.GetReadingFlowPosition(owner, item) is { } index ? (index, index) : null;

            return scopes.Positions.TryGetValue(item, out var position) ? position : null;
        }
    }

    /// <summary>
    /// The steps run before a node is removed or moved: when it contains the sequential focus navigation starting point or
    /// the focused area (unless it is moved), the starting point becomes the position of the node in the flat tree, so the
    /// navigation continues from there, as in browsers.
    /// </summary>
    public static void OnRemoving(Document document, Node node, bool isMove)
    {
        var startingPoint = document.SequentialFocusNavigationStartingPoint;
        bool update;
        if (isMove)
        {
            // A moved node keeps the focus, and the positions inside it move with it
            update = startingPoint is { Kind: FocusNavigationStartingPointKind.AfterNode } position && ReferenceEquals(position.Node, node);
        }
        else
        {
            update = (startingPoint is { } position && node.IsShadowIncludingInclusiveAncestorOf(position.Node))
                || (document.FocusedArea is { } focused && node.IsShadowIncludingInclusiveAncestorOf(focused));
        }

        if (!update)
            return;

        Node? previous;
        Node? parent;
        if (node.Slottable?.AssignedSlot is { } slot)
        {
            var index = slot.AssignedNodesList.IndexOf(node);
            previous = index > 0 ? slot.AssignedNodesList[index - 1] : null;
            parent = slot;
        }
        else
        {
            previous = node.PreviousSibling;
            parent = node.ParentNode is ShadowRoot shadowRoot ? shadowRoot.Host : node.ParentNode;
        }

        document.SequentialFocusNavigationStartingPoint = previous is not null
            ? new(previous, FocusNavigationStartingPointKind.AfterNode)
            : parent is not null ? new(parent, FocusNavigationStartingPointKind.BeforeChildren) : null;
    }

    /// <summary>The sequential navigation search algorithm (https://html.spec.whatwg.org/multipage/interaction.html#sequential-navigation-search-algorithm).</summary>
    private static Element? Search(Node startingPoint, bool forward)
    {
        var document = startingPoint as Document ?? startingPoint.NodeDocument;
        var scopes = new FocusNavigationScopes(document);
        Element? candidate;
        if (startingPoint is Element element)
        {
            candidate = SearchFromElement(scopes, element, forward, descend: true, includeSelf: false);
        }
        else
        {
            // The viewport, or a navigable: the first (or last) sequentially focusable area of the document
            var orderedScope = scopes.GetTabIndexOrderedScope(document);
            candidate = forward ? scopes.GetFirstArea(orderedScope, 0) : scopes.GetLastArea(orderedScope, orderedScope.Count - 1);
        }

        return ResolveNavigableContainer(candidate, forward);
    }

    /// <summary>When the candidate is a navigable container, the search continues in its content navigable.</summary>
    private static Element? ResolveNavigableContainer(Element? candidate, bool forward)
    {
        if (candidate is not null && NavigableContainers.GetContentNavigable(candidate) is { } navigable)
            return Search(navigable.Document, forward) ?? Search(candidate, forward);

        return candidate;
    }
}

/// <summary>
/// The default actions of the user agent for the trusted events of the user interactions (the events dispatched by the
/// host to simulate the input devices): a press of the primary mouse button focuses the clicked focusable area, the Tab
/// key moves the focus sequentially, the arrow keys, Home and End move the caret in the editable content and the text
/// controls (<see cref="CaretMovementKeys"/>), the editing shortcuts select all, undo, redo or format (<see cref="Editing.ProcessShortcut"/>),
/// the access keys trigger the commands of their elements, the Escape key is a close
/// request, and the Enter key activates the buttons and links, or submits the form of a text field (implicit submission).
/// </summary>
internal static class UserAgentDefaultActions
{
    /// <summary>
    /// Runs the steps of the user agent that precede the dispatch of a trusted event (as in browsers, the listeners of the
    /// event observe the new state): the update of the state of the pointing device (<c>:hover</c> and <c>:active</c>), the
    /// keyboard modality of <c>:focus-visible</c>, the light dismiss of the popovers and of the dialogs, and the time of the
    /// excluding inputs of the layout shifts.
    /// </summary>
    public static void BeforeDispatch(Event @event, Node target)
    {
        target.NodeDocument.NotifyLayoutShiftInput(@event);
        if (@event is MouseEvent mouseEvent)
        {
            UserActionStates.BeforeDispatch(mouseEvent, target);
        }

        // https://drafts.csswg.org/selectors/#the-focus-visible-pseudo: when the user interacts with the keyboard, the focused
        // element indicates its focus, and so do the elements focused by script. As in browsers, the keyboard shortcuts
        // (the modifier keys, and the keys pressed with Control, Alt or Meta) are not keyboard interactions.
        if (@event is KeyboardEvent { Type: "keydown", CtrlKey: false, AltKey: false, MetaKey: false, Key: not ("Shift" or "Control" or "Alt" or "Meta") }
            && target.NodeDocument.BrowsingContext?.Top.Document is { } topDocument)
        {
            topDocument.LastFocusWasPointer = false;
            if (FocusModel.GetCurrentlyFocusedArea(topDocument) is Element focused && !focused.NodeDocument.FocusVisible)
            {
                focused.NodeDocument.FocusVisible = true;
                focused.NodeDocument.IncrementElementStateVersion();
            }
        }

        if (@event is PointerEvent { Type: "pointerdown" or "pointerup" } pointerEvent && target.IsConnected)
        {
            target.NodeDocument.RunLightDismissActivities(pointerEvent, target);
        }
    }

    /// <summary>Runs the default action of a trusted event, after its dispatch.</summary>
    public static void Run(Event @event, Node target, bool canceled)
    {
        var topDocument = target.NodeDocument.BrowsingContext?.Top.Document;
        if (topDocument is null)
            return;

        // https://w3c.github.io/pointerlock/#dfn-default-unlock-gesture: the Escape key exits the pointer lock, whether the
        // page cancels the event or not ("a default unlock gesture must always be available"). It is also a close request,
        // which exits fullscreen. While the keyboard lock of the fullscreen element is active, the key is held to exit both.
        if (@event is KeyboardEvent { Type: "keydown", Key: "Escape" } && topDocument.BrowsingContext is { PointerLockTarget: not null } pointerLockContext
            && !FullscreenModel.IsKeyboardLockActive(topDocument))
        {
            PointerLockModel.ExitByUserAgent(pointerLockContext);
        }

        if (@event is PointerEvent pointerEvent)
        {
            HtmlInputElement.RunSliderDefaultAction(pointerEvent, target, canceled, topDocument);
            HtmlInputElement.RunSpinButtonDefaultAction(pointerEvent, target, canceled);
        }

        switch (@event)
        {
            case MouseEvent { Type: "mousedown", ScrollbarPress: { } scrollbarPress }:
                // A press on a scrollbar scrolls (see UserInput.Scrollbars.cs): it does not change the selection. As in the
                // browsers, it focuses as a press on the scroll container does, but a press on a scrollbar of the viewport
                // keeps the focus where it is
                if (!canceled && !scrollbarPress.IsViewport)
                {
                    FocusClickedArea(topDocument, target);
                }

                break;

            case MouseEvent { Type: "mousedown" } mouseEvent:
                // A press on a scroll navigation control focuses the control (a pseudo-element of the target)
                // A press on a select element opens its picker, which focuses an option, or closes it, which focuses the select element
                if (!canceled && mouseEvent.Button == 0 && !ScrollControlActions.OnMouseDown(mouseEvent, target) && !SelectActions.OnMouseDown(mouseEvent, target))
                {
                    // The press puts the caret at the position of the pointer, or starts a selection, then focuses: as in the
                    // browsers, the listeners of the focus events may change the new selection (e.g. select the value of a
                    // text control), and the caret of a focused editing host is not moved to its start
                    PointerSelection.OnMouseDown(topDocument, mouseEvent, target);
                    FocusClickedArea(topDocument, target);
                    if (topDocument.PointerSelectionState is { } selectionState)
                    {
                        selectionState.FocusedArea = selectionState.Document.FocusedArea;
                    }
                }
                else if (!canceled && mouseEvent.Button is 1 or 2)
                {
                    // As in the browsers, a press of the middle or of the secondary button also moves the caret and the focus
                    PointerSelection.OnMouseDown(topDocument, mouseEvent, target);
                    FocusClickedArea(topDocument, target);
                }

                break;

            case MouseEvent { Type: "mousemove" } moveEvent when topDocument.PointerSelectionState is not null:
                PointerSelection.OnMouseMove(topDocument, moveEvent, target);
                break;

            case MouseEvent { Type: "mouseup" } mouseEvent:
                if (mouseEvent.Button == 0 && topDocument.PointerSelectionState is not null)
                {
                    PointerSelection.OnMouseUp(topDocument);
                }

                SelectActions.OnMouseUp(mouseEvent, target, canceled);
                break;

            // https://fullscreen.spec.whatwg.org/#keyboard-locking: while the keyboard lock of the fullscreen element is active,
            // the Escape key is given to the page (it is not a close request), and holding it exits fullscreen, whether the
            // page cancels the events or not
            case KeyboardEvent { Type: "keydown" or "keyup", Key: "Escape" } escapeEvent when FullscreenModel.IsKeyboardLockActive(topDocument):
                FullscreenModel.OnLockedEscapeKey(topDocument, isDown: escapeEvent.Type == "keydown");
                break;

            case KeyboardEvent { Type: "keydown" } keyboardEvent:
                // The key combination of an assigned access key triggers the action of the command of its element
                if (!canceled && AccessKeys.Process(target.NodeDocument, keyboardEvent))
                    break;

                if (SelectActions.OnKeyDown(keyboardEvent, target, canceled))
                {
                    // The key opened or closed the picker of a select element, or picked or focused one of its options
                    topDocument.SpaceKeyActivationTarget = null;
                }
                else if (!canceled && keyboardEvent.Key == "Tab" && !keyboardEvent.CtrlKey && !keyboardEvent.AltKey && !keyboardEvent.MetaKey)
                {
                    // As in browsers (unspecified), moving the focus with the keyboard ends the activation of the element
                    // pressed by the space key or by the pointing device (it no longer matches :active)
                    topDocument.SpaceKeyActivationTarget = null;
                    topDocument.ActivelyPointedElement = null;
                    target.NodeDocument.ActivelyPointedElement = null;
                    SequentialFocusNavigation.Navigate(topDocument, forward: !keyboardEvent.ShiftKey);
                }
                else if (!canceled && ScrollControlActions.OnKeyDown(keyboardEvent, target))
                {
                    // Enter, the space key or an arrow key activated the focused scroll navigation control
                    topDocument.SpaceKeyActivationTarget = null;
                }
                else if (!canceled && HtmlInputElement.RunNumberKeyDefaultAction(keyboardEvent, target))
                {
                    // The up and down arrow keys stepped the value of the number control
                }
                else if (!canceled && HtmlInputElement.RunRadioKeyDefaultAction(keyboardEvent, target))
                {
                    // An arrow key focused and checked the next or previous radio button of the group
                }
                else if (!canceled && CaretMovementKeys.Process(target, keyboardEvent))
                {
                    // The arrow keys, Home and End moved the caret of the editable content or of the text control
                }
                else if (!canceled && Editing.ProcessShortcut(target, keyboardEvent))
                {
                    // Select All, Undo, Redo, or a formatting shortcut of the editable content
                }
                else if (!canceled && keyboardEvent.Key != " " && ScrollByKey(target, keyboardEvent))
                {
                    // The arrow keys, PageUp, PageDown, Home and End scrolled
                }
                else if (keyboardEvent.Key == "Escape" && target.NodeDocument.CloseInputPickers())
                {
                    // The open picker of an input element handles the Escape key, as in the browsers: it closes, and it is
                    // not a close request of the document
                }
                else if (!canceled && keyboardEvent.Key == "Escape")
                {
                    // A close request (https://html.spec.whatwg.org/multipage/interaction.html#close-requests)
                    target.NodeDocument.ProcessCloseRequest();
                }
                else if (!canceled && keyboardEvent.Key == "Enter" && target is Element enterTarget && IsActivatedByEnter(enterTarget))
                {
                    FireSyntheticClick(enterTarget);
                }
                else if (!canceled && keyboardEvent.Key == "Enter" && target is HtmlInputElement { FormOwner: { } form } input && BlocksImplicitSubmission(input))
                {
                    SubmitImplicitly(form);
                }
                else if (keyboardEvent.Key == " ")
                {
                    // The space key activates the element when it is released, unless its keydown event was canceled or a
                    // listener disabled the element
                    topDocument.SpaceKeyActivationTarget = !canceled && target is Element spaceTarget && IsActivatedBySpace(spaceTarget) && !HtmlElementStates.IsActuallyDisabled(spaceTarget) ? spaceTarget : null;

                    // The space key scrolls by a page when it does not activate nor edit the target
                    if (!canceled && topDocument.SpaceKeyActivationTarget is null)
                    {
                        ScrollByKey(target, keyboardEvent);
                    }
                }

                break;

            case KeyboardEvent { Type: "keyup", Key: " " }:
                if (ReferenceEquals(topDocument.SpaceKeyActivationTarget, target) && target is Element activationTarget)
                {
                    topDocument.SpaceKeyActivationTarget = null;
                    if (!canceled)
                    {
                        FireSyntheticClick(activationTarget);
                    }
                }

                break;
        }
    }

    /// <summary>
    /// The scroll of the keys (the arrow keys, PageUp, PageDown, Home, End and the space key), as in the browsers: the nearest
    /// scrolling box of the target of the key that can scroll, or of the node the user last clicked (the sequential focus
    /// navigation starting point) when the viewport is focused. The keys that edit a text control, a select element or
    /// editable content do not scroll.
    /// </summary>
    private static bool ScrollByKey(Node target, KeyboardEvent keyboardEvent)
    {
        if (target is HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement || Editing.GetEditingHost(target) is not null)
            return false;

        var document = target.NodeDocument;
        var start = target as Element ?? target.ParentElement;
        if (FocusModel.GetFocusedElement(document) is null && document.SequentialFocusNavigationStartingPoint?.Node is { IsConnected: true } clicked)
        {
            start = clicked as Element ?? clicked.ParentElement;
        }

        return start is not null && UserInput.ScrollByKey(start, keyboardEvent);
    }

    /// <summary>Whether the Enter key activates the element (as in browsers): the links, the buttons and the summary of a details element.</summary>
    private static bool IsActivatedByEnter(Element element)
    {
        if (!element.IsHtmlNamespace)
            return element is SvgAElement or MathMLAnchorElement && element.HasActivationBehavior;

        return element.TagId switch
        {
            TagId.A or TagId.Area => element.HasAttributeWithLocalName("href"),
            TagId.Button or TagId.Summary => true,
            TagId.Input => HtmlElementStates.GetInputType(element) is "submit" or "reset" or "button" or "image",
            _ => false,
        };
    }

    /// <summary>The fields that block implicit submission (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#field-that-blocks-implicit-submission).</summary>
    private static bool BlocksImplicitSubmission(HtmlInputElement input) => HtmlElementStates.GetInputType(input) is "text" or "search" or "url" or "tel" or "email" or "password"
        or "date" or "month" or "week" or "time" or "datetime-local" or "number";

    /// <summary>
    /// The implicit submission of a form (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#implicit-submission):
    /// a click on its default button (its first submit button), else the form is submitted when it has a single field that
    /// blocks implicit submission.
    /// </summary>
    private static void SubmitImplicitly(HtmlFormElement form)
    {
        var root = form.GetRoot();
        var blockingFields = 0;
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is not HtmlElement element || !ReferenceEquals(element.FormOwner, form))
                continue;

            if (element.IsSubmitButton())
            {
                // A disabled default button does not submit the form
                if (!HtmlElementStates.IsActuallyDisabled(element))
                {
                    FireSyntheticClick(element);
                }

                return;
            }

            if (element is HtmlInputElement input && BlocksImplicitSubmission(input))
            {
                blockingFields++;
            }
        }

        if (blockingFields <= 1)
        {
            form.SubmitForm(form, submittedFromSubmitMethod: false);
        }
    }

    /// <summary>Whether the space key activates the element (as in browsers): the buttons, the check boxes, the radio buttons and the summary of a details element.</summary>
    internal static bool IsActivatedBySpace(Element element)
    {
        if (!element.IsHtmlNamespace)
            return false;

        return element.TagId switch
        {
            TagId.Button or TagId.Summary => true,
            TagId.Input => HtmlElementStates.GetInputType(element) is "submit" or "reset" or "button" or "image" or "checkbox" or "radio" or "color" or "file",
            _ => false,
        };
    }

    /// <summary>Fires the trusted click event of a keyboard activation (https://html.spec.whatwg.org/multipage/webappapis.html#fire-a-click-event), which runs the activation behavior.</summary>
    internal static void FireSyntheticClick(Element element)
    {
        if (HtmlElementStates.IsActuallyDisabled(element))
            return;

        var click = new PointerEvent("click", new PointerEventInit { Bubbles = true, Cancelable = true, Composed = true, View = element.NodeDocument.DefaultView, PointerId = -1 }) { IsTrusted = true, IsPositionless = true };
        EventDispatcher.Dispatch(click, element);
    }

    /// <summary>
    /// The click focus (https://html.spec.whatwg.org/multipage/interaction.html#click-focusable): the focusing steps run for
    /// the nearest focusable area (in the flat tree) that contains the target, with the "click" focus trigger. When there is
    /// none, the viewport is focused and the target becomes the sequential focus navigation starting point.
    /// </summary>
    private static void FocusClickedArea(Document topDocument, Node target)
    {
        for (Node? node = target; node is not null; node = HtmlElement.GetFlatTreeParent(node))
        {
            if (node is not Element element)
                continue;

            if (FocusModel.IsClickFocusable(element))
            {
                topDocument.LastFocusWasPointer = true;
                topDocument.LastPointerFocusDocument = element.NodeDocument;
                FocusModel.Focus(element, focusVisible: null, FocusTrigger.Click);

                // As in browsers, the sequential focus navigation continues from the clicked node in the focused element
                if (!ReferenceEquals(element, target) && ReferenceEquals(FocusModel.GetFocusedElement(topDocument), element))
                {
                    target.NodeDocument.SequentialFocusNavigationStartingPoint = new(target);
                }

                return;
            }

            if (element.AttachedShadowRoot is { DelegatesFocus: true } && FocusModel.GetFocusableArea(element, FocusTrigger.Click) is { } area)
            {
                topDocument.LastFocusWasPointer = true;
                topDocument.LastPointerFocusDocument = element.NodeDocument;
                FocusModel.Focus(area, focusVisible: null, FocusTrigger.Click);
                return;
            }
        }

        // Nothing is focused by the click: the elements focused by script indicate their focus again
        topDocument.LastFocusWasPointer = false;
        var document = target.NodeDocument;
        FocusModel.RunFocusingSteps(document);
        document.SequentialFocusNavigationStartingPoint = new(target);
    }
}
