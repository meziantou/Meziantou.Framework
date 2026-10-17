using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// content-visibility (https://drafts.csswg.org/css-contain-2/#content-visibility) and the last remembered sizes
// (https://drafts.csswg.org/css-sizing-4/#last-remembered): the skipped contents, the relevance to the user of the elements with
// content-visibility: auto, and the steps of the rendering update that depend on the layout.
internal sealed partial class LayoutEngine
{
    /// <summary>The elements with <c>content-visibility: auto</c> at the last rendering update (their state is forgotten when they lose it).</summary>
    private List<Element>? _contentVisibilityAutoElements;

    /// <summary>
    /// The boxes (by element and pseudo-element, as the box tree may be built again) whose skipped contents are laid out for the
    /// geometry APIs (see <see cref="ContainerBox.LaysOutSkippedContents"/>), until the next rendering update.
    /// </summary>
    private HashSet<(Element Element, string? PseudoElement)>? _laidOutSkippedContents;

    /// <summary>The elements that have a last remembered size (see <see cref="LastRememberedSizes"/>).</summary>
    private HashSet<Element>? _elementsWithLastRememberedSize;

    /// <summary>The root of the fragment tree of the last layout, without computing the layout.</summary>
    internal BoxFragment? LaidOutRoot => _root;

    /// <summary>Returns the first fragment of a box in the last layout, without computing the layout (null when the box was not laid out).</summary>
    internal Fragment? GetLaidOutFirstFragment(LayoutBox box) => GetFirstFragment(box);

    /// <summary>
    /// Called when the relevance to the user of the elements with <c>content-visibility: auto</c> may have changed without a
    /// change of the styles (e.g. the selection): the next layout pass updates their skipped state.
    /// </summary>
    internal void OnContentRelevancyChanged()
    {
        if (_boxTree is { ContentVisibilityAutoBoxes.Count: > 0 })
        {
            _needsLayout = true;

            // The rendering update fires the contentvisibilityautostatechange events
            _document.EventLoop?.RequestRenderingOpportunity(_document);
        }
    }

    /// <summary>
    /// Updates whether the boxes with <c>content-visibility: auto</c> skip their contents (https://drafts.csswg.org/css-contain-2/#valdef-content-visibility-auto):
    /// "If the element is not relevant to the user, it also skips its contents".
    /// </summary>
    private static void UpdateSkippedContents(BoxTree tree)
    {
        foreach (var box in tree.ContentVisibilityAutoBoxes)
        {
            if (box.Element is { } element)
            {
                var skipsContents = !ContentRelevancy.IsRelevantToTheUser(element);
                if (box.SkipsContents != skipsContents)
                {
                    box.SkipsContents = skipsContents;
                    LayoutResultCache.Invalidate(box);
                }
            }
        }
    }

    /// <summary>The boxes that lay out their skipped contents (see <see cref="ContainerBox.LaysOutSkippedContents"/>), or null.</summary>
    private HashSet<ContainerBox>? _boxesLayingOutSkippedContents;

    /// <summary>
    /// Makes the boxes of a patched box tree (see <see cref="BoxTree.TryPatch"/>) lay out their skipped contents as the boxes of a
    /// new tree: only the ones whose skipped contents a geometry API needed since the last rendering update (see
    /// <see cref="ApplyLaidOutSkippedContents"/>).
    /// </summary>
    private void ResetLaidOutSkippedContents()
    {
        if (_boxesLayingOutSkippedContents is null)
            return;

        foreach (var box in _boxesLayingOutSkippedContents)
        {
            box.LaysOutSkippedContents = false;
            LayoutResultCache.Invalidate(box);
        }

        _boxesLayingOutSkippedContents = null;
    }

    private void SetLaysOutSkippedContents(ContainerBox box)
    {
        if (box.LaysOutSkippedContents)
            return;

        box.LaysOutSkippedContents = true;
        LayoutResultCache.Invalidate(box);
        (_boxesLayingOutSkippedContents ??= new(ReferenceEqualityComparer.Instance)).Add(box);
    }

    /// <summary>Marks the boxes whose skipped contents a geometry API needed since the last rendering update (the box tree may have been built again).</summary>
    private void ApplyLaidOutSkippedContents(BoxTree tree)
    {
        if (_laidOutSkippedContents is not { Count: > 0 } keys)
            return;

        foreach (var (element, pseudoElement) in keys)
        {
            var box = pseudoElement is null ? tree.GetPrincipalBox(element) : tree.GetPseudoElementBox(element, pseudoElement);
            if (box is not null)
            {
                SetLaysOutSkippedContents(box);
            }
        }
    }

    /// <summary>
    /// Lays out the contents of an element when it skips them, or the skipped contents that contain it, for the geometry APIs
    /// that depend on its contents (e.g. its scrolling area). The layout is computed when it is out of date.
    /// </summary>
    public void EnsureContentsLaidOut(Element element)
    {
        EnsureLayout();
        if (_boxTree?.GetPrincipalBox(element) is { } box)
        {
            EnsureSkippedContentsLaidOut(box, includeSelf: true);
        }
    }

    /// <summary>
    /// Lays out the skipped contents that contain a box (and the ones of the box itself when <paramref name="includeSelf"/> is
    /// true) when they are not laid out: a geometry API reads them, so "the user-agent cannot avoid rendering work"
    /// (https://drafts.csswg.org/css-contain-2/#cv-examples). The contents are still not painted nor hit tested, and they do not
    /// change the size of the box that skips them (it has size containment). Returns whether the layout was computed again (the
    /// box tree may have been built again: the boxes must be resolved again).
    /// </summary>
    private bool EnsureSkippedContentsLaidOut(LayoutBox box, bool includeSelf)
    {
        if (_inLayout)
            return false;

        var changed = false;
        for (var current = includeSelf ? box as ContainerBox ?? box.Parent : box.Parent; current is not null; current = current.Parent)
        {
            if (current.HasUnlaidSkippedContents && current.Element is { } element)
            {
                SetLaysOutSkippedContents(current);
                (_laidOutSkippedContents ??= []).Add((element, current.PseudoElement));
                changed = true;
            }
        }

        if (changed)
        {
            _needsLayout = true;
            UpdateLayout();
        }

        return changed;
    }

    /// <summary>
    /// Runs the steps of the rendering update that depend on the layout of the document
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering): while true, recalculates the styles and
    /// updates the layout; the layout is computed again while the initial determination of the proximity to the viewport of an
    /// element with <c>content-visibility: auto</c> makes it relevant to the user ("update content relevancy for a document",
    /// which updates the "currently relevant to the user" flags and fires the <c>contentvisibilityautostatechange</c> events);
    /// then the last remembered sizes are recorded ("at the time that ResizeObserver events are determined",
    /// https://drafts.csswg.org/css-sizing-4/#last-remembered), the active resize observations deeper than the depth of the
    /// last broadcast are gathered, and the loop ends when there is none, else they are broadcast. Finally, the resize loop
    /// error is delivered when observations were skipped. The snapshot post-layout state steps run once after each style and
    /// layout update that precedes a gathering of the resize observations (the states of the scroll-state query containers, see
    /// <see cref="UpdateScrollStateSnapshots"/>): the styles and the layout are updated again when a snapshot changed.
    /// </summary>
    internal void UpdateRendering()
    {
        // The rendering does not need the skipped contents: the next geometry API that needs them lays them out again
        _laidOutSkippedContents?.Clear();

        // Each element is initially determined once, so the loop ends; the depth of the resize observations increases
        var updateNonInitial = true;
        var proximityChanged = false;
        var resizeObserverDepth = 0;
        var didRunSnapshotPostLayoutStateSteps = false;
        while (true)
        {
            UpdateLayout();
            if (_boxTree is { ContentVisibilityAutoBoxes: { Count: > 0 } autoBoxes })
            {
                var hadInitialVisibleDetermination = ContentRelevancy.UpdateContentRelevancy(this, autoBoxes, updateNonInitial, out var changed);
                proximityChanged |= changed;
                updateNonInitial = false;
                if (hadInitialVisibleDetermination)
                    continue;
            }

            // "If didRunSnapshotPostLayoutStateSteps is false: run snapshot post-layout state steps for doc, set
            // didRunSnapshotPostLayoutStateSteps to true, and if any snapshotted state changed, then continue"
            // (https://github.com/whatwg/html/pull/11613): the scroll-state query containers
            if (!didRunSnapshotPostLayoutStateSteps)
            {
                didRunSnapshotPostLayoutStateSteps = true;

                // The active scroll markers and the scroll buttons (https://drafts.csswg.org/css-overflow-5/#active-scroll-marker)
                var scrollStateChanged = UpdateScrollStateSnapshots();
                if (UpdateScrollMarkerSnapshots(onlyNew: false) || scrollStateChanged)
                    continue;
            }

            didRunSnapshotPostLayoutStateSteps = false;

            // https://drafts.csswg.org/css-anchor-position-1/#last-successful-recording: recorded with the last remembered sizes
            RecordLastSuccessfulPositionOptions();
            if (_boxTree is { } tree && (tree.ContainIntrinsicSizeAutoBoxes.Count > 0 || _elementsWithLastRememberedSize is { Count: > 0 }))
            {
                LastRememberedSizes.Record(this, _document, tree.ContainIntrinsicSizeAutoBoxes, _elementsWithLastRememberedSize ??= new(ReferenceEqualityComparer.Instance));
            }

            // https://drafts.csswg.org/resize-observer-1/#gather-active-observations-h (the callbacks may make the document inactive)
            if (!_document.HasResizeObservers || !_document.IsFullyActive || !_document.GatherActiveResizeObservations(resizeObserverDepth))
                break;

            resizeObserverDepth = _document.BroadcastActiveResizeObservations();
        }

        if (_document.HasSkippedResizeObservations())
        {
            _document.DeliverResizeLoopErrorNotification();
        }

        UpdateContentVisibilityAutoElements(proximityChanged);
        QueueDeferredScrollEvents();
    }

    /// <summary>Forgets the state of the elements that lost <c>content-visibility: auto</c>, and schedules the rendering update that fires the events of the proximities determined by this one.</summary>
    private void UpdateContentVisibilityAutoElements(bool proximityChanged)
    {
        var tree = _boxTree;
        if (tree is null)
            return;

        // The proximities determined by this rendering update take effect at the next one, which fires their events
        if (proximityChanged && _document.EventLoop is { } eventLoop)
        {
            eventLoop.RequestRenderingOpportunity(_document);
        }

        if (tree.ContentVisibilityAutoBoxes.Count > 0 || _contentVisibilityAutoElements is { Count: > 0 })
        {
            if (_contentVisibilityAutoElements is not null)
            {
                ContentRelevancy.ForgetStates(_contentVisibilityAutoElements, tree.ContentVisibilityAutoBoxes);
            }

            _contentVisibilityAutoElements ??= [];
            _contentVisibilityAutoElements.Clear();
            foreach (var box in tree.ContentVisibilityAutoBoxes)
            {
                if (box.Element is { } element && ReferenceEquals(tree.GetPrincipalBox(element), box))
                {
                    _contentVisibilityAutoElements.Add(element);
                }
            }
        }
    }
}
