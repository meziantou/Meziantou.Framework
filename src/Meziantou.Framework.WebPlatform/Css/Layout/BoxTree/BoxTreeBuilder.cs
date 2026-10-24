using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// Builds the box tree of a document (https://drafts.csswg.org/css-display/#box-generation) from its flat tree
/// (https://drafts.csswg.org/css-scoping/#flat-tree) and the computed styles: the principal boxes of the elements, the
/// boxes of the <c>::marker</c>, <c>::before</c>, <c>::after</c> and <c>::first-letter</c> pseudo-elements and of the generated
/// content, the text runs, then the anonymous boxes of the fixups: the block-in-inline split and the anonymous blocks
/// (https://drafts.csswg.org/css2/#anonymous-block-level), the anonymous flex and grid items, the table fixup
/// (https://drafts.csswg.org/css-tables-3/#fixup-algorithm), the ruby fixup (https://drafts.csswg.org/css-ruby-1/#box-fixup)
/// and the run-in boxes (https://drafts.csswg.org/css-display/#run-in-layout).
/// </summary>
/// <remarks>
/// The computed display is already blockified (the root element, the floats, the absolutely positioned boxes and the flex
/// and grid items); the children of the ruby containers are inlinified here. The fixups of a box run when its children are
/// built (bottom-up), except the split of the inline boxes that contain block-level boxes, which is done by their nearest
/// block container.
/// </remarks>
internal sealed partial class BoxTreeBuilder
{
    private readonly StyleEngine _engine;
    private readonly Dictionary<(Node Node, string? PseudoElement), LayoutBox> _boxes;
    private readonly Dictionary<ComputedStyle, ComputedStyle> _anonymousStyles = new(ReferenceEqualityComparer.Instance);

    /// <summary>The styles of the boxes of the fieldsets, by the style of the fieldset and by the style of its principal box.</summary>
    private Dictionary<ComputedStyle, (ComputedStyle Frame, ComputedStyle Content)>? _fieldsetStyles;
    private readonly List<ContainerBox> _firstLetterContainers = [];
    private readonly HashSet<ContainerBox> _inlinesWithBlocks = new(ReferenceEqualityComparer.Instance);

    /// <summary>The anonymous blocks generated around the run-in sequences that are not followed by a block box.</summary>
    private readonly HashSet<ContainerBox> _runInBlocks = new(ReferenceEqualityComparer.Instance);
    private readonly StringBuilder _text = new();
    private readonly Element _root;
    private CssCounters? _counters;

    /// <summary>The elements of the top layer whose boxes are built as children of the viewport, or null.</summary>
    private HashSet<Element>? _deferredTopLayer;

    /// <summary>Whether elements of the document host a scoped view transition (see <see cref="BuildScopedViewTransitionLayer"/>).</summary>
    private bool _hasScopedViewTransitions;

    /// <summary>The elements of the top layer reached while building the tree (their ancestors generate boxes), not built yet.</summary>
    private HashSet<Element>? _reachedTopLayer;

    /// <summary>The depth of nesting of the quotes (https://drafts.csswg.org/css-content-3/#quote-values), in the tree order of the generated content.</summary>
    private int _quoteDepth;

    /// <summary>The depth of nesting of the quotes at the start of the contents of the size containers, when it is not 0 (see <see cref="RebuildContents"/>).</summary>
    private Dictionary<ContainerBox, int>? _containerQuoteDepths;

    /// <summary>The principal boxes with <c>content-visibility: auto</c> (see <see cref="BoxTree.ContentVisibilityAutoBoxes"/>).</summary>
    private List<ContainerBox> _contentVisibilityAutoBoxes = [];

    /// <summary>The principal boxes whose <c>contain-intrinsic-size</c> may have the <c>auto</c> keyword (see <see cref="BoxTree.ContainIntrinsicSizeAutoBoxes"/>).</summary>
    private List<ContainerBox> _containIntrinsicSizeAutoBoxes = [];

    /// <summary>Whether the styles may declare contain-intrinsic-size (or content-visibility, which implies its auto keyword): the boxes are checked for it.</summary>
    private readonly bool _mayUseContainIntrinsicSizeAuto;

    private BoxTreeBuilder(Element root, Dictionary<(Node Node, string? PseudoElement), LayoutBox>? boxes = null)
    {
        _root = root;
        _engine = root.NodeDocument.GetStyleEngine();
        _boxes = boxes ?? [];
        _mayUseContainIntrinsicSizeAuto = _engine.MayHaveContentVisibility();
    }

    /// <summary>Builds the box tree of the document, or returns an empty tree when its root element is not rendered (e.g. the document has no browsing context).</summary>
    public static BoxTree Build(Document document)
    {
        if (document.DocumentElement is not { } root || !ComputedStyleDeclarations.IsRendered(root))
            return new BoxTree(viewport: null, [], containerQuoteDepths: null);

        var builder = new BoxTreeBuilder(root);
        var viewport = builder.BuildViewport();
        var tree = new BoxTree(viewport, builder._boxes, builder._containerQuoteDepths)
        {
            ContentVisibilityAutoBoxes = builder._contentVisibilityAutoBoxes,
            ContainIntrinsicSizeAutoBoxes = builder._containIntrinsicSizeAutoBoxes,
            ScrollControls = builder._scrollControls,
            Counters = builder._counters,
        };

        builder.RecordUnpatchableFeatures(tree);
        return tree;
    }

    /// <summary>
    /// Builds again the children of the principal box of a size container, after the styles of the descendants of the container
    /// were computed again with its size by the layout (the style and layout interleaving of the size container queries,
    /// https://drafts.csswg.org/css-contain-3/#size-container): its <c>::marker</c>, <c>::before</c>, the boxes of its flat tree
    /// descendants, its <c>::after</c> and the <c>::first-letter</c> boxes in its contents (also the one of an ancestor whose
    /// first formatted line is in the container). The box of the container is kept: its style does not depend on its size.
    /// </summary>
    /// <remarks>
    /// The boxes of the elements of the top layer that are descendants of the container are children of the viewport, laid out
    /// after the root box: they are built again at their place (an element of the top layer that had no box is not added).
    /// The counters are computed again from the new styles; the quotes continue from the depth at the start of the contents of
    /// the container.
    /// </remarks>
    public static void RebuildContents(BoxTree tree, ContainerBox box)
    {
        if (box.Element is not { } element || box.PseudoElement is not null || box.Kind != ContainerBoxKind.Normal || element.NodeDocument.DocumentElement is not { } root)
            return;

        var builder = new BoxTreeBuilder(root, tree.Boxes)
        {
            _contentVisibilityAutoBoxes = tree.ContentVisibilityAutoBoxes,
            _containIntrinsicSizeAutoBoxes = tree.ContainIntrinsicSizeAutoBoxes,
            _scrollControls = tree.ScrollControls,
        };
        if (root.NodeDocument.TopLayerElementsForRendering is { Count: > 0 } topLayer)
        {
            builder._deferredTopLayer = CreateDeferredTopLayer(topLayer, root);
        }

        // The boxes of the previous contents are forgotten; the ::first-letter boxes of the ancestors are created again
        List<Element>? firstLetterAncestors = null;
        RemoveBoxes(tree.Boxes, box, element, ref firstLetterAncestors);
        box.DetachChildren();
        if (box.FirstLineStyle is not null)
        {
            box.FirstLineStyle = null;
        }

        builder._quoteDepth = tree.GetContainerQuoteDepth(box);
        if (BoxGeneration.IsSvgElement(element))
        {
            // A foreignObject element (see BuildForeignObjects)
            builder.BuildChildNodes(element, box.Style, box);
            builder.FixupContainer(box);
        }
        else
        {
            builder.BuildBoxContents(element, box.Style, box.Display, box);
        }

        foreach (var container in builder._firstLetterContainers)
        {
            builder.CreateFirstLetterBox(container);
        }

        if (firstLetterAncestors is not null)
        {
            foreach (var ancestor in firstLetterAncestors)
            {
                if (tree.GetPrincipalBox(ancestor) is { } ancestorBox)
                {
                    builder.CreateFirstLetterBox(ancestorBox);
                }
            }
        }

        // The boxes of the elements of the top layer in the container are children of the viewport (after the root box, laid out
        // after the container): they are built again at their place
        if (builder._deferredTopLayer is not null && tree.Viewport is { } viewport)
        {
            foreach (var topLayerElement in root.NodeDocument.TopLayerElementsForRendering!)
            {
                if (!ReferenceEquals(topLayerElement, element) && IsInclusiveDescendant(topLayerElement, element))
                {
                    builder.RebuildTopLayerElement(viewport, topLayerElement);
                }
            }
        }

        // The scroll markers of the elements of the container are in the groups of their scroll containers, which may be
        // outside of the container (and already laid out): they are collected again, and the document is laid out once more
        if (builder._scrollControls is { } scrollControls && tree.Viewport is { } treeViewport)
        {
            RemoveDetachedScrollControls(scrollControls, treeViewport);
            if (scrollControls.Groups.Count > 0)
            {
                builder.PopulateScrollMarkerGroups(treeViewport);
                scrollControls.PopulatedDuringLayout = true;
            }

            tree.ScrollControls = scrollControls;
        }

        // (the counters computed with the new styles are the ones of the next patches)
        if (builder._counters is not null)
        {
            tree.Counters = builder._counters;
        }

        builder.RecordUnpatchableFeatures(tree);
    }

    /// <summary>
    /// Builds the boxes of the cues and of the regions rendered over a video element (https://w3c.github.io/webvtt/#obtaining-css-boxes)
    /// as the children of its replaced box: the layout calls it when the cues that are displayed change (see
    /// <see cref="WebVttCueLayout"/>). The document tree of the boxes is the tree of the elements that stand for the WebVTT
    /// objects (see <see cref="WebVtt.WebVttElement"/>): a region is a flex container whose children are the roots of its
    /// cues; the root of a cue is a block container, whose children are wrapped in an anonymous inline box, the WebVTT cue
    /// background box.
    /// </summary>
    public static void RebuildCueBoxes(BoxTree tree, ContainerBox videoBox, IReadOnlyList<WebVtt.WebVttDisplayItem> items)
    {
        if (videoBox.Element is not { } video || video.NodeDocument.DocumentElement is not { } root)
            return;

        List<Element>? firstLetterAncestors = null;
        RemoveBoxes(tree.Boxes, videoBox, video, ref firstLetterAncestors);
        videoBox.DetachChildren();
        var builder = new BoxTreeBuilder(root, tree.Boxes);
        foreach (var item in items)
        {
            if (item.Region is null)
            {
                builder.BuildCueRoot(item.Element, videoBox);
                continue;
            }

            var style = builder._engine.GetStyle(item.Element);
            var region = new ContainerBox(ContainerBoxKind.Normal, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flex), item.Element);
            videoBox.AppendChild(region);
            tree.Boxes[(item.Element, null)] = region;
            for (var child = item.Element.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is WebVtt.WebVttElement cueRoot)
                {
                    builder.BuildCueRoot(cueRoot, region);
                }
            }

            builder.FixupContainer(region);
        }

        builder.RecordUnpatchableFeatures(tree);
    }

    private void BuildCueRoot(WebVtt.WebVttElement cueRoot, ContainerBox parent)
    {
        var style = _engine.GetStyle(cueRoot);
        var box = new ContainerBox(ContainerBoxKind.Normal, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot), cueRoot);
        parent.AppendChild(box);
        _boxes[(cueRoot, null)] = box;

        // The WebVTT cue background box
        var backgroundStyle = _engine.GetStyle(cueRoot, WebVtt.WebVttElement.BackgroundBoxPseudoElement);
        var background = new ContainerBox(ContainerBoxKind.Normal, backgroundStyle, Display.Inline, cueRoot, WebVtt.WebVttElement.BackgroundBoxPseudoElement);
        box.AppendChild(background);
        _boxes[(cueRoot, WebVtt.WebVttElement.BackgroundBoxPseudoElement)] = background;
        BuildNodes(cueRoot.FirstChild, cueRoot, backgroundStyle, background);
        FixupContainer(background);
        FixupContainer(box);
    }

    /// <summary>Removes the boxes of the descendants of a box from the boxes by node, and collects the elements whose ::first-letter box is there but which are not descendants of the container.</summary>
    private static void RemoveBoxes(Dictionary<(Node Node, string? PseudoElement), LayoutBox> boxes, ContainerBox parent, Element container, ref List<Element>? firstLetterAncestors, HashSet<LayoutBox>? kept = null)
    {
        foreach (var child in parent.Children)
        {
            // (the boxes kept by a patch of the tree, see TryPatch)
            if (kept is not null && kept.Contains(child))
                continue;

            if (child is TextRun run)
            {
                if (run.Node is { } node && boxes.TryGetValue((node, null), out var first) && ReferenceEquals(first, run))
                {
                    boxes.Remove((node, null));
                }

                continue;
            }

            var childBox = (ContainerBox)child;
            if (childBox.Element is { } childElement && boxes.TryGetValue((childElement, childBox.PseudoElement), out var existing) && ReferenceEquals(existing, childBox))
            {
                boxes.Remove((childElement, childBox.PseudoElement));
                if (childBox.PseudoElement == "first-letter" && !IsInclusiveDescendant(childElement, container))
                {
                    (firstLetterAncestors ??= []).Add(childElement);
                }
            }

            RemoveBoxes(boxes, childBox, container, ref firstLetterAncestors, kept);
        }
    }

    /// <summary>Returns whether an element is an inclusive descendant of another one in the flat tree.</summary>
    private static bool IsInclusiveDescendant(Element element, Element container)
    {
        for (Element? current = element; current is not null; current = StyleEngine.GetInheritanceParent(current))
        {
            if (ReferenceEquals(current, container))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds again the boxes of an element of the top layer (and of its <c>::backdrop</c>) whose style was computed again. The
    /// boxes are children of the viewport, which may already be recorded for the layout of the absolutely positioned boxes:
    /// the principal box is kept and receives the new style and children when it is of the same kind, else it is replaced. An
    /// element that had no box is not added.
    /// </summary>
    private void RebuildTopLayerElement(ContainerBox viewport, Element element)
    {
        if (_boxes.GetValueOrDefault((element, null)) is not ContainerBox previous || previous.Parent != viewport)
            return;

        if (_boxes.GetValueOrDefault((element, "backdrop")) is ContainerBox backdrop && backdrop.Parent == viewport)
        {
            var backdropStyle = _engine.GetStyle(element, "backdrop");
            if (backdropStyle.Layout.Display.IsNone || backdropStyle.GetComputedValue("content") == "none")
            {
                _boxes.Remove((element, "backdrop"));
                viewport.RemoveChild(backdrop);
            }
            else
            {
                backdrop.Style = backdropStyle;
            }
        }

        List<Element>? firstLetterAncestors = null;
        _boxes.Remove((element, null));
        RemoveBoxes(_boxes, previous, element, ref firstLetterAncestors);

        // The new boxes are built in a temporary parent
        var parent = new ContainerBox(ContainerBoxKind.Anonymous, previous.Style, Display.Block, element: null);
        _deferredTopLayer!.Remove(element);
        BuildElement(element, parent);
        _deferredTopLayer.Add(element);
        if (parent.FirstChild is ContainerBox box && ReferenceEquals(parent.FirstChild, parent.LastChild) && box.Kind == previous.Kind)
        {
            parent.RemoveChild(box);
            previous.DetachChildren();
            foreach (var child in box.DetachChildren())
            {
                previous.AppendChild(child);
            }

            previous.Style = box.Style;
            previous.Display = box.Display;
            previous.ChildrenAreInline = box.ChildrenAreInline;
            previous.SkipsContents = box.SkipsContents;
            previous.IsContentVisibilityAuto = box.IsContentVisibilityAuto;
            ReplaceBox(_contentVisibilityAutoBoxes, box, previous);
            ReplaceBox(_containIntrinsicSizeAutoBoxes, box, previous);
            if (previous.FirstLineStyle is not null || box.FirstLineStyle is not null)
            {
                previous.FirstLineStyle = box.FirstLineStyle;
            }

            _boxes[(element, null)] = previous;
            var index = _firstLetterContainers.IndexOf(box);
            if (index >= 0)
            {
                _firstLetterContainers[index] = previous;
            }

            return;
        }

        // Another kind of box (or none): the box is replaced
        foreach (var child in parent.DetachChildren())
        {
            if (child is ContainerBox replacement)
            {
                replacement.IsInTopLayer = true;
            }

            viewport.InsertBefore(child, previous);
        }

        viewport.RemoveChild(previous);
    }

    private ContainerBox BuildViewport()
    {
        // The initial containing block has the writing mode and the direction of the root element (https://drafts.csswg.org/css-writing-modes-4/#icb):
        // the principal writing mode, propagated from the body element
        var rootStyle = _engine.GetStyle(_root);
        SetPrincipalWritingMode(rootStyle);
        var viewport = new ContainerBox(ContainerBoxKind.Viewport, rootStyle, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot), element: null);
        if (_root.NodeDocument.TopLayerElementsForRendering is { Count: > 0 } topLayer)
        {
            _deferredTopLayer = CreateDeferredTopLayer(topLayer, _root);
        }

        _hasScopedViewTransitions = _root.NodeDocument.HasScopedViewTransitions;
        BuildElement(_root, viewport);
        if (_reachedTopLayer is not null || _reachedPickers is not null)
        {
            BuildTopLayer(viewport);
        }

        BuildViewTransitionLayer(viewport);

        FixupContainer(viewport);
        PopulateScrollMarkerGroups(viewport);
        foreach (var container in _firstLetterContainers)
        {
            CreateFirstLetterBox(container);
        }

        return viewport;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-writing-modes-4/#principal-flow: the used writing mode and direction of the root element are
    /// the computed ones of its first body child element (the HTML documents), else its own. The containment of the html or body
    /// element disables the propagation (https://drafts.csswg.org/css-contain-2/#contain-property).
    /// </summary>
    private void SetPrincipalWritingMode(ComputedStyle rootStyle)
    {
        var source = rootStyle;
        if (_root.IsHtml(TagId.Html) && !ScrollContainers.HasContainment(rootStyle.Layout))
        {
            foreach (var child in _root.EnumerateChildNodes())
            {
                if (child is HtmlBodyElement body)
                {
                    if (_engine.GetStyle(body) is { } bodyStyle && !ScrollContainers.HasContainment(bodyStyle.Layout))
                    {
                        source = bodyStyle;
                    }

                    break;
                }
            }
        }

        rootStyle.Layout.SetPrincipalWritingMode(
            LayoutKeywords.ParseWritingMode(source.GetComputedValue("writing-mode")),
            source.GetComputedValue("direction") == "rtl" ? Direction.Rtl : Direction.Ltr);
    }

    /// <summary>
    /// Builds the boxes of the elements of the top layer (https://drafts.csswg.org/css-position-4/#top-layer) that were reached
    /// while the tree was built (their ancestors generate boxes): each one is "rendered as an atomic unit as if it were a sibling
    /// of the document's root", after its <c>::backdrop</c> (https://drafts.csswg.org/css-position-4/#backdrop), in the order of
    /// the top layer. The elements of the top layer that are descendants of another one are reached while it is built.
    /// </summary>
    private void BuildTopLayer(ContainerBox viewport)
    {
        var topLayer = _root.NodeDocument.TopLayerElementsForRendering!;
        var built = new List<(Element Element, ContainerBox? Backdrop, LayoutBox? Box)>();
        var remaining = true;
        while (remaining)
        {
            remaining = false;
            foreach (var element in topLayer)
            {
                // The select popover of a select element whose picker is open (the options are built with it)
                if (_reachedPickers is not null && _reachedPickers.Remove(element))
                {
                    remaining = true;
                    var pickerBackdrop = BuildBackdrop(element, viewport, StyleEngine.SelectPickerPrefix + "backdrop");
                    var lastChild = viewport.LastChild;
                    BuildSelectPicker((HtmlSelectElement)element, viewport, inTopLayer: true);
                    built.Add((element, pickerBackdrop, ReferenceEquals(viewport.LastChild, lastChild) ? null : viewport.LastChild));
                    continue;
                }

                if (_reachedTopLayer is null || !_reachedTopLayer.Remove(element))
                    continue;

                remaining = true;
                _deferredTopLayer!.Remove(element);
                var backdrop = BuildBackdrop(element, viewport);
                var last = viewport.LastChild;
                BuildElement(element, viewport);
                var box = ReferenceEquals(viewport.LastChild, last) ? null : viewport.LastChild;
                if (box is ContainerBox container)
                {
                    container.IsInTopLayer = true;
                }

                built.Add((element, backdrop, box));
            }
        }

        // The boxes are in the order of the top layer, after the root box
        foreach (var element in topLayer)
        {
            foreach (var (builtElement, backdrop, box) in built)
            {
                if (!ReferenceEquals(builtElement, element))
                    continue;

                if (backdrop is not null)
                {
                    viewport.AppendChild(backdrop);
                }

                if (box is not null)
                {
                    viewport.AppendChild(box);
                }
            }
        }
    }

    /// <summary>
    /// Builds the boxes of the pseudo-elements of the active view transition of the document
    /// (https://drafts.csswg.org/css-view-transitions-1/#view-transition-pseudo-elements): the <c>::view-transition</c>
    /// pseudo-element "generates a new stacking context, called the view transition layer, which paints after all other content
    /// of the document (including any content rendered in the top layer)": its box is the last child of the viewport box, and its
    /// containing block is the snapshot containing block. Its descendants are the groups, their image pairs, and the images,
    /// which are replaced boxes (https://drafts.csswg.org/css-view-transitions-1/#::view-transition-old). As in the browsers, the
    /// <c>::view-transition</c> pseudo-element of an animating transition generates a box even without group (the specification
    /// says it "only generates boxes when that pseudo-element has children").
    /// </summary>
    private void BuildViewTransitionLayer(ContainerBox viewport)
    {
        if (_root.NodeDocument.ViewTransitionPseudoTree is not { } tree || !ReferenceEquals(tree.Root, _root))
            return;

        var style = _engine.GetStyle(_root, ViewTransitionPseudoTree.RootName);
        if (style.Layout.Display.IsNone)
            return;

        var box = new ContainerBox(ContainerBoxKind.Normal, style, GetViewTransitionDisplay(style), _root, ViewTransitionPseudoTree.RootName) { IsInTopLayer = true, IsViewTransitionRoot = true };
        viewport.AppendChild(box);
        _boxes[(_root, ViewTransitionPseudoTree.RootName)] = box;
        foreach (var group in tree.Groups)
        {
            BuildViewTransitionGroup(_root, group, box);
        }

        FixupContainer(box);
    }

    /// <summary>
    /// Creates the boxes of the pseudo-element tree of a scoped view transition
    /// (https://drafts.csswg.org/css-view-transitions-2/#scoped-vt-layout): "the ::view-transition pseudo-element is a child of the
    /// root element in the box tree", its last child. It is absolutely positioned in the padding box of the root element (which
    /// has layout containment), "generates a stacking context which paints separately from, and on top of, the painting of the
    /// root element and its other children", is not scrolled nor clipped by the root element, and does not extend its
    /// scrollable overflow (see <see cref="ContainerBox.IsScopedViewTransitionRoot"/>).
    /// </summary>
    private void BuildScopedViewTransitionLayer(Element scope, ViewTransitionPseudoTree tree, ContainerBox contents)
    {
        var style = _engine.GetStyle(scope, ViewTransitionPseudoTree.RootName);
        if (style.Layout.Display.IsNone)
            return;

        var box = new ContainerBox(ContainerBoxKind.Normal, style, GetViewTransitionDisplay(style), scope, ViewTransitionPseudoTree.RootName) { IsScopedViewTransitionRoot = true };
        contents.AppendChild(box);
        _boxes[(scope, ViewTransitionPseudoTree.RootName)] = box;
        foreach (var group in tree.Groups)
        {
            BuildViewTransitionGroup(scope, group, box);
        }

        FixupContainer(box);
    }

    private void BuildViewTransitionGroup(Element root, ViewTransitionGroup group, ContainerBox parent)
    {
        if (BuildViewTransitionBox(root, ViewTransitionPseudoTree.GroupPrefix + group.Name + ")", parent) is not { } groupBox)
            return;

        if (BuildViewTransitionBox(root, ViewTransitionPseudoTree.ImagePairPrefix + group.Name + ")", groupBox) is { } imagePair)
        {
            if (group.HasOld)
            {
                BuildViewTransitionImage(root, ViewTransitionPseudoTree.OldPrefix + group.Name + ")", imagePair);
            }

            if (group.HasNew)
            {
                BuildViewTransitionImage(root, ViewTransitionPseudoTree.NewPrefix + group.Name + ")", imagePair);
            }

            FixupContainer(imagePair);
        }

        if (group.Children.Count > 0 && BuildViewTransitionBox(root, ViewTransitionPseudoTree.GroupChildrenPrefix + group.Name + ")", groupBox) is { } children)
        {
            foreach (var child in group.Children)
            {
                BuildViewTransitionGroup(root, child, children);
            }

            FixupContainer(children);
        }

        FixupContainer(groupBox);
    }

    private ContainerBox? BuildViewTransitionBox(Element root, string pseudoElement, ContainerBox parent)
    {
        var style = _engine.GetStyle(root, pseudoElement);
        if (style.Layout.Display.IsNone)
            return null;

        var box = new ContainerBox(ContainerBoxKind.Normal, style, GetViewTransitionDisplay(style), root, pseudoElement);
        parent.AppendChild(box);
        _boxes[(root, pseudoElement)] = box;
        return box;
    }

    /// <summary>The images of a view transition are replaced boxes without children.</summary>
    private void BuildViewTransitionImage(Element root, string pseudoElement, ContainerBox parent)
    {
        var style = _engine.GetStyle(root, pseudoElement);
        var display = style.Layout.Display;
        if (display.IsNone)
            return;

        var box = new ContainerBox(ContainerBoxKind.Replaced, style, GetReplacedDisplay(display.IsContents ? Display.Inline : display), root, pseudoElement);
        parent.AppendChild(box);
        _boxes[(root, pseudoElement)] = box;
    }

    /// <summary>The display of the box of a view transition pseudo-element: the table and the contents displays are treated as flow layout.</summary>
    private static Display GetViewTransitionDisplay(ComputedStyle style)
    {
        var display = style.Layout.Display;
        if (display.IsContents || display.IsTable || display.Internal != DisplayInternal.None)
            return display.IsInlineLevel ? Display.Inline : Display.Block;

        return display;
    }

    /// <summary>
    /// Creates the box of the <c>::backdrop</c> of an element of the top layer (https://drafts.csswg.org/css-position-4/#backdrop):
    /// "it generates boxes as if it were a sibling of its originating element, when its content is not none" (blockified: it is
    /// absolutely positioned in the top layer). Its style is the UA <c>::backdrop { position: fixed; inset: 0 }</c> by default.
    /// </summary>
    private ContainerBox? BuildBackdrop(Element element, ContainerBox viewport, string pseudoElement = "backdrop")
    {
        var style = _engine.GetStyle(element, pseudoElement);
        var display = style.Layout.Display;
        if (display.IsNone || style.GetComputedValue("content") == "none")
            return null;

        var box = new ContainerBox(ContainerBoxKind.Normal, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow), element, pseudoElement) { IsInTopLayer = true };
        viewport.AppendChild(box);
        _boxes[(element, pseudoElement)] = box;
        return box;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Box generation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Whether the children of a box are in the skipped contents of the box or of an ancestor (<see cref="ContainerBox.SkipsContents"/>).</summary>
    private static bool IsInSkippedContents(ContainerBox parent)
    {
        for (var box = parent; box is not null; box = box.Parent)
        {
            if (box.SkipsContents)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Appends the boxes generated by an element to a container (none for display: none, the boxes of its contents for
    /// display: contents), with the boxes of its scroll navigation controls that are siblings of its box (see <see cref="BuildScrollControls"/>).
    /// </summary>
    private void BuildElement(Element element, ContainerBox parent)
    {
        // A patch of the tree keeps the boxes of the elements that did not change (see TryPatch)
        if (_patch is { } patch && patch.TryKeep(element, parent))
            return;

        BuildElementBoxes(element, parent);
        if (!ReferenceEquals(element, _root))
        {
            BuildScrollControls(element, parent);
        }
    }

    private void BuildElementBoxes(Element element, ContainerBox parent)
    {
        var style = _engine.GetStyle(element);
        var display = style.Layout.Display;
        if (display.IsNone)
            return;

        // https://drafts.csswg.org/css-position-4/#top-layer: an element of the top layer is not rendered at its position in the
        // tree, but as a child of the viewport (see BuildTopLayer)
        // (https://drafts.csswg.org/css-contain-2/#skips-its-contents: an element of the top layer in the skipped contents of an
        // ancestor is skipped too: it stays in the contents of that ancestor, which are not painted)
        if (_deferredTopLayer is not null && _deferredTopLayer.Contains(element) && !IsInSkippedContents(parent))
        {
            (_reachedTopLayer ??= new HashSet<Element>(ReferenceEqualityComparer.Instance)).Add(element);
            return;
        }

        // An embed element that represents nothing generates no box
        if (BoxGeneration.RepresentsNothing(element))
            return;

        // The SVG elements generate SVG boxes, except the outermost svg element, a CSS replaced box (https://svgwg.org/svg2-draft/render.html#Rendering)
        if (BoxGeneration.IsSvgElement(element))
        {
            if (BoxGeneration.IsSvgRoot(element) && !display.IsContents)
            {
                BuildSvgRoot(element, style, display, parent);
            }

            return;
        }

        if (display.IsContents)
        {
            // https://drafts.csswg.org/css-display/#valdef-display-contents: the element is replaced by its contents and its pseudo-elements
            BuildContents(element, style, parent);
            return;
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#button-layout: "If the computed value of 'display' is 'inline-grid',
        // 'grid', 'inline-flex', 'flex', 'none', or 'contents', then behave as the computed value. Otherwise, if the computed value
        // of 'display' is a value such that the outer display type is 'inline', then behave as 'inline-block'. Otherwise, behave as
        // 'flow-root'."
        if (element.IsHtml(TagId.Button) && (display.Inside is not (DisplayInside.Flex or DisplayInside.Grid) || display.Internal != DisplayInternal.None))
        {
            display = new Display(DisplayBox.Normal, display.Outside == DisplayOutside.Inline && display.Internal == DisplayInternal.None ? DisplayOutside.Inline : DisplayOutside.Block, DisplayInside.FlowRoot);
        }

        // As in the browsers, a file upload control is an atomic box whatever its display, as the buttons (and as the replaced
        // elements, whose table displays behave as inline): it is the block container of its button and of its text. So are
        // the controls with base appearance whose parts are boxes
        if (FormControlContents.HasGeneratedContents(element, style))
        {
            display = GetReplacedDisplay(display);
            if (display.Inside is not (DisplayInside.Flex or DisplayInside.Grid))
            {
                display = new Display(DisplayBox.Normal, display.Outside == DisplayOutside.Inline ? DisplayOutside.Inline : DisplayOutside.Block, DisplayInside.FlowRoot);
            }
        }

        // A br or wbr element is a line break (opportunity) whatever its display (other than none and contents), as in the browsers:
        // its box is an inline box, so that it breaks the line of its parent
        if (element.IsHtml(TagId.Br) || element.IsHtml(TagId.Wbr))
        {
            display = Display.Inline;
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements: "The element is expected to
        // establish a new block formatting context. The 'display' property is expected to act as follows: If the computed value of
        // 'display' is a value such that the outer display type is 'inline', then behave as 'inline-block'. Otherwise, behave as
        // 'flow-root'." (the display of a flex or grid fieldset is the one of its anonymous fieldset content box, see WrapFieldsetContents)
        if (element.IsHtml(TagId.Fieldset))
        {
            var isInline = display.Outside == DisplayOutside.Inline && (display.Internal == DisplayInternal.None || display.IsInternalRubyBox);
            display = new Display(DisplayBox.Normal, isInline ? DisplayOutside.Inline : DisplayOutside.Block, DisplayInside.FlowRoot);
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets: a frameset is rendered "as a box with the height
        // and width of the viewport", whose frame and frameset children are laid out in its grid: as in the browsers, they are
        // block boxes whatever their display (see FramesetFormattingContext)
        if (element.IsHtml(TagId.Frameset) || (element.IsHtml(TagId.Frame) && FramesetFormattingContext.IsFramesetChild(element)))
        {
            display = new Display(DisplayBox.Normal, DisplayOutside.Block, element.IsHtml(TagId.Frameset) ? DisplayInside.FlowRoot : DisplayInside.Flow);
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements: a rendered legend "is expected to
        // establish a new formatting context for its contents [...] The 'display' property is expected to behave as if its computed
        // value was blockified" (a list item stays a list item: https://drafts.csswg.org/css-display-4/#blockify)
        if (IsRenderedLegend(element, style))
        {
            display = new Display(DisplayBox.Normal, DisplayOutside.Block, display.Internal != DisplayInternal.None || display.Inside == DisplayInside.Flow ? DisplayInside.FlowRoot : display.Inside, IsListItem: display.IsListItem);
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#images-3: "If the element is an img element that represents some
        // text and the user agent does not expect this to change: The user agent is expected to treat the element as a
        // non-replaced phrasing element whose content is the text" (as in the browsers, an image button too)
        if (element is HtmlImageElement or HtmlInputElement { InputType: InputType.Image })
        {
            // An image whose content is replaced by an image of the content property does not represent its alternative text
            string? altText = null;
            if (element is HtmlImageElement imageElement)
            {
                altText = GetReplacementImage(style) is null ? imageElement.GetAltTextRendering(style) : null;
                imageElement.BoxTreeRendersAltText = altText is not null;
            }
            else if (element is HtmlInputElement imageButton)
            {
                altText = GetReplacementImage(style) is null ? imageButton.GetImageButtonAltTextRendering(style) : null;
                imageButton.BoxTreeRendersImageButtonAltText = altText is not null;
            }

            if (altText is not null)
            {
                var altBox = CreatePrincipalBox(style, display, element, pseudoElement: null, parent, out var altContents);
                _boxes[(element, null)] = altBox;
                ApplyContentVisibility(altBox, element, style);
                altContents.AppendChild(new TextRun(style, element, pseudoElement: null, TextRunKind.Generated, node: null, altText, 0, altText.Length));
                FixupContainer(altContents);
                if (!ReferenceEquals(altBox, altContents))
                {
                    FixupContainer(altBox);
                }

                return;
            }
        }

        if (BoxGeneration.IsReplaced(element, style))
        {
            // As in the browsers, the content of an img element or of an image button can be replaced by an image
            // (https://drafts.csswg.org/css-content-3/#content-property)
            var replaced = new ContainerBox(ContainerBoxKind.Replaced, style, GetReplacedDisplay(display), element) { GeneratedImage = element is HtmlImageElement or HtmlInputElement { InputType: InputType.Image } ? GetReplacementImage(style) : null };
            parent.AppendChild(replaced);
            _boxes[(element, null)] = replaced;
            ApplyContentVisibility(replaced, element, style);
            if (TextControlLayout.HasInnerEditor(element))
            {
                BuildTextControlContents(replaced, element, style);
            }
            else
            {
                BuildFormControlContents(replaced, element, style);
            }

            return;
        }

        // https://drafts.csswg.org/css-content-3/#content-property: an element whose content is an image is replaced by it
        if (GetReplacementImage(style) is { } image)
        {
            var replaced = new ContainerBox(ContainerBoxKind.Replaced, style, GetReplacedDisplay(display), element) { GeneratedImage = image };
            parent.AppendChild(replaced);
            _boxes[(element, null)] = replaced;
            ApplyContentVisibility(replaced, element, style);
            return;
        }

        var box = CreatePrincipalBox(style, display, element, pseudoElement: null, parent, out var contents);
        _boxes[(element, null)] = box;
        if (display.Internal == DisplayInternal.TableColumn)
        {
            // https://drafts.csswg.org/css-tables-3/#fixup-algorithm: the children of a table-column are discarded
            FixupContainer(contents);
            return;
        }

        // The contents of a size or anchored container may be built again by the layout (see RebuildContents), from the same state
        if (_quoteDepth != 0 && (style.Layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize | ContainerType.Anchored)) != ContainerType.Normal)
        {
            (_containerQuoteDepths ??= new(ReferenceEqualityComparer.Instance))[contents] = _quoteDepth;
        }

        ApplyContentVisibility(box, element, style);

        // https://drafts.csswg.org/css-contain-2/#containment-style: the effects of open-quote, close-quote, no-open-quote and
        // no-close-quote are scoped to the sub-tree of an element with style containment (its pseudo-elements included): the
        // depth of nesting continues from the outside, the changes inside do not affect the outside
        var quoteDepth = _quoteDepth;
        BuildBoxContents(element, style, display, contents);
        if (style.Layout.HasStyleContainment)
        {
            _quoteDepth = quoteDepth;
        }

        if (!ReferenceEquals(box, contents))
        {
            FixupContainer(box);
        }
    }

    /// <summary>
    /// Builds the children of the replaced box of a text control (see <see cref="TextControlLayout"/>): its inner editor, an
    /// anonymous block container whose text is the value, then the box of its <c>::placeholder</c> pseudo-element when its
    /// value is empty and it has a placeholder (https://html.spec.whatwg.org/multipage/input.html#attr-input-placeholder).
    /// The texts are generated text runs (no DOM node is created).
    /// </summary>
    private void BuildTextControlContents(ContainerBox control, Element element, ComputedStyle style)
    {
        var multiline = element is HtmlTextAreaElement;
        var display = new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot);
        var value = TextControlLayout.GetValue(element);

        // https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2: "if the element has a wrap attribute whose
        // value is an ASCII case-insensitive match for the string "off", then the user agent is expected to not wrap the rendered value"
        var wraps = !multiline || !string.Equals(element.GetAttributeValue("wrap"), "off", StringComparison.OrdinalIgnoreCase);
        var editorStyle = style.GetDerivedStyle((InnerEditorStyleKey, multiline, wraps), (_engine, multiline, wraps), static (style, state) => state._engine.CreateInnerEditorStyle(style, state.multiline, state.wraps));
        var editor = new ContainerBox(ContainerBoxKind.Normal, editorStyle, display, element: null) { IsTextControlInnerEditor = true };
        control.AppendChild(editor);
        if (value.Length > 0)
        {
            editor.AppendChild(new TextRun(editorStyle, element, pseudoElement: null, TextRunKind.Generated, node: null, value, 0, value.Length));
        }

        // As in the browsers, the inner editor always has a line (the line of the caret, which gives the baseline of the
        // control): the line of an empty value, and the line after the last line feed of the value of a textarea
        if (value.Length == 0 || (multiline && value[^1] == '\n'))
        {
            editor.AppendChild(new TextRun(editorStyle, element, pseudoElement: null, TextRunKind.LineBreak, node: null, "\n", 0, 1));
        }

        FixupContainer(editor);
        if (value.Length == 0 && TextControlLayout.GetPlaceholder(element) is { } placeholder)
        {
            var placeholderStyle = _engine.GetStyle(element, TextControlLayout.PlaceholderPseudoElement)
                .GetDerivedStyle((PlaceholderBoxStyleKey, style, multiline), (style, multiline), static (placeholder, state) => StyleEngine.CreatePlaceholderBoxStyle(placeholder, state.style, state.multiline));
            var placeholderBox = new ContainerBox(ContainerBoxKind.Normal, placeholderStyle, display, element, TextControlLayout.PlaceholderPseudoElement);
            control.AppendChild(placeholderBox);
            _boxes[(element, TextControlLayout.PlaceholderPseudoElement)] = placeholderBox;
            placeholderBox.AppendChild(new TextRun(placeholderStyle, element, TextControlLayout.PlaceholderPseudoElement, TextRunKind.Generated, node: null, placeholder, 0, placeholder.Length));
            FixupContainer(placeholderBox);
        }
    }

    /// <summary>
    /// Builds the children of the box of an element that holds its contents (its principal box, or the table grid box of a
    /// table): its <c>::marker</c>, <c>::before</c>, its children in the flat tree and its <c>::after</c>, then runs the fixups.
    /// </summary>
    private void BuildBoxContents(Element element, ComputedStyle style, Display display, ContainerBox contents)
    {
        if (contents.IsBlockContainer)
        {
            // The ::first-line and ::first-letter pseudo-elements exist when a rule styles them
            if (_engine.MayHavePseudoElementRules(element, "first-line") && _engine.GetStyle(element, "first-line") is { HasCascadedValues: true } firstLineStyle)
            {
                contents.FirstLineStyle = firstLineStyle;
            }

            // The ::first-letter boxes are created once the tree is built (the first formatted line may be in a descendant block)
            if (_engine.MayHavePseudoElementRules(element, "first-letter") && _engine.GetStyle(element, "first-letter").HasCascadedValues)
            {
                _firstLetterContainers.Add(contents);
            }
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets: only the frame and frameset children of a
        // frameset are rendered, in its grid (no pseudo-element, no text)
        if (element.IsHtml(TagId.Frameset))
        {
            for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
            {
                if (FramesetFormattingContext.IsFramesetChild(child))
                {
                    BuildElement(child, contents);
                }
            }

            FixupContainer(contents);
            return;
        }

        var isRoot = ReferenceEquals(element, _root);
        if (isRoot)
        {
            BuildRootScrollControlsBefore(element, style, contents);
        }

        if (display.IsListItem)
        {
            BuildMarker(element, contents);
        }

        // https://drafts.csswg.org/css-forms-1/#checkmark: the ::checkmark of an option rendered with base appearance generates
        // a box "as if it was immediately preceding the ::before pseudo-element"
        var hasBaseAppearanceChildren = element.TagId is TagId.Select or TagId.Option or TagId.Optgroup && HasBaseAppearanceChildren(element, style);
        if (hasBaseAppearanceChildren && element is HtmlOptionElement && style.SelectStyle is { } optionSelectStyle && _engine.AreContentsRenderedWithBaseAppearance(optionSelectStyle))
        {
            BuildPseudoElement(element, StyleEngine.Checkmark, contents);
        }

        if (element.TagId is TagId.Input or TagId.Meter or TagId.Progress && FormControlContents.HasGeneratedContents(element, style))
        {
            // (these controls have no ::before nor ::after pseudo-element)
            BuildGeneratedControlContents(element, style, contents);
            FixupContainer(contents);
            return;
        }

        BuildPseudoElement(element, "before", contents);
        if (element.IsHtml(TagId.Br))
        {
            // https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3: a br element renders as a line break
            contents.AppendChild(new TextRun(style, element, pseudoElement: null, TextRunKind.LineBreak, node: null, "\n", 0, 1));
        }
        else if (element.IsHtml(TagId.Wbr))
        {
            contents.AppendChild(new TextRun(style, element, pseudoElement: null, TextRunKind.WordBreakOpportunity, node: null, ((char)0x200B).ToString(), 0, 1));
        }
        else if (element is HtmlDetailsElement)
        {
            BuildDetailsChildren(element, contents);
        }
        else if (hasBaseAppearanceChildren)
        {
            BuildBaseAppearanceChildren(element, style, contents);
        }
        else
        {
            BuildChildNodes(element, style, contents);
        }

        // https://drafts.csswg.org/css-forms-1/#checkmark: "For checkboxes and radio elements, it generates a box as if it was
        // an child of its originating element, between the boxes generated by the ::before and ::after pseudo-element"
        if (element is HtmlInputElement { InputType: InputType.Checkbox or InputType.Radio } && StyleEngine.HasControlBaseAppearance(style))
        {
            BuildPseudoElement(element, StyleEngine.Checkmark, contents);
        }

        BuildPseudoElement(element, "after", contents);
        if (element is HtmlSelectElement select && StyleEngine.HasBaseAppearance(style) && StyleEngine.IsDropDownBox(select))
        {
            BuildSelectTrailingBoxes(select, contents);
        }

        if (isRoot)
        {
            BuildRootScrollControlsAfter(element, style, contents);
        }
        else if (_hasScopedViewTransitions && element.NodeDocument.GetScopedViewTransition(element)?.PseudoTree is { } viewTransitionTree)
        {
            BuildScopedViewTransitionLayer(element, viewTransitionTree, contents);
        }

        FixupContainer(contents);
    }

    /// <summary>
    /// Creates the principal box of an element or a pseudo-element, appended to the parent, and returns the box its contents
    /// go to: the box itself, or the table grid box of a table, whose principal box is a table wrapper box
    /// (https://drafts.csswg.org/css-tables-3/#table-wrapper-box).
    /// </summary>
    /// <summary>
    /// Applies <c>content-visibility</c> to the principal box of an element (https://drafts.csswg.org/css-contain-2/#content-visibility),
    /// when size containment can apply to it: <c>hidden</c> skips its contents; <c>auto</c> skips them when the element is not
    /// relevant to the user (updated before each layout pass, see <see cref="ContentRelevancy"/>). Records the boxes whose
    /// <c>contain-intrinsic-size</c> may remember their size (see <see cref="LastRememberedSizes"/>).
    /// </summary>
    private void ApplyContentVisibility(ContainerBox box, Element element, ComputedStyle style)
    {
        var contentVisibility = style.Layout.ContentVisibility;
        if (contentVisibility != ContentVisibility.Visible && UsedContainment.CanApplySizeContainment(box))
        {
            if (contentVisibility == ContentVisibility.Hidden)
            {
                box.SkipsContents = true;
            }
            else
            {
                box.IsContentVisibilityAuto = true;
                box.SkipsContents = !ContentRelevancy.IsRelevantToTheUser(element);
                _contentVisibilityAutoBoxes.Add(box);
            }
        }

        if (_mayUseContainIntrinsicSizeAuto && LastRememberedSizes.MayHaveAutoKeyword(style))
        {
            _containIntrinsicSizeAutoBoxes.Add(box);
        }
    }

    private static void ReplaceBox(List<ContainerBox> boxes, ContainerBox box, ContainerBox replacement)
    {
        var index = boxes.IndexOf(box);
        if (index >= 0)
        {
            boxes[index] = replacement;
        }
    }

    /// <summary>
    /// Returns whether an element is the rendered legend of its parent (https://html.spec.whatwg.org/multipage/rendering.html#rendered-legend):
    /// the first legend child of a fieldset that does not float and is not absolutely positioned.
    /// </summary>
    private bool IsRenderedLegend(Element element, ComputedStyle style)
    {
        if (!element.IsHtml(TagId.Legend) || element.ParentNode is not Element parent || !parent.IsHtml(TagId.Fieldset))
            return false;

        if (style.Layout.IsFloating || style.Layout.IsAbsolutelyPositioned)
            return false;

        for (var sibling = element.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
        {
            if (sibling is Element previous && previous.IsHtml(TagId.Legend) && _engine.GetStyle(previous) is { } previousStyle
                && !previousStyle.Layout.IsFloating && !previousStyle.Layout.IsAbsolutelyPositioned && !previousStyle.Layout.Display.IsNone)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns the display type of a replaced box (https://drafts.csswg.org/css-display-3/#layout-specific-display): "replaced
    /// elements with a display value of table-row, table-row-group, table-header-group, table-footer-group, table-column,
    /// table-column-group, table-cell, and table-caption are treated as inline-level boxes"; the ones with table or inline-table
    /// behave according to their outer display type (https://drafts.csswg.org/css-tables-3/#table-structure).
    /// </summary>
    private static Display GetReplacedDisplay(Display display)
    {
        if (display.IsInternalTableBox || display.Internal == DisplayInternal.TableCaption)
            return Display.Inline;

        return display.Inside == DisplayInside.Table ? display with { Inside = DisplayInside.FlowRoot } : display;
    }

    private ContainerBox CreatePrincipalBox(ComputedStyle style, Display display, Element element, string? pseudoElement, ContainerBox parent, out ContainerBox contents)
    {
        _usesRunIns |= display.Outside == DisplayOutside.RunIn;
        if (display.IsTable)
        {
            var wrapper = new ContainerBox(ContainerBoxKind.TableWrapper, style, display with { Inside = DisplayInside.FlowRoot }, element, pseudoElement);
            contents = new ContainerBox(ContainerBoxKind.TableGrid, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Table), element, pseudoElement);
            wrapper.AppendChild(contents);
            parent.AppendChild(wrapper);
            return wrapper;
        }

        // https://html.spec.whatwg.org/multipage/rendering.html#the-fieldset-and-legend-elements: the overflow and the columns
        // of a fieldset are the ones of its anonymous fieldset content box
        if (pseudoElement is null && element.IsHtml(TagId.Fieldset))
        {
            style = GetFieldsetStyles(style).Frame;
        }

        var box = new ContainerBox(ContainerBoxKind.Normal, style, display, element, pseudoElement);
        parent.AppendChild(box);
        contents = box;
        return box;
    }

    /// <summary>Returns the styles of the principal box and of the anonymous fieldset content box of a fieldset (see <see cref="StyleEngine.CreateFieldsetStyles"/>).</summary>
    private (ComputedStyle Frame, ComputedStyle Content) GetFieldsetStyles(ComputedStyle style)
    {
        if (!(_fieldsetStyles ??= new(ReferenceEqualityComparer.Instance)).TryGetValue(style, out var styles))
        {
            // "If the computed value of 'display' on the fieldset element is 'grid' or 'inline-grid', then set the used value to
            // 'grid'. If the computed value of 'display' on the fieldset element is 'flex' or 'inline-flex', then set the used
            // value to 'flex'. Otherwise, set the used value to 'flow-root'."
            var display = style.Layout.Display;
            var contentDisplay = display.Internal != DisplayInternal.None ? "flow-root" : display.Inside switch
            {
                DisplayInside.Grid => "grid",
                DisplayInside.Flex => "flex",
                _ => "flow-root",
            };

            // (the styles are kept with the style of the fieldset: the frame style is derived from it, and the content style from the frame style)
            var frame = style.GetDerivedStyle((FieldsetFrameStyleKey, contentDisplay), (_engine, contentDisplay), static (style, state) =>
            {
                var (frame, content) = state._engine.CreateFieldsetStyles(style, state.contentDisplay);
                frame.GetDerivedStyle(FieldsetContentStyleKey, content, static (_, content) => content);
                return frame;
            });
            styles = (frame, frame.GetDerivedStyle(FieldsetContentStyleKey, (_engine, style, contentDisplay), static (_, state) => state._engine.CreateFieldsetStyles(state.style, state.contentDisplay).Content));
            _fieldsetStyles[style] = styles;
            _fieldsetStyles[styles.Frame] = styles;
        }

        return styles;
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/rendering.html#anonymous-fieldset-content-box: "The anonymous fieldset content box
    /// is expected to appear after the rendered legend and is expected to contain the content (including the '::before' and
    /// '::after' pseudo-elements) of the fieldset element except for the rendered legend, if there is one." The principal box
    /// of the fieldset is a block container whose children are the rendered legend and the content box (laid out by
    /// <see cref="FieldsetFormattingContext"/>).
    /// </summary>
    private void WrapFieldsetContents(ContainerBox box)
    {
        var legend = BlockFormattingContext.FindRenderedLegend(box);
        var contentStyle = GetFieldsetStyles(box.Style).Content;
        // (an anonymous box of the normal kind: it may be a multi-column, flex or grid container and a scroll container)
        var content = new ContainerBox(ContainerBoxKind.Normal, contentStyle, contentStyle.Layout.Display with { Outside = DisplayOutside.Block }, element: null) { IsFieldsetContent = true };
        for (var child = box.FirstChild; child is not null;)
        {
            var next = child.NextSibling;
            if (!ReferenceEquals(child, legend))
            {
                box.RemoveChild(child);
                content.AppendChild(child);
            }

            child = next;
        }

        box.AppendChild(content);
        box.ChildrenAreInline = false;
        FixupContainer(content);
    }

    /// <summary>Appends the boxes of the contents of a display: contents element: its ::before, its children in the flat tree and its ::after.</summary>
    private void BuildContents(Element element, ComputedStyle style, ContainerBox parent)
    {
        BuildPseudoElement(element, "before", parent);
        BuildChildNodes(element, style, parent);
        BuildPseudoElement(element, "after", parent);
    }

    /// <summary>Appends the boxes of the children of an element in the flat tree: its shadow tree, the assigned nodes of a slot (or its fallback content), or its children.</summary>
    private void BuildChildNodes(Element element, ComputedStyle style, ContainerBox parent)
    {
        if (element.AttachedShadowRoot is { } shadowRoot)
        {
            BuildNodes(shadowRoot.FirstChild, element, style, parent);
        }
        else if (element is HtmlSlotElement { AssignedNodesList.Count: > 0 } slot)
        {
            foreach (var node in slot.AssignedNodesList)
            {
                BuildNode(node, element, style, parent);
            }
        }
        else
        {
            BuildNodes(element.FirstChild, element, style, parent);
        }
    }

    private void BuildNodes(Node? first, Element parentElement, ComputedStyle parentStyle, ContainerBox parent)
    {
        for (var node = first; node is not null; node = node.NextSibling)
        {
            BuildNode(node, parentElement, parentStyle, parent);
        }
    }

    private void BuildNode(Node node, Element parentElement, ComputedStyle parentStyle, ContainerBox parent)
    {
        switch (node)
        {
            case Element element:
                BuildElement(element, parent);
                break;

            // A text node is styled by its parent in the flat tree
            case Text { Data.Length: > 0 } text:
                var run = new TextRun(parentStyle, parentElement, pseudoElement: null, TextRunKind.Text, text, text.Data, 0, text.Data.Length);
                parent.AppendChild(run);
                _boxes[(text, null)] = run;
                break;
        }
    }

    /// <summary>
    /// The children of a details element (https://html.spec.whatwg.org/multipage/rendering.html#the-details-and-summary-elements):
    /// its summary (or, without summary child, the summary that the user agent provides, see <see cref="HtmlDetailsElement.ImplicitSummary"/>),
    /// then the other children in the box of its second slot, the <c>::details-content</c> pseudo-element
    /// (https://drafts.csswg.org/css-pseudo-4/#details-content-pseudo). The box has the computed <c>display</c> of the
    /// pseudo-element (a block in the user agent style sheet; <c>contents</c>: the children are children of the box of the details
    /// element; <c>none</c>: they have no box) and skips its contents with <c>content-visibility: hidden</c> (the user agent style
    /// sheet, while the details element is closed). Its <c>::before</c>, <c>::after</c>, <c>::first-line</c> and
    /// <c>::first-letter</c> pseudo-elements are generated (<c>content-visibility: auto</c> is not applied to it).
    /// </summary>
    private void BuildDetailsChildren(Element details, ContainerBox parent)
    {
        Element? summary = null;
        for (var child = details.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (child.IsHtml(TagId.Summary))
            {
                summary = child;
                BuildElement(child, parent);
                break;
            }
        }

        // A details element without summary child renders the summary that the user agent provides
        if (summary is null && details is HtmlDetailsElement { ImplicitSummary: { } implicitSummary })
        {
            BuildElement(implicitSummary, parent);
        }

        var contentStyle = _engine.GetStyle(details, StyleEngine.DetailsContent);
        var display = contentStyle.Layout.Display;
        if (display.IsNone)
            return;

        var contents = parent;
        ContainerBox? box = null;
        if (!display.IsContents)
        {
            box = CreatePrincipalBox(contentStyle, display, details, StyleEngine.DetailsContent, parent, out contents);
            _boxes[(details, StyleEngine.DetailsContent)] = box;
            box.SkipsContents = contentStyle.Layout.ContentVisibility == ContentVisibility.Hidden && UsedContainment.CanApplySizeContainment(box);
            if (contents.IsBlockContainer)
            {
                if (_engine.MayHavePseudoElementRules(details, StyleEngine.DetailsContentPrefix + "first-line") && _engine.GetStyle(details, StyleEngine.DetailsContentPrefix + "first-line") is { HasCascadedValues: true } firstLineStyle)
                {
                    contents.FirstLineStyle = firstLineStyle;
                }

                if (_engine.MayHavePseudoElementRules(details, StyleEngine.DetailsContentPrefix + "first-letter") && _engine.GetStyle(details, StyleEngine.DetailsContentPrefix + "first-letter").HasCascadedValues)
                {
                    _firstLetterContainers.Add(contents);
                }
            }
        }

        BuildPseudoElement(details, StyleEngine.DetailsContentPrefix + "before", contents);
        for (var node = details.FirstChild; node is not null; node = node.NextSibling)
        {
            if (!ReferenceEquals(node, summary))
            {
                BuildNode(node, details, contentStyle, contents);
            }
        }

        BuildPseudoElement(details, StyleEngine.DetailsContentPrefix + "after", contents);
        if (box is not null)
        {
            FixupContainer(contents);
            if (!ReferenceEquals(box, contents))
            {
                FixupContainer(box);
            }
        }
    }

    /// <summary>
    /// The box of an outermost svg element: a replaced box whose children are the boxes of the rendered <c>foreignObject</c>
    /// elements of its SVG subtree (each a block formatting context root laid out by the SVG layout), the rest being SVG boxes.
    /// </summary>
    private void BuildSvgRoot(Element svg, ComputedStyle style, Display display, ContainerBox parent)
    {
        var box = new ContainerBox(ContainerBoxKind.Replaced, style, GetReplacedDisplay(display), svg);
        parent.AppendChild(box);
        _boxes[(svg, null)] = box;
        ApplyContentVisibility(box, svg, style);
        BuildForeignObjects(svg, box);
    }

    private void BuildForeignObjects(Element element, ContainerBox svgBox)
    {
        for (var child = element.FirstElementChild; child is not null; child = child.NextElementSibling)
        {
            if (!BoxGeneration.IsSvgElement(child) || BoxGeneration.IsNeverRenderedSvgElement(child))
                continue;

            var style = _engine.GetStyle(child);
            if (style.Layout.Display.IsNone)
                continue;

            if (child.LocalName != "foreignObject")
            {
                BuildForeignObjects(child, svgBox);
                continue;
            }

            var box = new ContainerBox(ContainerBoxKind.Normal, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot), child);
            svgBox.AppendChild(box);
            _boxes[(child, null)] = box;
            BuildChildNodes(child, style, box);
            FixupContainer(box);
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Pseudo-elements and generated content (https://drafts.csswg.org/css-content-3/)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Appends the box of a ::before or ::after pseudo-element, generated when its content is not none (https://drafts.csswg.org/css-pseudo-4/#generated-content).</summary>
    private void BuildPseudoElement(Element element, string pseudoElement, ContainerBox parent)
    {
        if (_engine.MayHavePseudoElementRules(element, pseudoElement))
        {
            BuildPseudoElementBox(element, pseudoElement, parent);
        }
    }

    private void BuildPseudoElementBox(Element element, string pseudoElement, ContainerBox parent)
    {
        var style = _engine.GetStyle(element, pseudoElement);
        var content = style.GetComputedValue("content");
        if (content is "none" or "normal" or "")
            return;

        var display = style.Layout.Display;
        if (display.IsNone)
            return;

        if (display.IsContents)
        {
            // (the counters of a pseudo-element without box are not in the box tree, see CounterOperations)
            _hasUntrackedCounterOperations |= HasCounterOperations(style);
            AppendContent(parent, style, element, pseudoElement, content);
            return;
        }

        var box = CreatePrincipalBox(style, display, element, pseudoElement, parent, out var contents);
        _boxes[(element, pseudoElement)] = box;
        if (display.IsListItem)
        {
            // A ::before or ::after pseudo-element that is a list item has a marker (https://drafts.csswg.org/css-pseudo-4/#marker-pseudo:
            // "::before::marker"), which inherits from the pseudo-element
            BuildMarker(element, contents, pseudoElement);
        }

        AppendContent(contents, style, element, pseudoElement, content);
        FixupContainer(contents);
        if (!ReferenceEquals(box, contents))
        {
            FixupContainer(box);
        }
    }

    /// <summary>
    /// Appends the box of the ::marker of a list item (https://drafts.csswg.org/css-lists-3/#marker-pseudo): its content when
    /// it is not normal, otherwise its list-style-image when it is a valid image, or the marker string of its list-style-type (a string, or the
    /// representation of the list-item counter followed by the suffix of the counter style). An outside marker is an
    /// inline-block positioned by the layout; an inside marker is an inline box.
    /// </summary>
    /// <param name="element">The list item, or the originating element of the pseudo-element that is a list item.</param>
    /// <param name="parent">The box of the contents of the list item.</param>
    /// <param name="originatingPseudoElement">The ::before or ::after pseudo-element that is the list item, or null for the element.</param>
    private void BuildMarker(Element element, ContainerBox parent, string? originatingPseudoElement = null)
    {
        // The marker of a pseudo-element (::before::marker, which inherits from it) is a box of the pseudo-element (its texts
        // are generated content of it)
        var name = originatingPseudoElement ?? "marker";
        var style = _engine.GetStyle(element, originatingPseudoElement is null ? name : originatingPseudoElement + "::marker");
        var content = style.GetComputedValue("content");
        if (content is "none")
            return;

        // https://drafts.csswg.org/css-lists-3/#list-style-position-property: "If the list item is an inline box, this value is
        // equivalent to inside"
        var outside = style.Layout.Text.ListStylePosition == ListStylePosition.Outside && parent.Display is not { Outside: DisplayOutside.Inline, Inside: DisplayInside.Flow };
        var display = outside ? new Display(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.FlowRoot) : Display.Inline;
        ContainerBox box;
        if (content is not ("normal" or ""))
        {
            box = new ContainerBox(ContainerBoxKind.Normal, style, display, element, name) { IsOutsideMarker = outside };
            AppendContent(box, style, element, name, content);
        }
        else if (style.GetComputedValue("list-style-image") is { } image && image != "none" && !Om.CssImageFetching.IsInvalidImage(element.NodeDocument, image))
        {
            // https://drafts.csswg.org/css-lists-3/#image-marker: "If the specified image is valid, ...". The marker of a list item
            // whose image cannot be loaded is the one of its list-style-type (the boxes are built again when the fetch fails)
            box = new ContainerBox(ContainerBoxKind.Normal, style, display, element, name) { IsOutsideMarker = outside };
            box.AppendChild(new ContainerBox(ContainerBoxKind.Replaced, GetAnonymousStyle(style), Display.Inline, element, name) { GeneratedImage = image });
        }
        else if (GetMarkerString(element, style, originatingPseudoElement) is { Length: > 0 } text)
        {
            box = new ContainerBox(ContainerBoxKind.Normal, style, display, element, name) { IsOutsideMarker = outside };

            // The bullets are symbols, not text (the browsers draw them): as there, letter-spacing and word-spacing do not space them
            var textStyle = style;
            if (style.GetComputedValue("list-style-type") is "disc" or "circle" or "square" or "disclosure-open" or "disclosure-closed"
                && (style.GetComputedValue("letter-spacing") != "normal" || style.GetComputedValue("word-spacing") != "normal"))
            {
                textStyle = style.GetDerivedStyle(MarkerSymbolStyleKey, 0, static (style, _) => style.WithOverrides([new CssLonghandValue("letter-spacing", "normal"), new CssLonghandValue("word-spacing", "normal")]));
            }

            box.AppendChild(new TextRun(textStyle, element, name, TextRunKind.Generated, node: null, text, 0, text.Length));
        }
        else
        {
            return;
        }

        parent.AppendChild(box);
        if (originatingPseudoElement is null)
        {
            _boxes[(element, name)] = box;
        }

        FixupContainer(box);
    }

    /// <summary>The marker string of a list item (https://drafts.csswg.org/css-lists-3/#marker-string), or null when list-style-type is none.</summary>
    private string? GetMarkerString(Element element, ComputedStyle style, string? originatingPseudoElement = null)
    {
        var pseudoElement = originatingPseudoElement ?? "marker";
        return GetCounters(element, pseudoElement).GetMarkerString(element, style, pseudoElement);
    }

    /// <summary>
    /// Returns the counters of the document (computed once per build), to read the counters of an element or of a pseudo-element.
    /// A patch of the tree (see <see cref="TryPatch"/>) reads the counters of the previous build: it fails when the counters of
    /// the element were not computed by it (a new element).
    /// </summary>
    private CssCounters GetCounters(Element element, string? pseudoElement)
    {
        _counters ??= CssCounters.Compute(_root);
        if (_patch is not null && !_counters.HasCounters(element, pseudoElement))
        {
            _patch.Failed = true;
        }

        return _counters;
    }

    /// <summary>
    /// Appends the boxes of a content list (https://drafts.csswg.org/css-content-3/#content-values): the strings, the counters
    /// and the quotes are text runs, the images are replaced boxes; the alternative text (after the slash) is not rendered.
    /// The attr() functions are substituted by the computed value.
    /// </summary>
    private void AppendContent(ContainerBox parent, ComputedStyle style, Element element, string pseudoElement, string content)
    {
        _text.Clear();
        var values = CssParser.ParseListOfComponentValues(content, out var input);
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Whitespace }:
                    break;

                case CssPreservedToken { Type: CssTokenType.String } text:
                    _text.Append(text.Token.Value);
                    break;

                case CssPreservedToken slash when slash.Token.IsDelim('/'):
                    FlushContentText(parent, style, element, pseudoElement);
                    return;

                case CssPreservedToken { Type: CssTokenType.Ident } keyword:
                    AppendQuote(style, keyword.Token.Value);
                    break;

                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "counter") || InfraAscii.EqualsIgnoreAsciiCase(function.Name, "counters"):
                    _text.Append(GetCounterText(style, element, pseudoElement, function));
                    break;

                case CssPreservedToken { Type: CssTokenType.Url }:
                case CssFunction imageFunction when IsImageFunction(imageFunction.Name):
                    // An <image>: url(), image-set(), the gradients...
                    FlushContentText(parent, style, element, pseudoElement);
                    var image = input[value.Start..value.End];
                    parent.AppendChild(new ContainerBox(ContainerBoxKind.Replaced, GetAnonymousStyle(style), Display.Inline, element, pseudoElement) { GeneratedImage = image });
                    break;
            }
        }

        FlushContentText(parent, style, element, pseudoElement);
    }

    /// <summary>Whether a function is an &lt;image&gt; (https://drafts.csswg.org/css-images-4/#image-values, https://drafts.fxtf.org/filter-effects/#FilterCSSImageValue).</summary>
    internal static bool IsImageFunction(string name)
    {
        name = InfraAscii.ToAsciiLowercase(name);
        return name.EndsWith("gradient", StringComparison.Ordinal) || name is "url" or "src" or "image" or "image-set" or "-webkit-image-set" or "cross-fade" or "-webkit-cross-fade" or "element" or "paint" or "filter";
    }

    private void FlushContentText(ContainerBox parent, ComputedStyle style, Element element, string pseudoElement)
    {
        if (_text.Length == 0)
            return;

        var text = _text.ToString();
        _text.Clear();
        parent.AppendChild(new TextRun(style, element, pseudoElement, TextRunKind.Generated, node: null, text, 0, text.Length));
    }

    /// <summary>
    /// https://drafts.csswg.org/css-content-3/#quote-values: open-quote and close-quote are replaced by the quotes of the
    /// quotes property at the nesting level; no-open-quote and no-close-quote only change the level. A close-quote at the
    /// level 0 is not rendered.
    /// </summary>
    private void AppendQuote(ComputedStyle style, string keyword)
    {
        _usesQuotes = true;
        switch (InfraAscii.ToAsciiLowercase(keyword))
        {
            case "open-quote":
                _text.Append(GetQuote(style, _quoteDepth, open: true));
                _quoteDepth++;
                break;
            case "no-open-quote":
                _quoteDepth++;
                break;
            case "close-quote" when _quoteDepth > 0:
                _quoteDepth--;
                _text.Append(GetQuote(style, _quoteDepth, open: false));
                break;
            case "no-close-quote" when _quoteDepth > 0:
                _quoteDepth--;
                break;
        }
    }

    /// <summary>
    /// The quote of a nesting level (the last pair is used for the deeper levels). The auto value uses the quotation marks of
    /// the content language of the element (<see cref="LanguageQuotes"/>).
    /// </summary>
    internal static string GetQuote(ComputedStyle style, int depth, bool open)
    {
        var quotes = style.GetComputedValue("quotes");
        for (var current = style.Parent; quotes == "match-parent" && current is not null; current = current.Parent)
        {
            quotes = current.GetComputedValue("quotes");
        }

        if (quotes is "none")
            return "";

        if (quotes is "auto" or "match-parent" or "")
        {
            // The quotation marks of the content language of the element
            var marks = LanguageQuotes.Get(style.Element is { } element ? Selectors.HtmlElementStates.GetLanguage(element) : null);
            return marks[(depth == 0 ? 0 : 2) + (open ? 0 : 1)];
        }

        var strings = new List<string>();
        foreach (var value in CssParser.ParseListOfComponentValues(quotes))
        {
            if (value is CssPreservedToken { Type: CssTokenType.String } text)
            {
                strings.Add(text.Token.Value);
            }
        }

        if (strings.Count < 2)
            return "";

        var pair = Math.Min(depth, (strings.Count / 2) - 1);
        return strings[(pair * 2) + (open ? 0 : 1)];
    }

    /// <summary>The text of a counter() or counters() function (https://drafts.csswg.org/css-lists-3/#counter-functions).</summary>
    private string GetCounterText(ComputedStyle style, Element element, string pseudoElement, CssFunction function)
    {
        var isCounters = InfraAscii.EqualsIgnoreAsciiCase(function.Name, "counters");
        var arguments = ValueMatcher.SplitAtCommas(function.Value).ConvertAll(ValueMatcher.TrimWhitespace);
        if (arguments.Count == 0 || arguments[0] is not [CssPreservedToken { Type: CssTokenType.Ident } name])
            return "";

        string? separator = null;
        if (isCounters)
        {
            if (arguments.Count < 2 || arguments[1] is not [CssPreservedToken { Type: CssTokenType.String } separatorToken])
                return "";

            separator = separatorToken.Token.Value;
        }

        // https://drafts.csswg.org/css-lists-3/#counter-functions: "If the <counter-style> argument is omitted it defaults to decimal"
        var counters = GetCounters(element, pseudoElement);
        var styleIndex = isCounters ? 2 : 1;
        var counterStyle = (arguments.Count > styleIndex ? counters.ResolveCounterStyle(arguments[styleIndex], style, "content") : null) ?? CounterStyle.Decimal;
        return separator is not null
            ? counters.GetCounters(element, pseudoElement, name.Token.Value, separator, counterStyle, style)
            : counters.GetCounter(element, pseudoElement, name.Token.Value, counterStyle, style);
    }

    /// <summary>
    /// The &lt;content-replacement&gt; of an element (https://drafts.csswg.org/css-content-3/#typedef-content-content-replacement):
    /// the computed content of the element is a single &lt;image&gt;, or null.
    /// </summary>
    private static string? GetReplacementImage(ComputedStyle style)
    {
        // The content property is not inherited: it is normal unless it is declared or animated (most elements)
        if (!style.TryGetCascadedValue("content", out _) && !style.HasAnimationEffects)
            return null;

        var content = style.GetComputedValue("content");
        if (content is "normal" or "none" or "" || !content.Contains('(', StringComparison.Ordinal))
            return null;

        var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(content));
        return values switch
        {
            [CssPreservedToken { Type: CssTokenType.Url }] => content,
            [CssFunction function] when IsImageFunction(function.Name) => content,
            [CssPreservedToken { Type: CssTokenType.Url } or CssFunction, CssPreservedToken slash, ..] when slash.Token.IsDelim('/') && (values[0] is not CssFunction f || IsImageFunction(f.Name)) => content[..content.IndexOf('/', StringComparison.Ordinal)].TrimEnd(),
            _ => null,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // ::first-letter (https://drafts.csswg.org/css-pseudo-4/#first-letter-pseudo)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Creates the ::first-letter box of a block container: the first-letter text of its first formatted line (the first
    /// typographic letter unit, in a descendant block container when its first in-flow child is a block, with the punctuation
    /// that precedes and follows it) is wrapped in an inline box (a floating box when it floats). When the punctuation and
    /// the letter are not in the same text run (the quote of a <c>::before</c>, the text of an inline box), "the user agent
    /// may create a ::first-letter pseudo-element from one of the elements, both elements, or simply not create a
    /// pseudo-element" (https://drafts.csswg.org/css-pseudo-4/#first-letter-pseudo): as in the browsers, the box wraps the
    /// part of the first text run only (the punctuation that precedes a letter of a following run, or the letter without
    /// the punctuation of the following run). There is no first letter when the first formatted line starts with an atomic
    /// inline or a line break, or has no typographic letter unit after the punctuation.
    /// </summary>
    private void CreateFirstLetterBox(ContainerBox container)
    {
        if (container.Element is not { } element)
            return;

        var scanner = new FirstLetterScanner();
        ScanFirstLetterText(container, scanner);
        if (!scanner.LetterFound || scanner.Parts.Count == 0)
            return;

        var (run, start, end) = scanner.Parts[0];

        // https://drafts.csswg.org/css-pseudo-4/#first-letter-pseudo: the ::first-letter is inside the innermost element that
        // contains the letter (the fictional tag sequence <p><span><p::first-letter>T</p::first-letter>he...), also a
        // display: contents one: it inherits from this element, whose style the text run has; or inside the ::before or ::after
        // pseudo-element whose generated content has the letter
        var pseudoElement = container.PseudoElement switch
        {
            StyleEngine.DetailsContent => StyleEngine.DetailsContentPrefix + "first-letter",
            StyleEngine.SelectPicker => StyleEngine.SelectPickerPrefix + "first-letter",
            _ => "first-letter",
        };
        var style = _engine.GetStyle(element, pseudoElement);
        if (!ReferenceEquals(run.Style, style.Parent) && (run.PseudoElement is null ? !ReferenceEquals(run.Style.Element, element) : run.PseudoElement is "before" or "after"))
        {
            style = style.WithParent(run.Style);
        }

        var parent = run.Parent!;

        // https://drafts.csswg.org/css-inline-3/#initial-letter-styling: an initial letter box has its used style (it inherits
        // from the ::first-line pseudo-element of the block container)
        InitialLetterBox? initialLetter = null;
        if (style.GetComputedValue("initial-letter") is not ("normal" or ""))
        {
            var block = parent;
            while (block.IsInlineBox && block.Parent is { } ancestor)
            {
                block = ancestor;
            }

            var letterStyle = container.FirstLineStyle is { } firstLineStyle ? style.WithParent(firstLineStyle) : style;
            if (InitialLetter.CreateBoxStyle(letterStyle, block.Style, out initialLetter) is { } boxStyle)
            {
                style = boxStyle;
            }
        }

        var display = style.Layout.IsFloating ? Display.Block : Display.Inline;
        if (start > 0)
        {
            run = SplitRun(run, run.Start + start);
            end -= start;
        }

        if (end < run.Length)
        {
            SplitRun(run, run.Start + end);
        }

        // The text of the first letter has the style of the ::first-letter pseudo-element
        run.Style = style;
        var box = new ContainerBox(ContainerBoxKind.Normal, style, display, element, "first-letter");
        box.ChildrenAreInline = box.IsBlockContainer;
        if (initialLetter is not null)
        {
            box.InitialLetter = initialLetter;
        }

        parent.InsertBefore(box, run);
        box.AppendChild(run);
        _boxes.TryAdd((element, pseudoElement), box);
    }

    /// <summary>The state of the search of the first-letter text in the text runs that start the first formatted line.</summary>
    private sealed class FirstLetterScanner
    {
        /// <summary>The ranges of the text runs (relative to the start of each run) that form the first-letter text.</summary>
        public List<(TextRun Run, int Start, int End)> Parts { get; } = [];

        /// <summary>Whether the typographic letter unit was found (otherwise the parts are punctuation that precedes it).</summary>
        public bool LetterFound { get; private set; }

        /// <summary>Whether a text run that is not collapsible white space was scanned: the content that follows is not at the start of the line.</summary>
        public bool HasContent { get; private set; }

        /// <summary>Scans the next text run of the line; returns whether the first-letter text may continue in the following run.</summary>
        public bool Add(TextRun run)
        {
            var text = run.Text;
            var preservesBreaks = run.Style.Layout.WhiteSpaceCollapse is WhiteSpaceCollapse.Preserve or WhiteSpaceCollapse.PreserveBreaks or WhiteSpaceCollapse.BreakSpaces;
            if (!HasContent && TextRun.IsWhiteSpace(text))
            {
                // The white space that starts the line is not rendered; a preserved line feed ends the first line
                return !preservesBreaks || !text.Contains('\n');
            }

            var letterFound = LetterFound;
            var result = ScanFirstLetterText(text, isLineStart: !HasContent, preservesBreaks, IsDutch(run.Element), ref letterFound, out var start, out var end);
            LetterFound = letterFound;
            HasContent = true;
            if (end > start)
            {
                Parts.Add((run, start, end));
            }

            return result;
        }

        /// <summary>https://drafts.csswg.org/css-pseudo-4/#first-letter-pattern: the Dutch digraph IJ is a typographic letter unit when the content language is Dutch.</summary>
        private static bool IsDutch(Element? element)
            => element is not null && Selectors.HtmlElementStates.GetLanguage(element) is { Length: >= 2 } language
                && language.StartsWith("nl", StringComparison.OrdinalIgnoreCase) && (language.Length == 2 || language[2] == '-');
    }

    /// <summary>
    /// Scans the text runs that start the first formatted line of a box, in tree order, until the first-letter text ends;
    /// returns whether the line may continue after the box. The first formatted line of a block container whose first
    /// in-flow child is a block container is the one of that block, even when it has no line
    /// (https://drafts.csswg.org/css-pseudo-4/#first-formatted-line): the content that follows the block is not on it.
    /// </summary>
    private static bool ScanFirstLetterText(ContainerBox box, FirstLetterScanner scanner)
    {
        if (box.SkipsContents)
            return false;

        foreach (var child in box.Children)
        {
            switch (child)
            {
                case TextRun text:
                    if (text.Kind == TextRunKind.LineBreak)
                        return false;

                    if (text.Kind == TextRunKind.WordBreakOpportunity)
                        continue;

                    if (!scanner.Add(text))
                        return false;

                    continue;

                case ContainerBox container:
                    if (container.IsOutOfFlow || container.PseudoElement == "marker")
                        continue;

                    if (container.IsInlineBox)
                    {
                        if (!ScanFirstLetterText(container, scanner))
                            return false;

                        continue;
                    }

                    if (!scanner.HasContent && container.IsBlockLevel && container.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous && container.IsBlockContainer)
                    {
                        // The anonymous block of an inside marker that is followed by a block is not the first formatted line
                        if (IsMarkerBlock(container))
                            continue;

                        ScanFirstLetterText(container, scanner);
                    }

                    return false;
            }
        }

        return true;
    }

    /// <summary>Whether a box is an anonymous block that only contains the inside marker of its list item.</summary>
    private static bool IsMarkerBlock(ContainerBox box)
    {
        if (!box.IsAnonymous || box.FirstChild is null)
            return false;

        foreach (var child in box.Children)
        {
            if (child is not ContainerBox { PseudoElement: "marker" })
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the range of the first letter in a text (https://drafts.csswg.org/css-pseudo-4/#first-letter-pattern), or null
    /// when the text does not start with a first-letter text that has a typographic letter unit.
    /// </summary>
    internal static (int Start, int End)? GetFirstLetterRange(ReadOnlySpan<char> text, bool isDutch = false)
    {
        var letterFound = false;
        ScanFirstLetterText(text, isLineStart: true, preservesBreaks: false, isDutch, ref letterFound, out var start, out var end);
        return letterFound ? (start, end) : null;
    }

    /// <summary>
    /// Scans a text for a part of the first-letter text (https://drafts.csswg.org/css-pseudo-4/#first-letter-pattern). Before
    /// the letter (<paramref name="letterFound"/> is false): the white space that starts the line is skipped, then the
    /// punctuation (any P* category) and the spaces (Zs, except U+3000) that precede the letter are included, then the
    /// typographic letter unit: the grapheme cluster of a letter, a number or a symbol (with its combining marks, or an Indic
    /// conjunct; a control character is rendered as a symbol, https://drafts.csswg.org/css-text-4/#white-space-processing),
    /// or the Dutch digraph IJ. After the letter: the punctuation other than the opening punctuation and the dashes (Pc, Pe,
    /// Pi, Pf, Po), with the typographic spaces before it (Zs other than U+3000 and the word separators). The punctuation
    /// characters are grapheme clusters too. Returns whether the range reaches the end of the text, so that the first-letter
    /// text may continue in the next text.
    /// </summary>
    private static bool ScanFirstLetterText(ReadOnlySpan<char> text, bool isLineStart, bool preservesBreaks, bool isDutch, ref bool letterFound, out int start, out int end)
    {
        var i = 0;
        start = 0;
        end = 0;
        if (!letterFound)
        {
            if (isLineStart)
            {
                while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\r' or '\f')
                {
                    // A preserved line feed ends the first line: the letter is not on it
                    if (text[i] == '\n' && preservesBreaks)
                        return false;

                    i++;
                }
            }

            start = i;
            end = i;
            while (i < text.Length && GetCategory(text, i) is var category && (IsPunctuation(category) || (category == UnicodeCategory.SpaceSeparator && text[i] != '\U00003000')))
            {
                i = GraphemeClusters.GetNextBoundary(text, i);
            }

            end = i;
            if (i >= text.Length)
                return true;

            if (!IsTypographicLetterUnit(text, i))
            {
                end = start;
                return false;
            }

            // The typographic letter unit is a grapheme cluster (https://drafts.csswg.org/css-text-4/#characters), as Chrome: the
            // letter with its combining marks, and the conjuncts of the Indic scripts (UAX #29 rule GB9c, e.g. Devanagari KA,
            // VIRAMA, SSA)
            var letter = i;
            i = GraphemeClusters.GetNextBoundary(text, i);
            if (isDutch && i == letter + 1 && i < text.Length && ((text[letter] == 'I' && text[i] == 'J') || (text[letter] == 'i' && text[i] == 'j')))
            {
                i = GraphemeClusters.GetNextBoundary(text, i);
            }

            letterFound = true;
        }

        while (true)
        {
            var next = i;
            while (next < text.Length && GetCategory(text, next) == UnicodeCategory.SpaceSeparator && !IsWordSeparator(text, next) && text[next] != '\U00003000')
            {
                next += CharLength(text, next);
            }

            if (next >= text.Length || GetCategory(text, next) is not (UnicodeCategory.ConnectorPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation))
            {
                break;
            }

            i = GraphemeClusters.GetNextBoundary(text, next);
        }

        end = i;
        return i >= text.Length;
    }

    private static bool IsTypographicLetterUnit(ReadOnlySpan<char> text, int index) => GetCategory(text, index) switch
    {
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber
            or UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol => true,
        UnicodeCategory.Control => text[index] is not ('\t' or '\n' or '\r' or '\f'),
        _ => false,
    };

    private static bool IsPunctuation(UnicodeCategory category) => category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation;

    /// <summary>
    /// https://drafts.csswg.org/css-text-4/#word-separator: the space, the no-break space, the Ethiopic word space, the Aegean
    /// word separators, the Ugaritic word divider and the Phoenician word separator.
    /// </summary>
    private static bool IsWordSeparator(ReadOnlySpan<char> text, int index)
        => (CharLength(text, index) == 2 ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index]) is 0x20 or 0xA0 or 0x1361 or 0x10100 or 0x10101 or 0x1039F or 0x1091F;

    private static UnicodeCategory GetCategory(ReadOnlySpan<char> text, int index) => CharLength(text, index) == 2
        ? CharUnicodeInfo.GetUnicodeCategory(char.ConvertToUtf32(text[index], text[index + 1]))
        : CharUnicodeInfo.GetUnicodeCategory(text[index]);

    private static int CharLength(ReadOnlySpan<char> text, int index) => char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]) ? 2 : 1;

    /// <summary>Splits a text run at an offset of its data: the run keeps the text before it, the returned run (inserted after it) has the rest.</summary>
    private static TextRun SplitRun(TextRun run, int offset)
    {
        var rest = new TextRun(run.Style, run.Element, run.PseudoElement, run.Kind, run.Node, run.Data, offset, run.End - offset)
        {
            NextRunOfNode = run.NextRunOfNode,
        };
        run.Length = offset - run.Start;
        run.NextRunOfNode = rest;
        run.Parent!.InsertBefore(rest, run.NextSibling);
        return rest;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Fixups
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The key of the style of the anonymous boxes in the styles derived from the style of their parent (see <see cref="ComputedStyle.GetDerivedStyle"/>).</summary>
    private static readonly object AnonymousStyleKey = new();

    /// <summary>The key of the style of the principal box of a fieldset, derived from the style of the fieldset (with the display of its content box).</summary>
    private static readonly object FieldsetFrameStyleKey = new();

    /// <summary>The key of the style of the anonymous fieldset content box, derived from the style of the principal box of the fieldset.</summary>
    private static readonly object FieldsetContentStyleKey = new();

    /// <summary>The key of the style of the inner editor of a text control, derived from the style of the control (with whether it is multiline and wraps).</summary>
    private static readonly object InnerEditorStyleKey = new();

    /// <summary>The key of the style of the box of the ::placeholder of a text control, derived from the style of the pseudo-element (with the style of the control).</summary>
    private static readonly object PlaceholderBoxStyleKey = new();

    /// <summary>The key of the style of the symbol of a list marker (a bullet), which letter-spacing and word-spacing do not space.</summary>
    private static readonly object MarkerSymbolStyleKey = new();

    private ComputedStyle GetAnonymousStyle(ComputedStyle parent)
    {
        // https://drafts.csswg.org/css-writing-modes-4/#principal-flow: the anonymous boxes of the root element inherit its used
        // writing mode and direction (the principal writing mode, set by each build of the tree), not its computed ones: their
        // style is created by each build. The anonymous styles of the other styles are kept with them
        if (!parent.IsRoot)
            return parent.GetDerivedStyle(AnonymousStyleKey, _engine, static (style, engine) => engine.CreateAnonymousStyle(style));

        if (!_anonymousStyles.TryGetValue(parent, out var anonymousStyle))
        {
            anonymousStyle = _engine.CreateAnonymousStyle(parent);
            anonymousStyle.Layout.SetPrincipalWritingMode(parent.Layout.WritingMode, parent.Layout.Direction);
            _anonymousStyles[parent] = anonymousStyle;
        }

        return anonymousStyle;
    }

    private ContainerBox CreateAnonymousBox(ContainerBox parent, Display display) => new(ContainerBoxKind.Anonymous, GetAnonymousStyle(parent.Style), display, element: null);

    /// <summary>Runs the fixups of a box whose children are built: ruby, tables, then the anonymous boxes of its formatting context.</summary>
    private void FixupContainer(ContainerBox box)
    {
        if (IsBlockLevelRubyContainer(box))
        {
            WrapBlockRubyContents(box);
        }

        if (BlockFormattingContext.IsFieldset(box))
        {
            WrapFieldsetContents(box);
            return;
        }

        // https://drafts.css-houdini.org/css-layout-api-1/#layout-api-containers: the boxes laid out by a registered layout class
        if (TryFixupLayoutApiContainer(box))
            return;

        if (box.Kind == ContainerBoxKind.Replaced || box.IsEmpty)
        {
            if (box.IsBlockContainer)
            {
                box.ChildrenAreInline = !box.IsEmpty;
            }

            return;
        }

        var display = box.Display;
        var isRuby = box.Kind != ContainerBoxKind.TableGrid && (display.Inside == DisplayInside.Ruby || display.IsInternalRubyBox);
        if (isRuby || IsRunInFlowBox(box))
        {
            // https://drafts.csswg.org/css-display/#run-in-layout: "A run-in box with display: flow inlinifies its contents"
            InlinifyChildren(box);
        }

        FixupTableChildren(box);
        FixupRubyChildren(box, isRuby);

        if (box.Kind == ContainerBoxKind.TableWrapper)
        {
            FixupTableWrapper(box);
        }
        else if (MathLayoutBoxes.GeneratesAnonymousMrow(box))
        {
            // https://w3c.github.io/mathml-core/#anonymous-mrow-boxes: msqrt, mpadded and mtd wrap their children in an anonymous mrow
            var mrow = CreateAnonymousBox(box, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Math));
            while (box.FirstChild is { } child)
            {
                mrow.AppendChild(child);
            }

            box.AppendChild(mrow);
            box.ChildrenAreInline = false;
            FixupMathContainer(mrow);
        }
        else if (box.Kind != ContainerBoxKind.TableGrid && display.Inside == DisplayInside.Math)
        {
            FixupMathContainer(box);
        }
        else if (box.IsBlockContainer)
        {
            FixupBlockContainer(box);
        }
        else if (box.Kind != ContainerBoxKind.TableGrid && (display.IsFlexContainer || display.IsGridContainer))
        {
            FixupFlexOrGridContainer(box);
        }
        else if (box.IsInlineBox || display.Outside == DisplayOutside.RunIn)
        {
            // The inline boxes that contain block-level boxes are split by their block container
            foreach (var child in box.Children)
            {
                if (IsInFlowBlockLevel(child) || (child is ContainerBox inline && _inlinesWithBlocks.Contains(inline)))
                {
                    _inlinesWithBlocks.Add(box);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The fixup of a MathML box (https://w3c.github.io/mathml-core/#box-model): the token elements lay out their contents as a
    /// block container (https://w3c.github.io/mathml-core/#layout-of-mtext); the children of the other MathML boxes are laid out
    /// by the math layout, which only lays out boxes: their text is not rendered and their inline boxes are blockified (an mspace
    /// renders no children).
    /// </summary>
    private void FixupMathContainer(ContainerBox box)
    {
        if (MathLayoutBoxes.IsTokenElement(box))
        {
            FixupBlockContainer(box);
            return;
        }

        box.ChildrenAreInline = false;
        var isSpace = MathLayoutBoxes.GetKind(box) == MathBoxKind.Space;
        for (var child = box.FirstChild; child is not null;)
        {
            var next = child.NextSibling;

            // The children of an mspace (an empty element) are not rendered
            if (child is TextRun || isSpace)
            {
                box.RemoveChild(child);
            }
            else if (child is ContainerBox { IsInlineBox: true } inline)
            {
                inline.Display = inline.Display with { Outside = DisplayOutside.Block };
                _inlinesWithBlocks.Remove(inline);
                if (IsBlockLevelRubyContainer(inline))
                {
                    WrapBlockRubyContents(inline);
                }

                FixupBlockContainer(inline);
            }

            child = next;
        }
    }

    private static bool IsInFlowBlockLevel(LayoutBox box) => box is ContainerBox { IsOutsideMarker: false } container && container.Display.IsBlockLevel && !container.IsOutOfFlow;

    /// <summary>
    /// The fixup of a block container (https://drafts.csswg.org/css2/#anonymous-block-level): the inline boxes that contain
    /// in-flow block-level boxes are split around them, then, when the container has in-flow block-level children, each
    /// sequence of inline-level content is wrapped in an anonymous block box. A sequence that contains only collapsible
    /// white space and out-of-flow boxes is not wrapped: the white space is not rendered and the out-of-flow boxes stay
    /// children of the container.
    /// </summary>
    private void FixupBlockContainer(ContainerBox box)
    {
        // The run-in boxes and the inline boxes that contain block-level boxes rebuild the list of the children
        foreach (var child in box.Children)
        {
            if (child is ContainerBox container && (container.Display.Outside == DisplayOutside.RunIn || _inlinesWithBlocks.Contains(container)))
            {
                RestructureInlineChildren(box);
                break;
            }
        }

        var hasBlock = false;
        var hasInlineLevel = false;
        foreach (var child in box.Children)
        {
            hasBlock |= IsInFlowBlockLevel(child);
            hasInlineLevel |= child is TextRun || (child is ContainerBox { IsOutsideMarker: false } container && !container.IsOutOfFlow);
        }

        // The container establishes (or continues) an inline formatting context when it has in-flow inline-level children
        if (!hasBlock)
        {
            box.ChildrenAreInline = hasInlineLevel;
            return;
        }

        // Each sequence of inline-level content between the block-level children is wrapped in an anonymous block
        box.ChildrenAreInline = false;
        LayoutBox? runStart = null;
        var runHasContent = false;
        for (var child = box.FirstChild; ; child = child.NextSibling)
        {
            if (child is null || IsInFlowBlockLevel(child) || child is ContainerBox { IsOutsideMarker: true })
            {
                if (runStart is not null)
                {
                    WrapInlineRun(box, runStart, child, runHasContent);
                    runStart = null;
                    runHasContent = false;
                }

                if (child is null)
                    break;

                continue;
            }

            runStart ??= child;
            runHasContent |= IsInlineContent(child);
        }
    }

    /// <summary>Splits the inline boxes that contain block-level boxes, then moves or wraps the run-in sequences.</summary>
    private void RestructureInlineChildren(ContainerBox box)
    {
        var children = box.DetachChildren();
        var result = children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not ContainerBox inline || !_inlinesWithBlocks.Contains(inline))
                continue;

            // The parts of the split inline boxes and their blocks replace the inline boxes in the list of the children
            result = new List<LayoutBox>(children.Count + 2);
            for (var j = 0; j < children.Count; j++)
            {
                if (children[j] is ContainerBox split && _inlinesWithBlocks.Contains(split))
                {
                    SplitInline(split, result);
                }
                else
                {
                    result.Add(children[j]);
                }
            }

            break;
        }

        FixupRunIns(box, result);
        foreach (var child in result)
        {
            box.AppendChild(child);
        }
    }

    /// <summary>Whether a box is inline-level content of a line: not out of flow, not an outside marker, not collapsible white space.</summary>
    private static bool IsInlineContent(LayoutBox box) => box switch
    {
        TextRun text => !text.IsCollapsibleWhiteSpace,
        ContainerBox container => !container.IsOutOfFlow && !container.IsOutsideMarker,
        _ => false,
    };

    /// <summary>
    /// Wraps the children from <paramref name="start"/> to <paramref name="end"/> (excluded) in an anonymous block when they
    /// have inline content; otherwise they are collapsible white space (not rendered: removed) and out-of-flow boxes (kept).
    /// </summary>
    private void WrapInlineRun(ContainerBox box, LayoutBox start, LayoutBox? end, bool hasContent)
    {
        if (hasContent)
        {
            var anonymous = CreateAnonymousBox(box, Display.Block);
            anonymous.ChildrenAreInline = true;
            box.InsertBefore(anonymous, start);
            for (var item = start; !ReferenceEquals(item, end);)
            {
                var next = item!.NextSibling;
                anonymous.AppendChild(item);
                item = next;
            }
        }
        else
        {
            for (var item = start; !ReferenceEquals(item, end);)
            {
                var next = item!.NextSibling;
                if (item is TextRun)
                {
                    box.RemoveChild(item);
                }

                item = next;
            }
        }
    }

    /// <summary>
    /// Splits an inline box that contains in-flow block-level boxes (https://drafts.csswg.org/css2/#anonymous-block-level):
    /// the inline box is broken around the block-level boxes (and the consecutive ones, separated by collapsible white space
    /// or out-of-flow boxes), into parts linked by <see cref="ContainerBox.Continuation"/>, even when a part is empty. The
    /// parts and the block-level boxes are appended to the output.
    /// </summary>
    private void SplitInline(ContainerBox inline, List<LayoutBox> output)
    {
        var children = inline.DetachChildren();
        var last = inline;
        ContainerBox? current = inline;
        List<LayoutBox>? pending = null;

        foreach (var child in children)
        {
            if (child is ContainerBox nested && _inlinesWithBlocks.Contains(nested))
            {
                var parts = new List<LayoutBox>();
                SplitInline(nested, parts);
                foreach (var part in parts)
                {
                    Place(part);
                }
            }
            else
            {
                Place(child);
            }
        }

        if (current is null)
        {
            StartPart();
        }

        output.Add(current!);

        void StartPart()
        {
            current = new ContainerBox(inline.Kind, inline.Style, inline.Display, inline.Element, inline.PseudoElement) { PreviousContinuation = last };
            last.Continuation = current;
            last = current;
            if (pending is not null)
            {
                foreach (var item in pending)
                {
                    current.AppendChild(item);
                }

                pending = null;
            }
        }

        void Place(LayoutBox child)
        {
            if (IsInFlowBlockLevel(child))
            {
                if (current is not null)
                {
                    output.Add(current);
                    current = null;
                }
                else if (pending is not null)
                {
                    // Between consecutive block-level boxes: the white space is not rendered
                    foreach (var item in pending)
                    {
                        if (item is ContainerBox pendingBox)
                        {
                            pendingBox.SplitInlineParent ??= last;
                            output.Add(item);
                        }
                    }

                    pending = null;
                }

                // The block is a child of the block container, but it is still in the inline box (its relative offset, its
                // containing block); a block of a nested split inline box keeps its innermost inline box
                var block = (ContainerBox)child;
                block.SplitInlineParent ??= last;
                output.Add(child);
            }
            else if (current is null && !IsInlineContent(child))
            {
                (pending ??= []).Add(child);
            }
            else
            {
                if (current is null)
                {
                    StartPart();
                }

                current!.AppendChild(child);
            }
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-display/#run-in-layout: a run-in sequence (the consecutive run-in boxes with the white space
    /// and the out-of-flow boxes between them) immediately followed by a block box that does not establish a formatting context
    /// is inserted at the start of the block box, after its marker; the white space and the out-of-flow boxes between the
    /// sequence and the block box do not prevent it and stay where they are. Otherwise "an anonymous block box is generated
    /// around the run-in sequence and all immediately following inline-level content (up to, but not including, the next run-in
    /// sequence, if any)". In both cases the run-in boxes are then inline-level boxes. The sequences are those of the box tree
    /// before any of them moves: a sequence inserted in a block box does not join the run-in boxes this block box starts with.
    /// </summary>
    private void FixupRunIns(ContainerBox parent, List<LayoutBox> children)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (!IsRunIn(children[i]))
                continue;

            // The sequence ends with its last run-in box; the white space and the out-of-flow boxes that follow it are skipped
            var last = i;
            var end = i + 1;
            for (; end < children.Count; end++)
            {
                if (IsRunIn(children[end]))
                {
                    last = end;
                }
                else if (IsInlineContent(children[end]))
                {
                    break;
                }
            }

            for (var j = i; j <= last; j++)
            {
                if (IsRunIn(children[j]))
                {
                    children[j].Display = children[j].Display with { Outside = DisplayOutside.Inline };
                }
            }

            if (end < children.Count && children[end] is ContainerBox block && IsRunInTarget(block))
            {
                // The marker of the block box stays before the run-in boxes
                var reference = block.FirstChild;
                while (reference is ContainerBox { PseudoElement: "marker" } marker && ReferenceEquals(marker.Element, block.Element))
                {
                    reference = reference.NextSibling;
                }

                var target = block;
                if (!block.ChildrenAreInline && reference is not null)
                {
                    // The block box has block-level children: the sequence starts the anonymous block of its first line, unless
                    // this one wraps the run-in sequence of the block box itself
                    if (reference is ContainerBox { IsAnonymous: true, ChildrenAreInline: true } anonymous && !_runInBlocks.Contains(anonymous))
                    {
                        target = anonymous;
                        reference = anonymous.FirstChild;
                    }
                    else
                    {
                        target = CreateAnonymousBox(block, Display.Block);
                        block.InsertBefore(target, reference);
                        reference = null;
                    }
                }

                for (var j = i; j <= last; j++)
                {
                    target.InsertBefore(children[j], reference);
                }

                target.ChildrenAreInline = true;
                children.RemoveRange(i, last - i + 1);
                i--;
                continue;
            }

            // The sequence is not followed by a block box: it starts an anonymous block with the inline-level content that follows
            end = last + 1;
            while (end < children.Count && !IsRunIn(children[end]) && !IsInFlowBlockLevel(children[end]) && children[end] is not ContainerBox { IsOutsideMarker: true })
            {
                end++;
            }

            // The white space and the out-of-flow boxes that end the anonymous block stay in the container
            while (end > last + 1 && !IsInlineContent(children[end - 1]))
            {
                end--;
            }

            // The inside marker of the container stays on the line of the content it precedes
            var start = i;
            while (start > 0 && children[start - 1] is ContainerBox { PseudoElement: "marker", IsOutsideMarker: false } marker && ReferenceEquals(marker.Element, parent.Element))
            {
                start--;
            }

            var wrapper = CreateAnonymousBox(parent, Display.Block);
            wrapper.ChildrenAreInline = true;
            for (var j = start; j < end; j++)
            {
                wrapper.AppendChild(children[j]);
            }

            _runInBlocks.Add(wrapper);
            children.RemoveRange(start, end - start);
            children.Insert(start, wrapper);
            i = start;
        }

        static bool IsRunIn(LayoutBox box) => box is ContainerBox { Display.Outside: DisplayOutside.RunIn };
    }

    /// <summary>Whether a box is a run-in box whose inner display type is flow: it inlinifies its contents.</summary>
    private static bool IsRunInFlowBox(ContainerBox box)
        => box.Kind is ContainerBoxKind.Normal && box.Display is { Outside: DisplayOutside.RunIn, Inside: DisplayInside.Flow, Internal: DisplayInternal.None };

    /// <summary>Whether a box can receive a run-in sequence: an in-flow block box (a block container whose inner display type is flow) that does not establish an independent formatting context.</summary>
    private static bool IsRunInTarget(ContainerBox box)
    {
        if (box.Kind != ContainerBoxKind.Normal || !box.Display.IsBlockLevel || box.Display.Inside != DisplayInside.Flow || box.Display.Internal != DisplayInternal.None || box.IsOutOfFlow)
            return false;

        var layout = box.Style.Layout;
        return !layout.IsScrollContainer && !layout.HasLayoutContainment && !layout.HasPaintContainment && !layout.IsMultiColumnContainer
            && (layout.ContainerType & (ContainerType.Size | ContainerType.InlineSize)) == ContainerType.Normal;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-flexbox-1/#flex-items, https://drafts.csswg.org/css-grid-2/#grid-items: each in-flow child
    /// is an item (the children are blockified by the computed display), and each sequence of text runs (and of other
    /// inline-level boxes, e.g. the images of the generated content) is wrapped in an anonymous block item, unless it
    /// contains only white space.
    /// </summary>
    private void FixupFlexOrGridContainer(ContainerBox box)
    {
        var children = box.DetachChildren();
        var run = new List<LayoutBox>();
        foreach (var child in children)
        {
            if (child is TextRun || (child is ContainerBox container && container.Display.IsInlineLevel && !container.Style.Layout.IsAbsolutelyPositioned))
            {
                run.Add(child);
                continue;
            }

            FlushItemRun(box, run);
            box.AppendChild(child);
        }

        FlushItemRun(box, run);
    }

    private void FlushItemRun(ContainerBox box, List<LayoutBox> run)
    {
        if (run.Count == 0)
            return;

        if (run.Exists(item => item is not TextRun text || !TextRun.IsWhiteSpace(text.Text)))
        {
            var anonymous = CreateAnonymousBox(box, Display.Block);
            anonymous.ChildrenAreInline = true;
            foreach (var item in run)
            {
                anonymous.AppendChild(item);
            }

            box.AppendChild(anonymous);
        }

        run.Clear();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Table fixup (https://drafts.csswg.org/css-tables-3/#fixup-algorithm)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>A table-root box: the table grid box (the table wrapper box is generated with it).</summary>
    private static bool IsTableRoot(LayoutBox box) => box is ContainerBox { Kind: ContainerBoxKind.TableGrid };

    /// <summary>A proper table child (https://drafts.csswg.org/css-tables-3/#proper-table-child): a row group, a row, a column, a column group or a caption.</summary>
    private static bool IsProperTableChild(LayoutBox box) => box is ContainerBox { Kind: not ContainerBoxKind.TableWrapper } container
        && container.Display.Internal is DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup or DisplayInternal.TableRow
            or DisplayInternal.TableColumn or DisplayInternal.TableColumnGroup or DisplayInternal.TableCaption;

    private static bool IsTableCell(LayoutBox box) => box is ContainerBox { Display.Internal: DisplayInternal.TableCell };

    private static bool IsTableRow(LayoutBox box) => box is ContainerBox { Display.Internal: DisplayInternal.TableRow };

    /// <summary>A table-non-root box (https://drafts.csswg.org/css-tables-3/#table-non-root-box): a proper table child or a cell.</summary>
    private static bool IsTableNonRoot(LayoutBox box) => IsProperTableChild(box) || IsTableCell(box);

    /// <summary>A tabular container (https://drafts.csswg.org/css-tables-3/#tabular-container): a row, a row group or a table-root.</summary>
    private static bool IsTabularContainer(ContainerBox box) => IsTableRoot(box) || box.Display.Internal is DisplayInternal.TableRow or DisplayInternal.TableRowGroup or DisplayInternal.TableHeaderGroup or DisplayInternal.TableFooterGroup;

    /// <summary>Whether a box is a white space only "anonymous inline box" of the table fixup: a text run that contains only white space.</summary>
    private static bool IsWhiteSpaceRun(LayoutBox? box) => box is TextRun { Kind: TextRunKind.Text } text && TextRun.IsWhiteSpace(text.Text);

    private void FixupTableChildren(ContainerBox box)
    {
        var isTabular = IsTabularContainer(box);
        var hasTableParts = isTabular || box.Display.Internal == DisplayInternal.TableColumnGroup;
        if (!hasTableParts)
        {
            foreach (var child in box.Children)
            {
                if (IsTableNonRoot(child))
                {
                    hasTableParts = true;
                    break;
                }
            }

            if (!hasTableParts)
                return;
        }

        // 1. Remove the irrelevant boxes
        for (var child = box.FirstChild; child is not null;)
        {
            var next = child.NextSibling;
            if (box.Display.Internal == DisplayInternal.TableColumnGroup && box.Kind != ContainerBoxKind.TableGrid && child is not ContainerBox { Display.Internal: DisplayInternal.TableColumn })
            {
                box.RemoveChild(child);
            }
            else if (IsWhiteSpaceRun(child))
            {
                // Consecutive white space runs (e.g. separated by a comment) are one run
                var previous = child.PreviousSibling;
                while (previous is not null && IsWhiteSpaceRun(previous))
                {
                    previous = previous.PreviousSibling;
                }

                var following = next;
                while (following is not null && IsWhiteSpaceRun(following))
                {
                    following = following.NextSibling;
                }

                var betweenNonRoots = previous is not null && following is not null && IsTableNonRoot(previous) && IsTableNonRoot(following);
                var atEdgeOfTabular = isTabular && (previous is null || following is null) && (previous is null || IsTableNonRoot(previous)) && (following is null || IsTableNonRoot(following));
                if (betweenNonRoots || atEdgeOfTabular)
                {
                    box.RemoveChild(child);
                }
            }

            child = next;
        }

        // 2. Generate the missing child wrappers
        if (IsTableRoot(box))
        {
            WrapSequences(box, child => !IsProperTableChild(child), new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, DisplayInternal.TableRow));
        }
        else if (box.Display.IsTableRowGroup)
        {
            WrapSequences(box, child => !IsTableRow(child), new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, DisplayInternal.TableRow));
        }
        else if (box.Display.Internal == DisplayInternal.TableRow)
        {
            WrapSequences(box, child => !IsTableCell(child), new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.FlowRoot, DisplayInternal.TableCell));
        }

        // 3. Generate the missing parents: the rows of the cells, then the tables of the proper table children
        if (box.Display.Internal != DisplayInternal.TableRow)
        {
            WrapSequences(box, IsTableCell, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Flow, DisplayInternal.TableRow));
        }

        if (!IsTableRoot(box))
        {
            WrapMisparentedTableChildren(box);
        }
    }

    /// <summary>Wraps each sequence of consecutive children that match the predicate (white space runs between them included) in an anonymous box.</summary>
    private void WrapSequences(ContainerBox box, Func<LayoutBox, bool> predicate, Display display)
    {
        for (var child = box.FirstChild; child is not null;)
        {
            if (!predicate(child))
            {
                child = child.NextSibling;
                continue;
            }

            var anonymous = CreateAnonymousBox(box, display);
            box.InsertBefore(anonymous, child);
            while (child is not null && (predicate(child) || (IsWhiteSpaceRun(child) && child.NextSibling is { } next && predicate(next))))
            {
                var next2 = child.NextSibling;
                anonymous.AppendChild(child);
                child = next2;
            }

            FixupContainer(anonymous);
            child = anonymous.NextSibling;
        }
    }

    /// <summary>
    /// Wraps each sequence of consecutive misparented proper table children in an anonymous table (a table wrapper box and
    /// a table grid box), inline-level when the parent is an inline box, a ruby box or a run-in box.
    /// </summary>
    private void WrapMisparentedTableChildren(ContainerBox box)
    {
        var isRowGroup = box.Display.IsTableRowGroup;
        var isColumnGroup = box.Display.Internal == DisplayInternal.TableColumnGroup;
        bool IsMisparented(LayoutBox child) => child is ContainerBox container && IsProperTableChild(container) && container.Display.Internal switch
        {
            DisplayInternal.TableRow => !isRowGroup,
            DisplayInternal.TableColumn => !isColumnGroup,
            _ => true,
        };

        var inline = box.IsInlineBox || box.Display.Outside == DisplayOutside.RunIn || box.Display.Inside == DisplayInside.Ruby || box.Display.IsInternalRubyBox;
        for (var child = box.FirstChild; child is not null;)
        {
            if (!IsMisparented(child))
            {
                child = child.NextSibling;
                continue;
            }

            var style = GetAnonymousStyle(box.Style);
            var tableWrapper = new ContainerBox(ContainerBoxKind.TableWrapper, style, new Display(DisplayBox.Normal, inline ? DisplayOutside.Inline : DisplayOutside.Block, DisplayInside.FlowRoot), element: null);
            var grid = new ContainerBox(ContainerBoxKind.TableGrid, style, new Display(DisplayBox.Normal, DisplayOutside.Block, DisplayInside.Table), element: null);
            tableWrapper.AppendChild(grid);
            box.InsertBefore(tableWrapper, child);
            while (child is not null && (IsMisparented(child) || (IsWhiteSpaceRun(child) && child.NextSibling is { } next && IsMisparented(next))))
            {
                var next2 = child.NextSibling;
                grid.AppendChild(child);
                child = next2;
            }

            FixupContainer(grid);
            FixupContainer(tableWrapper);
            child = tableWrapper.NextSibling;
        }
    }

    /// <summary>
    /// The children of a table wrapper box are the table grid box and the captions
    /// (https://drafts.csswg.org/css-tables-3/#table-wrapper-box): the captions are moved from the grid box to the wrapper,
    /// before the grid box (caption-side: top) or after it (bottom).
    /// </summary>
    private static void FixupTableWrapper(ContainerBox wrapper)
    {
        if (wrapper.FirstChild is not ContainerBox { Kind: ContainerBoxKind.TableGrid } grid)
            return;

        for (var child = grid.FirstChild; child is not null;)
        {
            var next = child.NextSibling;
            if (child is ContainerBox { Display.Internal: DisplayInternal.TableCaption } caption)
            {
                if (caption.Style.Layout.Table.CaptionSide == CaptionSide.Bottom)
                {
                    wrapper.AppendChild(caption);
                }
                else
                {
                    wrapper.InsertBefore(caption, grid);
                }
            }

            child = next;
        }

        wrapper.ChildrenAreInline = false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Ruby fixup (https://drafts.csswg.org/css-ruby-1/#box-fixup)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// https://drafts.csswg.org/css-display/#inlinify: the in-flow block-level children of a ruby container or of an internal
    /// ruby box are inlinified (a block container becomes an inline-block, a table an inline-table...). "If an inline box (display:
    /// inline) is inlinified, it recursively inlinifies all of its in-flow children, so that no block-level descendants break up the
    /// inline formatting context in which it participates": the inline boxes in them are no longer split around blocks.
    /// </summary>
    private void InlinifyChildren(ContainerBox box)
    {
        foreach (var child in box.Children)
        {
            if (child is not ContainerBox container || container.IsOutOfFlow)
                continue;

            if (container.Display.Outside == DisplayOutside.Block && container.Display.Internal == DisplayInternal.None)
            {
                container.Display = container.Display with { Outside = DisplayOutside.Inline, Inside = container.Display.Inside == DisplayInside.Flow ? DisplayInside.FlowRoot : container.Display.Inside };
            }
            else if (container.Display.Outside == DisplayOutside.RunIn && container.Display.Internal == DisplayInternal.None)
            {
                // An inlinified run-in box is an inline-level box (its contents are already inlinified when it is an inline box)
                container.Display = container.Display with { Outside = DisplayOutside.Inline };
            }
            else if (container.IsInlineBox && _inlinesWithBlocks.Remove(container))
            {
                InlinifyChildren(container);
            }
        }
    }

    /// <summary>Whether a box is a block-level ruby container (<c>display: block ruby</c>, or a blockified ruby container).</summary>
    private static bool IsBlockLevelRubyContainer(ContainerBox box)
        => box.Kind is ContainerBoxKind.Normal or ContainerBoxKind.Anonymous && box.Display is { Inside: DisplayInside.Ruby, Internal: DisplayInternal.None } && box.Display.Outside != DisplayOutside.Inline;

    /// <summary>
    /// https://drafts.csswg.org/css-ruby-1/#block-ruby: "If an element has an outer display type of block and an inner display
    /// type of ruby, it generates two boxes: a principal box of the outer display type, and an inline-level ruby container. The
    /// principal box is a block container, the ruby container its only child" (its outside marker stays in the block container).
    /// </summary>
    private void WrapBlockRubyContents(ContainerBox box)
    {
        box.Display = box.Display with { Inside = DisplayInside.Flow };
        if (box.IsEmpty)
            return;

        var ruby = CreateAnonymousBox(box, new Display(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Ruby));
        for (var child = box.FirstChild; child is not null;)
        {
            var next = child.NextSibling;
            if (child is not ContainerBox { IsOutsideMarker: true })
            {
                ruby.AppendChild(child);
            }

            child = next;
        }

        box.AppendChild(ruby);
        FixupContainer(ruby);
    }

    private static bool IsRubyAnnotation(LayoutBox box) => box is ContainerBox { Display.Internal: DisplayInternal.RubyText };

    private static bool IsRubyContainer(ContainerBox box) => box.Display.Inside == DisplayInside.Ruby && box.Display.Internal == DisplayInternal.None && box.Kind != ContainerBoxKind.TableGrid;

    /// <summary>
    /// The anonymous ruby boxes: the improperly contained ruby bases, annotations and level containers are wrapped in an
    /// anonymous ruby container; the text and inline-level boxes directly in a ruby container or a ruby base container are
    /// wrapped in anonymous ruby bases (in anonymous ruby annotations in a ruby annotation container), with the white space
    /// around them. A white space only sequence (https://drafts.csswg.org/css-ruby-1/#anon-gen-interpret-space) is removed at
    /// the start and at the end and before an annotation that does not follow an annotation (inter-level white space); between
    /// two bases (two annotations) it is intra-level white space, wrapped in an anonymous ruby base (annotation) flagged
    /// <see cref="ContainerBox.IsRubyWhiteSpace"/>; between the segments, it stays in the ruby container. The anonymous level
    /// containers are not generated: the layout pairs the bases and the annotations.
    /// </summary>
    private void FixupRubyChildren(ContainerBox box, bool isRuby)
    {
        var display = box.Display;
        bool IsImproperlyContained(LayoutBox child) => child is ContainerBox { Display.Internal: var type } && type switch
        {
            DisplayInternal.RubyBase => !IsRubyContainer(box) && display.Internal != DisplayInternal.RubyBaseContainer,
            DisplayInternal.RubyText => !IsRubyContainer(box) && display.Internal != DisplayInternal.RubyTextContainer,
            DisplayInternal.RubyBaseContainer or DisplayInternal.RubyTextContainer => !IsRubyContainer(box),
            _ => false,
        };

        if (box.Kind != ContainerBoxKind.TableGrid)
        {
            WrapRubySequences(box, IsImproperlyContained, new Display(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Ruby));
        }

        if (!isRuby)
            return;

        DisplayInternal? wrapperType = IsRubyContainer(box) || display.Internal == DisplayInternal.RubyBaseContainer ? DisplayInternal.RubyBase
            : display.Internal == DisplayInternal.RubyTextContainer ? DisplayInternal.RubyText
            : null;
        if (wrapperType is not { } type)
            return;

        static bool IsRubyInternal(LayoutBox child) => child is ContainerBox { Display.IsInternalRubyBox: true };

        // The white space at the start and at the end is removed (as in Gecko, before the white space around the content is
        // wrapped with it)
        while (box.FirstChild is { } first && IsWhiteSpaceRun(first))
        {
            box.RemoveChild(first);
        }

        while (box.LastChild is { } last && IsWhiteSpaceRun(last))
        {
            box.RemoveChild(last);
        }

        // Each sequence of the children that are not internal ruby boxes: a sequence with content (with the white space around it)
        // is wrapped in an anonymous base (annotation); a white space only sequence is intra-ruby white space
        for (var child = box.FirstChild; child is not null;)
        {
            if (IsRubyInternal(child))
            {
                child = child.NextSibling;
                continue;
            }

            var previous = child.PreviousSibling;
            var end = child;
            var hasContent = false;
            while (end is not null && !IsRubyInternal(end))
            {
                hasContent |= !IsWhiteSpaceRun(end);
                end = end.NextSibling;
            }

            if (hasContent)
            {
                var anonymous = CreateAnonymousBox(box, new Display(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Flow, type));
                MoveRubySequence(box, anonymous, child, end);
                FixupContainer(anonymous);
            }
            else if (previous is null || end is null || (IsRubyAnnotation(end) && !IsRubyAnnotation(previous)) || end is ContainerBox { Display.Internal: DisplayInternal.RubyTextContainer })
            {
                // The inter-level white space (before an annotation) is removed
                while (child != end)
                {
                    var next = child!.NextSibling;
                    box.RemoveChild(child);
                    child = next;
                }
            }
            else if (previous is ContainerBox { Display.Internal: var previousType and (DisplayInternal.RubyBase or DisplayInternal.RubyText) } && end is ContainerBox { Display.Internal: var nextType } && nextType == previousType)
            {
                // The intra-level white space: between two bases, or between two annotations
                var anonymous = CreateAnonymousBox(box, new Display(DisplayBox.Normal, DisplayOutside.Inline, DisplayInside.Flow, previousType));
                anonymous.IsRubyWhiteSpace = true;
                MoveRubySequence(box, anonymous, child, end);
            }

            child = end;
        }
    }

    /// <summary>Moves the children of a box from <paramref name="first"/> to <paramref name="end"/> (excluded) into a new anonymous box inserted in their place.</summary>
    private static void MoveRubySequence(ContainerBox box, ContainerBox anonymous, LayoutBox first, LayoutBox? end)
    {
        box.InsertBefore(anonymous, first);
        for (var child = first; child != end;)
        {
            var next = child!.NextSibling;
            anonymous.AppendChild(child);
            child = next;
        }
    }

    private void WrapRubySequences(ContainerBox box, Func<LayoutBox, bool> predicate, Display display)
    {
        for (var child = box.FirstChild; child is not null;)
        {
            if (!predicate(child))
            {
                child = child.NextSibling;
                continue;
            }

            var anonymous = CreateAnonymousBox(box, display);
            box.InsertBefore(anonymous, child);
            while (child is not null && (predicate(child) || (IsWhiteSpaceRun(child) && child.NextSibling is { } next && predicate(next))))
            {
                var next2 = child.NextSibling;
                anonymous.AppendChild(child);
                child = next2;
            }

            FixupContainer(anonymous);
            child = anonymous.NextSibling;
        }
    }
}
