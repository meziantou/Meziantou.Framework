using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The incremental update of the box tree: the boxes of the elements that changed (see BoxTreeChanges) are built again and replace
// their previous boxes, the other boxes are kept.
internal sealed partial class BoxTreeBuilder
{
    /// <summary>The maximum number of subtrees built by a patch (a root that cannot be built alone is replaced by its parent): beyond it, the tree is built again.</summary>
    private const int MaxPatchAttempts = 256;

    /// <summary>The state of a patch of the tree (see <see cref="TryPatch"/>) when the builder builds the boxes of a subtree, or null.</summary>
    private PatchState? _patch;

    /// <summary>Whether the content of a pseudo-element uses quotes (their depth of nesting continues from the previous boxes in the tree order).</summary>
    private bool _usesQuotes;

    /// <summary>Whether a box is a run-in box (it is moved into the next block box by the fixup of its parent).</summary>
    private bool _usesRunIns;

    /// <summary>Whether a pseudo-element that does not generate a box (display: contents) sets, resets or increments a counter.</summary>
    private bool _hasUntrackedCounterOperations;

    private sealed class PatchState(BoxTree tree, StyleEngine engine, HashSet<Element> changed)
    {
        /// <summary>Whether the subtree read the counters of an element that the counters of the previous build do not have.</summary>
        public bool Failed { get; set; }

        /// <summary>
        /// The boxes of the elements of the subtree that did not change, kept (with their descendants and the state the layout
        /// attached to them), and the boxes that stand for them in the new boxes until they replace them (same kind, style and
        /// display: the fixups of their parent give the same boxes).
        /// </summary>
        public List<(ContainerBox Placeholder, ContainerBox Box)> Kept { get; } = [];

        /// <summary>
        /// Appends a placeholder of the boxes of an element when they can be kept: the element and its descendants did not change
        /// (it is not one of the changed elements nor an ancestor of one), its principal box could be built alone (see
        /// <see cref="CanBePatchRoot"/>) and has the style of the element.
        /// </summary>
        public bool TryKeep(Element element, ContainerBox parent)
        {
            if (changed.Contains(element) || !CanBePatchRoot(tree, engine, element) || tree.GetPrincipalBox(element) is not { } box || !ReferenceEquals(box.Style, engine.GetStyle(element)))
                return false;

            for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
            {
                if (BuildsContentsWithAncestor(ancestor))
                    return false;
            }

            var placeholder = new ContainerBox(box.Kind, box.Style, box.Display, element);
            parent.AppendChild(placeholder);
            Kept.Add((placeholder, box));
            return true;
        }
    }

    /// <summary>The boxes of an element built by a patch, which replace its previous principal box.</summary>
    private sealed record PatchedSubtree(Element Root, ContainerBox Previous, ContainerBox Box, ContainerBox TemporaryParent, BoxTreeBuilder Builder, HashSet<LayoutBox> Kept);

    /// <summary>Marks the tree as not patchable when the boxes built by this builder depend on boxes that a patch may not build again (see <see cref="BoxTree.IsPatchable"/>).</summary>
    private void RecordUnpatchableFeatures(BoxTree tree)
    {
        if (!IsPatchable())
        {
            tree.IsPatchable = false;
        }
    }

    private bool IsPatchable()
        => !_usesQuotes && !_usesRunIns && !_hasUntrackedCounterOperations && _firstLetterContainers.Count == 0 && _scrollControls is null && _reachedTopLayer is null
            && _reachedPickers is null && !_hasScopedViewTransitions && _counters is not { HasPseudoElementOperations: true };

    /// <summary>
    /// Patches the box tree of a document after the changes recorded by its style engine (see <see cref="BoxTreeChanges"/>): the
    /// boxes of each element that changed (its styles or the styles of its pseudo-elements were discarded, its attributes or its
    /// children changed) and of its descendants are built again, and replace its previous principal box in its parent box; the
    /// other boxes are kept (with the state the layout attached to them, see <see cref="LayoutResultCache"/>), and so are the boxes
    /// of the elements of a rebuilt subtree that did not change (see <see cref="PatchState.TryKeep"/>). Returns false when the
    /// tree must be built again: the tree is then unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The boxes of an element can be built alone when the boxes of its parent do not depend on them beyond their display: its
    /// principal box is a box of the element (not an inline box, which its block container splits around its block-level
    /// descendants, nor a run-in box) whose parent box keeps it as it is (not a ruby container or a MathML box, whose fixups
    /// change the display of their children). The new boxes must be one box of the same kind, display and positioning (the
    /// fixups of the parent box, e.g. the anonymous block boxes around the inline-level boxes, are the same). Otherwise, the
    /// boxes of the parent element are built again. The elements whose generation of boxes depends on their ancestors (the
    /// legends, the summaries, the contents of the select elements) are built with them; the root element, and the body element
    /// when its styles changed (they are propagated to the viewport), are built with the whole tree.
    /// </para>
    /// <para>
    /// The counters of the previous build are kept: the patch fails when the counter operations of the boxes it builds again
    /// (counter-reset, counter-increment, counter-set, the list items, the style containment) are not the ones of the previous
    /// boxes, or when a new element or pseudo-element reads the counters. The trees that use quotes, run-in boxes,
    /// <c>::first-letter</c> boxes, scroll navigation controls, the top layer or view transitions are built again.
    /// </para>
    /// </remarks>
    public static bool TryPatch(BoxTree tree, Document document, BoxTreeChanges changes)
    {
        if (changes.IsFull || !tree.IsPatchable || tree.Viewport is null || document.DocumentElement is not { } documentElement)
            return false;

        if (document.TopLayerElements is { Count: > 0 } || document.HasScopedViewTransitions || document.ViewTransitionPseudoTree is not null || document.HasShadowRoots)
            return false;

        var engine = document.GetStyleEngine();
        var roots = new HashSet<Element>(ReferenceEqualityComparer.Instance);

        // The changed elements and their ancestors: the boxes of the other elements can be kept
        var changed = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        foreach (var element in changes.Elements)
        {
            // (the boxes of a removed element are removed with the boxes of its parent, whose children changed)
            if (!element.IsConnected || !ReferenceEquals(element.NodeDocument, document) || IsInUnrenderedSubtree(engine, element))
                continue;

            if (FindPatchRoot(tree, engine, element, documentElement) is not { } root)
                return false;

            roots.Add(root);
            for (var ancestor = element; ancestor is not null && changed.Add(ancestor); ancestor = ancestor.ParentElement)
            {
            }
        }

        // The subtrees are built (a root that cannot be built alone is replaced by the root of its parent), then the tree is changed
        var subtrees = new Dictionary<Element, PatchedSubtree>(ReferenceEqualityComparer.Instance);
        var attempts = 0;
        while (true)
        {
            foreach (var root in roots.ToArray())
            {
                if (HasAncestorIn(roots, root))
                {
                    roots.Remove(root);
                    subtrees.Remove(root);
                }
            }

            Element? failed = null;
            foreach (var root in roots)
            {
                if (subtrees.ContainsKey(root))
                    continue;

                if (++attempts > MaxPatchAttempts)
                    return false;

                if (BuildPatchedSubtree(tree, engine, changed, root, documentElement) is not { } subtree)
                {
                    failed = root;
                    break;
                }

                subtrees.Add(root, subtree);
            }

            if (failed is null)
                break;

            roots.Remove(failed);
            if (failed.ParentElement is not { } parent || FindPatchRoot(tree, engine, parent, documentElement) is not { } parentRoot)
                return false;

            roots.Add(parentRoot);
            for (var ancestor = failed; ancestor is not null && changed.Add(ancestor); ancestor = ancestor.ParentElement)
            {
            }
        }

        var contentVisibilityChanged = false;
        foreach (var subtree in subtrees.Values)
        {
            ReplaceSubtree(tree, subtree);
            var builder = subtree.Builder;
            contentVisibilityChanged |= builder._contentVisibilityAutoBoxes.Count > 0 || builder._containIntrinsicSizeAutoBoxes.Count > 0;
            tree.Counters ??= builder._counters;
        }

        // The boxes of the collapsible white space removed by the fixups stay in the boxes by node (as in a build): the ones of the
        // removed text nodes are pruned when they may be many
        if (tree.Boxes.Count > (2 * tree.PrunedBoxCount) + 256)
        {
            PruneDisconnectedNodes(tree);
        }

        // The lists of boxes are in tree order: they are collected again
        if (contentVisibilityChanged || tree.ContentVisibilityAutoBoxes.Count > 0 || tree.ContainIntrinsicSizeAutoBoxes.Count > 0)
        {
            CollectContentVisibilityBoxes(tree, engine.MayHaveContentVisibility());
        }

        return true;
    }

    /// <summary>Returns whether an ancestor of an element is not rendered (display: none): the element has no box, and the changes of its ancestors are recorded with them.</summary>
    private static bool IsInUnrenderedSubtree(StyleEngine engine, Element element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (engine.GetStyle(ancestor).Layout.Display.IsNone)
                return true;
        }

        return false;
    }

    private static bool HasAncestorIn(HashSet<Element> roots, Element element)
    {
        for (var ancestor = element.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (roots.Contains(ancestor))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the element whose boxes are built again for a change of an element: the element or its nearest ancestor whose
    /// boxes can be built alone (see <see cref="CanBePatchRoot"/>) and that is not in an element whose contents are built with it
    /// (see <see cref="BuildsContentsWithAncestor"/>), or null when the whole tree must be built again (the root element, or the
    /// body element whose styles changed).
    /// </summary>
    private static Element? FindPatchRoot(BoxTree tree, StyleEngine engine, Element element, Element documentElement)
    {
        Element? candidate = null;
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, documentElement))
                return candidate;

            if (BuildsContentsWithAncestor(current))
            {
                candidate = null;
            }
            else if (candidate is null && CanBePatchRoot(tree, engine, current))
            {
                candidate = current;
            }
        }

        return null;
    }

    /// <summary>Whether the boxes of the contents of an element depend on the element or its ancestors: the contents of the select, datalist, option and optgroup elements (built in the picker of a select element).</summary>
    private static bool BuildsContentsWithAncestor(Element element)
        => element.IsHtmlNamespace && element.TagId is TagId.Select or TagId.Datalist or TagId.Option or TagId.Optgroup;

    /// <summary>Whether the boxes of an element can be built alone and replace its principal box (see <see cref="TryPatch"/>).</summary>
    private static bool CanBePatchRoot(BoxTree tree, StyleEngine engine, Element element)
    {
        // The legends (the rendered legend of a fieldset) and the summaries (the summary of a details element) depend on their
        // parent; the root element is built with the whole tree
        if (!element.IsHtmlNamespace || element.TagId is TagId.Legend or TagId.Summary or TagId.Html or TagId.Head)
            return false;

        if (tree.GetPrincipalBox(element) is not { } box || box.PseudoElement is not null || !ReferenceEquals(box.Element, element))
            return false;

        // The styles of the body element are propagated to the viewport (its writing mode, its overflow): the tree is built again
        // when they change
        if (element.TagId == TagId.Body && !ReferenceEquals(box.Style, engine.GetStyle(element)))
            return false;

        if (box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Replaced or ContainerBoxKind.TableWrapper))
            return false;

        if (box.Parent is not { } parent || box.Continuation is not null || box.PreviousContinuation is not null || box.IsInTopLayer || box.IsInlineBox || box.Display.Outside == DisplayOutside.RunIn)
            return false;

        // The fixups of the ruby containers, of the MathML boxes and of the run-in boxes change the display of their children;
        // the boxes laid out by a layout API class are wrapped by the fixup of their container
        var parentDisplay = parent.Display;
        if (parent.Kind != ContainerBoxKind.TableGrid && (parentDisplay.Inside is DisplayInside.Ruby or DisplayInside.Math || parentDisplay.IsInternalRubyBox || parentDisplay.Outside == DisplayOutside.RunIn))
            return false;

        return parent.LayoutApi is null && box.LayoutApi is null;
    }

    /// <summary>Builds the boxes of an element in a temporary parent, or returns null when they cannot replace its principal box (see <see cref="TryPatch"/>).</summary>
    private static PatchedSubtree? BuildPatchedSubtree(BoxTree tree, StyleEngine engine, HashSet<Element> changed, Element root, Element documentElement)
    {
        var previous = tree.GetPrincipalBox(root)!;
        var patch = new PatchState(tree, engine, changed);
        var builder = new BoxTreeBuilder(documentElement) { _counters = tree.Counters, _patch = patch };
        var parent = new ContainerBox(ContainerBoxKind.Anonymous, previous.Parent!.Style, Display.Block, element: null);
        builder.BuildElement(root, parent);
        if (patch.Failed || !builder.IsPatchable())
            return null;

        if (parent.FirstChild is not ContainerBox box || !ReferenceEquals(box, parent.LastChild) || !ReferenceEquals(builder._boxes.GetValueOrDefault((root, null)), box))
            return null;

        if (box.Kind != previous.Kind || box.Display != previous.Display || box.IsOutOfFlow != previous.IsOutOfFlow || box.Continuation is not null)
            return null;

        // The counters of the previous build are valid while the counter operations are the same (the kept boxes do not change)
        var kept = new HashSet<LayoutBox>(ReferenceEqualityComparer.Instance);
        foreach (var (_, keptBox) in patch.Kept)
        {
            kept.Add(keptBox);
        }

        if (!CounterOperationsEqual(previous, box, kept))
            return null;

        return new PatchedSubtree(root, previous, box, parent, builder, kept);
    }

    /// <summary>Replaces the principal box of the root of a patched subtree by its new boxes.</summary>
    private static void ReplaceSubtree(BoxTree tree, PatchedSubtree subtree)
    {
        var (root, previous, box, temporaryParent, builder, kept) = subtree;
        var boxes = tree.Boxes;
        List<Element>? firstLetterAncestors = null;
        RemoveBoxes(boxes, previous, root, ref firstLetterAncestors, kept);
        boxes.Remove((root, null));

        // The kept boxes replace their placeholders (they are moved from the previous boxes)
        foreach (var (placeholder, keptBox) in builder._patch!.Kept)
        {
            var placeholderParent = placeholder.Parent!;
            placeholderParent.InsertBefore(keptBox, placeholder);
            placeholderParent.RemoveChild(placeholder);
        }

        var parent = previous.Parent!;
        temporaryParent.RemoveChild(box);
        parent.InsertBefore(box, previous);
        parent.RemoveChild(previous);

        // The boxes that contain the new boxes are laid out again
        LayoutResultCache.Invalidate(parent);
        foreach (var (key, value) in builder._boxes)
        {
            boxes[key] = value;
        }
    }

    private static void PruneDisconnectedNodes(BoxTree tree)
    {
        List<(Node, string?)>? disconnected = null;
        foreach (var key in tree.Boxes.Keys)
        {
            if (!key.Node.IsConnected)
            {
                (disconnected ??= []).Add(key);
            }
        }

        if (disconnected is not null)
        {
            foreach (var key in disconnected)
            {
                tree.Boxes.Remove(key);
            }
        }

        tree.PrunedBoxCount = tree.Boxes.Count;
    }

    /// <summary>Collects the boxes of the lists of the tree that depend on content-visibility and contain-intrinsic-size, in tree order.</summary>
    private static void CollectContentVisibilityBoxes(BoxTree tree, bool mayUseContainIntrinsicSizeAuto)
    {
        tree.ContentVisibilityAutoBoxes.Clear();
        tree.ContainIntrinsicSizeAutoBoxes.Clear();
        Collect(tree, tree.Viewport!, mayUseContainIntrinsicSizeAuto);

        static void Collect(BoxTree tree, ContainerBox parent, bool mayUseContainIntrinsicSizeAuto)
        {
            foreach (var child in parent.Children)
            {
                if (child is not ContainerBox box)
                    continue;

                if (box.Element is { } element && box.PseudoElement is null && ReferenceEquals(tree.GetPrincipalBox(element), box))
                {
                    if (box.IsContentVisibilityAuto)
                    {
                        tree.ContentVisibilityAutoBoxes.Add(box);
                    }

                    if (mayUseContainIntrinsicSizeAuto && LastRememberedSizes.MayHaveAutoKeyword(box.Style))
                    {
                        tree.ContainIntrinsicSizeAutoBoxes.Add(box);
                    }
                }

                Collect(tree, box, mayUseContainIntrinsicSizeAuto);
            }
        }
    }

    /// <summary>Returns whether a style sets, resets or increments a counter, or is the style of a list item, or has style containment (see <see cref="CssCounters"/>).</summary>
    private static bool HasCounterOperations(ComputedStyle style)
        => style.GetComputedValue("counter-reset") != "none" || style.GetComputedValue("counter-increment") != "none" || style.GetComputedValue("counter-set") != "none"
            || IsListItem(style) || style.Layout.HasStyleContainment;

    private static bool IsListItem(ComputedStyle style) => style.GetComputedValue("display").Contains("list-item", StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the counter operations of the boxes of two subtrees are the same (see <see cref="CssCounters"/>): the
    /// operations of the elements and pseudo-elements of their boxes, in tree order. The elements and pseudo-elements without box
    /// have no counter operation (display: none and contents; the pseudo-elements with display: contents make the tree not
    /// patchable).
    /// </summary>
    private static bool CounterOperationsEqual(ContainerBox previous, ContainerBox box, HashSet<LayoutBox> kept)
    {
        var previousOperations = new List<CounterOperations>();
        var operations = new List<CounterOperations>();
        CollectCounterOperations(previous, previousOperations, kept);
        CollectCounterOperations(box, operations, kept);
        return previousOperations.SequenceEqual(operations);
    }

    private static void CollectCounterOperations(LayoutBox box, List<CounterOperations> operations, HashSet<LayoutBox> kept)
    {
        if (box.Element is { } element && (box is ContainerBox { IsAnonymous: false } || box.PseudoElement is not null) && HasCounterOperations(box.Style))
        {
            var style = box.Style;
            operations.Add(new CounterOperations(element, box.PseudoElement, style.GetComputedValue("counter-reset"), style.GetComputedValue("counter-increment"), style.GetComputedValue("counter-set"),
                IsListItem(style), style.Layout.HasStyleContainment));
        }

        // (the boxes of a kept box are kept: its placeholder in the new boxes has no children)
        if (box is ContainerBox container && !kept.Contains(box))
        {
            foreach (var child in container.Children)
            {
                CollectCounterOperations(child, operations, kept);
            }
        }
    }

    /// <summary>
    /// Returns a description of the first difference between a patched box tree and the tree built for the same document and
    /// styles, or null when they are the same: the boxes (their kinds, displays, elements, flags and texts, the styles of the
    /// boxes of the elements and the parents of the derived styles), the boxes by node and the lists of the trees. For the check
    /// of the incremental layout (see <see cref="LayoutEngine.VerifyIncrementalLayout"/>).
    /// </summary>
    internal static string? DescribeDifference(BoxTree patched, BoxTree built)
    {
        if (patched.Viewport is null || built.Viewport is null)
            return patched.Viewport == built.Viewport ? null : "the viewport box";

        var map = new Dictionary<LayoutBox, LayoutBox>(ReferenceEqualityComparer.Instance);
        if (DescribeDifference(patched.Viewport, built.Viewport, map) is { } difference)
            return difference;

        // (the boxes of the collapsible white space removed by the fixups stay in the boxes by node, as the boxes of the removed
        // text nodes until the tree is pruned: such a box is not in the tree)
        var builtBoxes = new HashSet<LayoutBox>(map.Values, ReferenceEqualityComparer.Instance);
        foreach (var ((node, pseudoElement), builtBox) in built.Boxes)
        {
            var inTree = builtBoxes.Contains(builtBox);
            if (!patched.Boxes.TryGetValue((node, pseudoElement), out var patchedBox) || (map.TryGetValue(patchedBox, out var mapped) ? !ReferenceEquals(mapped, builtBox) : inTree))
                return $"the box of {Describe(node)}{(pseudoElement is null ? "" : "::" + pseudoElement)} by node";
        }

        foreach (var ((node, pseudoElement), patchedBox) in patched.Boxes)
        {
            if (map.ContainsKey(patchedBox) && !built.Boxes.ContainsKey((node, pseudoElement)))
                return $"the box of {Describe(node)}{(pseudoElement is null ? "" : "::" + pseudoElement)} by node is not built";
        }

        if (!ListsEqual(patched.ContentVisibilityAutoBoxes, built.ContentVisibilityAutoBoxes, map) || !ListsEqual(patched.ContainIntrinsicSizeAutoBoxes, built.ContainIntrinsicSizeAutoBoxes, map))
            return "the boxes with content-visibility: auto or contain-intrinsic-size: auto";

        return null;

        static bool ListsEqual(List<ContainerBox> patched, List<ContainerBox> built, Dictionary<LayoutBox, LayoutBox> map)
        {
            if (patched.Count != built.Count)
                return false;

            for (var i = 0; i < patched.Count; i++)
            {
                if (!map.TryGetValue(patched[i], out var mapped) || !ReferenceEquals(mapped, built[i]))
                    return false;
            }

            return true;
        }
    }

    private static string? DescribeDifference(LayoutBox patched, LayoutBox built, Dictionary<LayoutBox, LayoutBox> map)
    {
        map[patched] = built;
        var description = BoxTree.Dump(built).Split('\n')[0].Trim();
        if (!ReferenceEquals(patched.Element, built.Element) || patched.PseudoElement != built.PseudoElement || patched.Display != built.Display || patched.GetType() != built.GetType())
            return $"{description}: the box is {BoxTree.Dump(patched).Split('\n')[0].Trim()}";

        if (!StylesEquivalent(patched.Style, built.Style))
            return $"{description}: the style";

        if (patched is TextRun patchedRun)
        {
            var builtRun = (TextRun)built;
            return patchedRun.Kind == builtRun.Kind && ReferenceEquals(patchedRun.Node, builtRun.Node) && patchedRun.Start == builtRun.Start && patchedRun.Length == builtRun.Length && patchedRun.Text.SequenceEqual(builtRun.Text)
                ? null
                : $"{description}: the text run is {BoxTree.Dump(patched).Trim()}";
        }

        var patchedBox = (ContainerBox)patched;
        var builtBox = (ContainerBox)built;
        if (patchedBox.Kind != builtBox.Kind || patchedBox.ChildrenAreInline != builtBox.ChildrenAreInline || patchedBox.IsOutsideMarker != builtBox.IsOutsideMarker
            || patchedBox.IsRubyWhiteSpace != builtBox.IsRubyWhiteSpace || patchedBox.SkipsContents != builtBox.SkipsContents || patchedBox.IsContentVisibilityAuto != builtBox.IsContentVisibilityAuto || patchedBox.GeneratedImage != builtBox.GeneratedImage
            || patchedBox.IsInTopLayer != builtBox.IsInTopLayer || patchedBox.IsFieldsetContent != builtBox.IsFieldsetContent || patchedBox.IsTextControlInnerEditor != builtBox.IsTextControlInnerEditor
            || patchedBox.IsFormControlContents != builtBox.IsFormControlContents || (patchedBox.Continuation is null) != (builtBox.Continuation is null)
            || (patchedBox.PreviousContinuation is null) != (builtBox.PreviousContinuation is null) || (patchedBox.FirstLineStyle is null) != (builtBox.FirstLineStyle is null)
            || (patchedBox.FirstLineStyle is not null && !StylesEquivalent(patchedBox.FirstLineStyle, builtBox.FirstLineStyle!)))
        {
            return $"{description}: the flags of the box";
        }

        var patchedChild = patchedBox.FirstChild;
        var builtChild = builtBox.FirstChild;
        while (patchedChild is not null && builtChild is not null)
        {
            if (DescribeDifference(patchedChild, builtChild, map) is { } difference)
                return difference;

            patchedChild = patchedChild.NextSibling;
            builtChild = builtChild.NextSibling;
        }

        return patchedChild is null && builtChild is null ? null : $"{description}: the children ({(patchedChild is null ? "missing " + BoxTree.Dump(builtChild!).Split('\n')[0].Trim() : "extra " + BoxTree.Dump(patchedChild).Split('\n')[0].Trim())})";

        // The styles of the elements are the styles of the style engine; the styles derived from them are kept with them (see
        // ComputedStyle.GetDerivedStyle), except the ones derived from the style of the root element, created by each build
        // (the layout replaces the style of an anchor-positioned box by the style of its position option, see AnchorPositioning)
        static bool StylesEquivalent(ComputedStyle patched, ComputedStyle built)
        {
            patched = patched.PositionBaseStyle ?? patched;
            built = built.PositionBaseStyle ?? built;
            return ReferenceEquals(patched, built) || (ReferenceEquals(patched.Element, built.Element) && patched.PseudoElement == built.PseudoElement && ReferenceEquals(patched.Parent, built.Parent) && patched.Parent is { IsRoot: true });
        }
    }

    private static string Describe(Node node) => node is Element element ? "<" + element.LocalName + (element.Id is { Length: > 0 } id ? "#" + id : "") + ">" : node.NodeName;

    /// <summary>The counter operations of an element or a pseudo-element (see <see cref="CounterOperationsEqual"/>).</summary>
    private readonly record struct CounterOperations(Element Element, string? PseudoElement, string Reset, string Increment, string Set, bool IsListItem, bool HasStyleContainment);
}
